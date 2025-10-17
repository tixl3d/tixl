#nullable enable
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using T3.Core.Utils;

// ReSharper disable MemberCanBePrivate.Global

namespace Lib.io.dmx;

[Guid("e5a8d9e6-3c5a-4bbb-9da3-737b6330b9c3")]
internal sealed class SacnOutput : Instance<SacnOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    private const int SacnPort = 5568;
    private const string SacnDiscoveryIp = "239.255.250.214";
    private readonly byte[] _cid = Guid.NewGuid().ToByteArray();

    // --- State and Configuration ---
    private readonly ConnectionSettings _connectionSettings = new();
    private readonly object _dataLock = new();
    private readonly ConcurrentDictionary<string, string> _discoveredSources = new();
    private readonly byte[] _packetBuffer = new byte[126 + 512]; // Reusable buffer for zero-allocation packet creation

    [Output(Guid = "a3c4a2e8-bc1b-453a-9773-1952a6ea10a3")]
    public readonly Slot<Command> Result = new();

    private bool _connected;

    // --- Discovery Resources ---
    private Thread? _discoveryListenerThread;
    private UdpClient? _discoveryUdpClient;
    private List<(int universe, byte[] data)>? _dmxDataToSend;
    private volatile bool _isDiscovering;
    private string? _lastErrorMessage;
    private IStatusProvider.StatusLevel _lastStatusLevel = IStatusProvider.StatusLevel.Notice;
    private SacnPacketOptions _packetOptions;
    private volatile bool _printToLog;
    private CancellationTokenSource? _senderCts;

    // --- High-Performance Sending Resources ---
    private Thread? _senderThread;

    // --- Network and Connection Management ---
    private Socket? _socket;
    private bool _wasSendingLastFrame;

    public SacnOutput()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context);

        var settingsChanged = _connectionSettings.Update(
                                                         LocalIpAddress.GetValue(context),
                                                         TargetIpAddress.GetValue(context),
                                                         SendUnicast.GetValue(context)
                                                        );

        if (Reconnect.GetValue(context) || settingsChanged)
        {
            Reconnect.SetTypedInputValue(false);
            if (_printToLog) Log.Debug("sACN Output: Reconnecting sACN socket...", this);
            CloseSocket();
            _connected = TryConnectSacn(_connectionSettings.LocalIp);
        }

        var discoverSources = DiscoverSources.GetValue(context);
        if (discoverSources && !_isDiscovering) StartSacnDiscovery();
        else if (!discoverSources && _isDiscovering) StopSacnDiscovery();

        var enableSending = SendTrigger.GetValue(context);
        if (enableSending != _wasSendingLastFrame)
        {
            if (enableSending) StartSenderThread();
            else StopSenderThread();
            _wasSendingLastFrame = enableSending;
        }

        if (!enableSending)
        {
            SetStatus("Sending is disabled. Enable 'Send Trigger'.", IStatusProvider.StatusLevel.Notice);
            return;
        }

        if (!_connected)
        {
            SetStatus($"Not connected. {(_lastErrorMessage ?? "Check settings.")}", IStatusProvider.StatusLevel.Warning);
            return;
        }

        SetStatus("Connected and sending.", IStatusProvider.StatusLevel.Success);

        // --- Prepare Data for Sending Thread ---
        var startUniverse = Math.Max(1, StartUniverse.GetValue(context));
        var inputValueLists = InputsValues.GetCollectedTypedInputs();

        var preparedData = new List<(int universe, byte[] data)>();
        var universeIndex = startUniverse;

        foreach (var input in inputValueLists)
        {
            var buffer = input.GetValue(context);
            if (buffer == null) continue;

            for (var i = 0; i < buffer.Count; i += 512)
            {
                var chunkCount = Math.Min(buffer.Count - i, 512);
                if (chunkCount == 0) continue;

                var dmxData = new byte[chunkCount];
                for (var j = 0; j < chunkCount; j++)
                {
                    dmxData[j] = (byte)buffer[i + j].Clamp(0, 255);
                }

                preparedData.Add((universeIndex, dmxData));
                universeIndex++;
            }
        }

        // --- Safely pass prepared data to the sender thread ---
        lock (_dataLock)
        {
            _dmxDataToSend = preparedData;
            _packetOptions = new SacnPacketOptions
                                 {
                                     MaxFps = MaxFps.GetValue(context),
                                     Priority = (byte)Priority.GetValue(context).Clamp(0, 200),
                                     SourceName = SourceName.GetValue(context),
                                     EnableSync = EnableSync.GetValue(context),
                                     SyncUniverse = (ushort)SyncUniverse.GetValue(context).Clamp(1, 63999)
                                 };
        }
    }

    #region Sender Thread Management and Loop
    private void StartSenderThread()
    {
        if (_senderThread != null) return;

        if (_printToLog) Log.Debug("sACN Output: Starting sender thread.", this);
        _senderCts = new CancellationTokenSource();
        var token = _senderCts.Token;

        _senderThread = new Thread(() => SenderLoop(token))
                            {
                                IsBackground = true, Name = "sACNSender", Priority = ThreadPriority.AboveNormal
                            };
        _senderThread.Start();
    }

    private void StopSenderThread()
    {
        if (_senderThread == null) return;

        if (_printToLog) Log.Debug("sACN Output: Stopping sender thread.", this);
        _senderCts?.Cancel();
        if (_senderThread.Join(500))
        {
            _senderCts?.Dispose();
        }

        _senderCts = null;
        _senderThread = null;
    }

    private void SenderLoop(CancellationToken token)
    {
        var stopwatch = new Stopwatch();
        long nextFrameTimeTicks = 0;
        byte sequenceNumber = 0;

        while (!token.IsCancellationRequested)
        {
            // --- Copy shared data under lock ---
            List<(int universe, byte[] data)>? dataCopy;
            SacnPacketOptions optionsCopy;
            lock (_dataLock)
            {
                dataCopy = _dmxDataToSend;
                optionsCopy = _packetOptions;
            }

            // --- Frame Rate Limiting ---
            if (optionsCopy.MaxFps > 0)
            {
                if (!stopwatch.IsRunning) stopwatch.Start();
                long now = stopwatch.ElapsedTicks;
                if (now < nextFrameTimeTicks)
                {
                    if (nextFrameTimeTicks - now > Stopwatch.Frequency / 1000) Thread.Sleep(1);
                    else Thread.SpinWait(100);
                    continue;
                }

                if (now > nextFrameTimeTicks + Stopwatch.Frequency) nextFrameTimeTicks = now;
                nextFrameTimeTicks += (long)(Stopwatch.Frequency / (double)optionsCopy.MaxFps);
            }

            // --- Send Data (Lock socket access) ---
            lock (_connectionSettings)
            {
                var currentSocket = _socket;
                if (currentSocket == null || !_connected)
                {
                    Thread.Sleep(100);
                    continue;
                }

                if (dataCopy != null)
                {
                    foreach (var (universe, data) in dataCopy)
                    {
                        if (token.IsCancellationRequested) break;
                        try
                        {
                            var packetLength = BuildSacnDataPacket(universe, optionsCopy, data, sequenceNumber);
                            var targetEndPoint = (_connectionSettings.SendUnicast && _connectionSettings.TargetIp != null)
                                                     ? new IPEndPoint(_connectionSettings.TargetIp, SacnPort)
                                                     : new IPEndPoint(GetSacnMulticastAddress(universe), SacnPort);
                            currentSocket.SendTo(_packetBuffer, packetLength, SocketFlags.None, targetEndPoint);
                        }
                        catch (Exception e)
                        {
                            if (_printToLog) Log.Warning($"sACN Output send failed for universe {universe}: {e.Message}", this);
                            _connected = false;
                            break; // Stop sending if an error occurs
                        }
                    }
                }

                if (optionsCopy.EnableSync)
                {
                    SendSacnSync(currentSocket, optionsCopy.SyncUniverse, sequenceNumber);
                }
            }

            sequenceNumber++;
        }
    }
    #endregion

    #region Packet Sending (Zero-Allocation)
    private void SendSacnSync(Socket socket, ushort syncAddress, byte sequenceNumber)
    {
        try
        {
            var packetLength = BuildSacnSyncPacket(syncAddress, sequenceNumber);
            var syncEndPoint = new IPEndPoint(GetSacnMulticastAddress(syncAddress), SacnPort);
            socket.SendTo(_packetBuffer, packetLength, SocketFlags.None, syncEndPoint);
        }
        catch (Exception e)
        {
            if (_printToLog) Log.Warning($"sACN Output: Failed to send sACN sync packet to universe {syncAddress}: {e.Message}", this);
            _connected = false;
        }
    }

    private int BuildSacnSyncPacket(ushort syncUniverse, byte sequenceNumber)
    {
        // Root Layer
        _packetBuffer[0] = 0x00;
        _packetBuffer[1] = 0x10; // Preamble
        _packetBuffer[2] = 0x00;
        _packetBuffer[3] = 0x00; // Post-amble
        Encoding.ASCII.GetBytes("ASC-E1.17", 0, 10, _packetBuffer, 4); // ID
        _packetBuffer[14] = 0x00;
        _packetBuffer[15] = 0x00;
        short rootFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | 33));
        _packetBuffer[16] = (byte)(rootFlagsAndLength >> 8);
        _packetBuffer[17] = (byte)(rootFlagsAndLength & 0xFF);
        int vector = IPAddress.HostToNetworkOrder(0x00000004); // VECTOR_ROOT_E131_EXTENDED
        _packetBuffer[18] = (byte)(vector >> 24);
        _packetBuffer[19] = (byte)(vector >> 16);
        _packetBuffer[20] = (byte)(vector >> 8);
        _packetBuffer[21] = (byte)(vector & 0xFF);
        Array.Copy(_cid, 0, _packetBuffer, 22, 16);

        // E1.31 Framing Layer
        short frameFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | 9));
        _packetBuffer[38] = (byte)(frameFlagsAndLength >> 8);
        _packetBuffer[39] = (byte)(frameFlagsAndLength & 0xFF);
        int frameVector = IPAddress.HostToNetworkOrder(0x00000001); // VECTOR_E131_EXTENDED_SYNCHRONIZATION
        _packetBuffer[40] = (byte)(frameVector >> 24);
        _packetBuffer[41] = (byte)(frameVector >> 16);
        _packetBuffer[42] = (byte)(frameVector >> 8);
        _packetBuffer[43] = (byte)(frameVector & 0xFF);
        _packetBuffer[44] = sequenceNumber;
        short syncUni = IPAddress.HostToNetworkOrder((short)syncUniverse);
        _packetBuffer[45] = (byte)(syncUni >> 8);
        _packetBuffer[46] = (byte)(syncUni & 0xFF);
        _packetBuffer[47] = 0x00;
        _packetBuffer[48] = 0x00; // Reserved

        return 49;
    }

    private int BuildSacnDataPacket(int universe, SacnPacketOptions options, byte[] dmxData, byte sequenceNumber)
    {
        var dmxLength = (short)dmxData.Length;

        // Root Layer (38 bytes)
        _packetBuffer[0] = 0x00;
        _packetBuffer[1] = 0x10;
        _packetBuffer[2] = 0x00;
        _packetBuffer[3] = 0x00;
        Encoding.ASCII.GetBytes("ASC-E1.17", 0, 10, _packetBuffer, 4);
        _packetBuffer[14] = 0x00;
        _packetBuffer[15] = 0x00;
        short rootFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | (110 + dmxLength)));
        _packetBuffer[16] = (byte)(rootFlagsAndLength >> 8);
        _packetBuffer[17] = (byte)(rootFlagsAndLength & 0xFF);
        int vector = IPAddress.HostToNetworkOrder(0x00000004);
        _packetBuffer[18] = (byte)(vector >> 24);
        _packetBuffer[19] = (byte)(vector >> 16);
        _packetBuffer[20] = (byte)(vector >> 8);
        _packetBuffer[21] = (byte)(vector & 0xFF);
        Array.Copy(_cid, 0, _packetBuffer, 22, 16);

        // E1.31 Framing Layer (88 bytes)
        short frameFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | (88 + dmxLength)));
        _packetBuffer[38] = (byte)(frameFlagsAndLength >> 8);
        _packetBuffer[39] = (byte)(frameFlagsAndLength & 0xFF);
        int frameVector = IPAddress.HostToNetworkOrder(0x00000002); // VECTOR_E131_DATA_PACKET
        _packetBuffer[40] = (byte)(frameVector >> 24);
        _packetBuffer[41] = (byte)(frameVector >> 16);
        _packetBuffer[42] = (byte)(frameVector >> 8);
        _packetBuffer[43] = (byte)(frameVector & 0xFF);
        Array.Clear(_packetBuffer, 44, 64); // Clear source name area
        Encoding.UTF8.GetBytes(options.SourceName, 0, Math.Min(options.SourceName.Length, 63), _packetBuffer, 44);
        _packetBuffer[108] = options.Priority;
        short syncUni = IPAddress.HostToNetworkOrder((short)(options.EnableSync ? options.SyncUniverse : 0));
        _packetBuffer[109] = (byte)(syncUni >> 8);
        _packetBuffer[110] = (byte)(syncUni & 0xFF);
        _packetBuffer[111] = sequenceNumber;
        _packetBuffer[112] = 0x00; // Options
        short netUniverse = IPAddress.HostToNetworkOrder((short)universe);
        _packetBuffer[113] = (byte)(netUniverse >> 8);
        _packetBuffer[114] = (byte)(netUniverse & 0xFF);

        // DMP Layer
        short dmpFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | (11 + dmxLength)));
        _packetBuffer[115] = (byte)(dmpFlagsAndLength >> 8);
        _packetBuffer[116] = (byte)(dmpFlagsAndLength & 0xFF);
        _packetBuffer[117] = 0x02; // Vector
        _packetBuffer[118] = 0xa1; // Address Type & Data Type
        _packetBuffer[119] = 0x00;
        _packetBuffer[120] = 0x00; // First address
        _packetBuffer[121] = 0x00;
        _packetBuffer[122] = 0x01; // Address increment
        short propValueCount = IPAddress.HostToNetworkOrder((short)(dmxLength + 1));
        _packetBuffer[123] = (byte)(propValueCount >> 8);
        _packetBuffer[124] = (byte)(propValueCount & 0xFF);
        _packetBuffer[125] = 0x00; // DMX Start Code
        Array.Copy(dmxData, 0, _packetBuffer, 126, dmxLength);

        return 126 + dmxLength;
    }
    #endregion

    #region Discovery
    private void StartSacnDiscovery()
    {
        if (_printToLog) Log.Debug("sACN Output: Starting sACN Discovery Listener...", this);
        _isDiscovering = true;
        _discoveredSources.Clear();
        _discoveryListenerThread = new Thread(ListenForSacnDiscovery) { IsBackground = true, Name = "sACNDiscoveryListener" };
        _discoveryListenerThread.Start();
    }

    private void StopSacnDiscovery()
    {
        if (!_isDiscovering) return;
        if (_printToLog) Log.Debug("sACN Output: Stopping sACN Discovery.", this);
        _isDiscovering = false;
        _discoveryUdpClient?.Close(); // This will unblock the Receive call
        _discoveryListenerThread?.Join(200);
        _discoveryListenerThread = null;
    }

    private void ListenForSacnDiscovery()
    {
        try
        {
            _discoveryUdpClient = new UdpClient();
            var localEp = new IPEndPoint(IPAddress.Any, SacnPort);
            _discoveryUdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _discoveryUdpClient.Client.Bind(localEp);
            _discoveryUdpClient.JoinMulticastGroup(IPAddress.Parse(SacnDiscoveryIp));

            while (_isDiscovering)
            {
                try
                {
                    var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                    var data = _discoveryUdpClient.Receive(ref remoteEp);
                    if (data.Length <= 125) continue;

                    var sourceName = Encoding.UTF8.GetString(data, 44, 64).TrimEnd('\0');
                    var ipString = remoteEp.Address.ToString();
                    var displayName = string.IsNullOrWhiteSpace(sourceName) ? ipString : sourceName;

                    _discoveredSources[ipString] = $"{displayName} ({ipString})";
                }
                catch (SocketException)
                {
                    if (_isDiscovering) break;
                } // Break loop if the socket is closed
                catch (Exception e)
                {
                    if (_isDiscovering) Log.Error($"sACN discovery listener error: {e.Message}", this);
                }
            }
        }
        catch (Exception e)
        {
            if (_isDiscovering) Log.Error($"sACN discovery listener failed to bind: {e.Message}", this);
        }
        finally
        {
            _discoveryUdpClient?.Close();
            _discoveryUdpClient = null;
        }
    }
    #endregion

    #region Connection and Lifecycle
    public void Dispose()
    {
        StopSenderThread();
        StopSacnDiscovery();
        CloseSocket();
    }

    private void CloseSocket()
    {
        lock (_connectionSettings)
        {
            if (_socket == null) return;
            if (_printToLog) Log.Debug("sACN Output: Closing socket.", this);
            try
            {
                _socket.Close();
            }
            catch
            {
                /* Ignore */
            }

            _socket = null;
            _connected = false;
            _lastErrorMessage = "Socket closed.";
        }
    }

    private bool TryConnectSacn(IPAddress? localIp)
    {
        lock (_connectionSettings)
        {
            if (localIp == null)
            {
                _lastErrorMessage = "Local IP Address is not valid.";
                return false;
            }

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
                _socket.Bind(new IPEndPoint(localIp, 0)); // Bind to a dynamic port for sending
                _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 10);
                _lastErrorMessage = null;
                if (_printToLog) Log.Debug($"sACN Output: Socket bound to {localIp}.", this);
                return _connected = true;
            }
            catch (Exception e)
            {
                _lastErrorMessage = $"Failed to bind sACN socket to {localIp}: {e.Message}";
                CloseSocket();
                return false;
            }
        }
    }
    #endregion

    #region Helpers and Static Members
    private static IPAddress GetSacnMulticastAddress(int universe)
    {
        var u = (ushort)universe.Clamp(1, 63999);
        return new IPAddress(new byte[] { 239, 255, (byte)(u >> 8), (byte)(u & 0xFF) });
    }

    private static IEnumerable<string> GetLocalIPv4Addresses()
    {
        yield return "127.0.0.1";
        if (!NetworkInterface.GetIsNetworkAvailable()) yield break;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ipInfo in ni.GetIPProperties().UnicastAddresses)
                if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                    yield return ipInfo.Address.ToString();
        }
    }

    private struct SacnPacketOptions
    {
        public int MaxFps;
        public byte Priority;
        public string SourceName;
        public bool EnableSync;
        public ushort SyncUniverse;
    }

    private sealed class ConnectionSettings
    {
        private string? _lastLocalIpStr, _lastTargetIpStr;
        private bool _lastSendUnicast;
        public IPAddress? LocalIp { get; private set; }
        public IPAddress? TargetIp { get; private set; }
        public bool SendUnicast { get; private set; }

        public bool Update(string? localIpStr, string? targetIpStr, bool sendUnicast)
        {
            if (_lastLocalIpStr == localIpStr && _lastTargetIpStr == targetIpStr && _lastSendUnicast == sendUnicast) return false;

            _lastLocalIpStr = localIpStr;
            _lastTargetIpStr = targetIpStr;
            _lastSendUnicast = sendUnicast;
            SendUnicast = sendUnicast;

            IPAddress.TryParse(localIpStr, out var parsedLocalIp);
            LocalIp = parsedLocalIp;

            IPAddress.TryParse(targetIpStr, out var parsedTargetIp);
            TargetIp = sendUnicast ? parsedTargetIp : null;

            return true;
        }
    }
    #endregion

    #region IStatusProvider and ICustomDropdownHolder
    public IStatusProvider.StatusLevel GetStatusLevel() => _lastStatusLevel;
    public string? GetStatusMessage() => _lastErrorMessage;

    public void SetStatus(string m, IStatusProvider.StatusLevel l)
    {
        _lastErrorMessage = m;
        _lastStatusLevel = l;
    }

    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == LocalIpAddress.Id) return LocalIpAddress.Value;
        if (inputId == TargetIpAddress.Id) return TargetIpAddress.Value;
        return string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId == LocalIpAddress.Id)
        {
            foreach (var address in GetLocalIPv4Addresses())
            {
                yield return address;
            }
        }
        else if (inputId == TargetIpAddress.Id)
        {
            if (!_isDiscovering && _discoveredSources.IsEmpty)
            {
                yield return "Enable 'Discover Sources' to search...";
            }
            else if (_isDiscovering && _discoveredSources.IsEmpty)
            {
                yield return "Searching for sources...";
            }
            else
            {
                foreach (var sourceName in _discoveredSources.Values.OrderBy(name => name))
                {
                    yield return sourceName;
                }
            }
        }
        // Added an explicit else block to ensure all paths return a value.
        else
        {
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (string.IsNullOrEmpty(selected) || !isAListItem) return;

        if (inputId == LocalIpAddress.Id)
        {
            LocalIpAddress.SetTypedInputValue(selected);
        }
        else if (inputId == TargetIpAddress.Id)
        {
            var match = Regex.Match(selected, @"\(([^)]*)\)");
            TargetIpAddress.SetTypedInputValue(match.Success ? match.Groups[1].Value : selected);
        }
    }
    #endregion

    #region Inputs
    [Input(Guid = "2a8d39a3-5a41-477d-815a-8b8b9d8b1e4a")]
    public readonly MultiInputSlot<List<int>> InputsValues = new();

    [Input(Guid = "1b26f5d5-8141-4b13-b88d-6859ed5a4af8")]
    public readonly InputSlot<int> StartUniverse = new(1);

    [Input(Guid = "9C233633-959F-4447-B248-4D431C1B18E7")]
    public readonly InputSlot<string> LocalIpAddress = new("127.0.0.1");

    [Input(Guid = "9B8A7C6D-5E4F-4012-3456-7890ABCDEF12")]
    public readonly InputSlot<bool> SendTrigger = new();

    [Input(Guid = "C2D3E4F5-A6B7-4890-1234-567890ABCDEF")]
    public readonly InputSlot<bool> Reconnect = new();

    [Input(Guid = "8C6C9A8D-29C5-489E-8C6B-9E4A3C1E2B6A")]
    public readonly InputSlot<bool> SendUnicast = new();

    [Input(Guid = "D9E8D7C6-B5A4-434A-9E3A-4E2B1D0C9A7B")]
    public readonly InputSlot<string> TargetIpAddress = new();

    [Input(Guid = "3F25C04C-0A88-42FB-93D3-05992B861E61")]
    public readonly InputSlot<bool> DiscoverSources = new();

    [Input(Guid = "4A9E2D3B-8C6F-4B1D-8D7E-9F3A5B2C1D0E")]
    public readonly InputSlot<int> Priority = new(100);

    [Input(Guid = "5B1D9C8A-7E3F-4A2B-9C8D-1E0F3A5B2C1D")]
    public readonly InputSlot<string> SourceName = new("T3 sACN Output");

    [Input(Guid = "6F5C4B3A-2E1D-4F9C-8A7B-3D2E1F0C9B8A")]
    public readonly InputSlot<int> MaxFps = new(60);

    [Input(Guid = "7A8B9C0D-1E2F-3A4B-5C6D-7E8F9A0B1C2D")]
    public readonly InputSlot<bool> EnableSync = new();

    [Input(Guid = "8B9C0D1E-2F3A-4B5C-6D7E-8F9A0B1C2D3E")]
    public readonly InputSlot<int> SyncUniverse = new(64001);

    [Input(Guid = "D0E1F2A3-B4C5-4678-9012-3456789ABCDE")]
    public readonly InputSlot<bool> PrintToLog = new();
    #endregion
}