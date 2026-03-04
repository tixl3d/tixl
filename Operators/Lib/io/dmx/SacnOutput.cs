#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Core.Utils;

// ReSharper disable MemberCanBePrivate.Global

namespace Lib.io.dmx;

[Guid("e5a8d9e6-3c5a-4bbb-9da3-737b6330b9c3")]
internal sealed class SacnOutput : Instance<SacnOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    private const int SacnPort = 5568;
    private const string SacnDiscoveryIp = "239.255.250.214";

    [Output(Guid = "a3c4a2e8-bc1b-453a-9773-1952a6ea10a3")]
    public readonly Slot<Command> Result = new();

    // --- State and Configuration ---
    private readonly ConnectionSettings _connectionSettings = new();
    private volatile bool _printToLog;
    private bool _wasSendingLastFrame;
    private string? _lastErrorMessage;
    private IStatusProvider.StatusLevel _lastStatusLevel = IStatusProvider.StatusLevel.Notice;
    private readonly byte[] _cid = Guid.NewGuid().ToByteArray();
    private double _lastNetworkRefreshTime;

    // --- High-Performance Sending Resources ---
    private Thread? _senderThread;
    private CancellationTokenSource? _senderCts;
    private readonly object _dataLock = new();
    private List<(int universe, byte[] data)>? _dmxDataToSend;
    private SacnPacketOptions _packetOptions;
    private readonly byte[] _packetBuffer = new byte[126 + 512]; // Reusable buffer for zero-allocation packet creation

    // --- Discovery Resources ---
    private Thread? _discoveryListenerThread;
    private volatile bool _isDiscovering;
    private UdpClient? _discoveryUdpClient;
    private readonly ConcurrentDictionary<string, string> _discoveredSources = new();

    // --- Network and Connection Management ---
    private Socket? _socket;
    private bool _connected;

    public SacnOutput()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context);

        var localIpString = LocalIpAddress.GetValue(context);

        // Refresh network interfaces periodically
        if (string.IsNullOrEmpty(localIpString) && context.LocalTime - _lastNetworkRefreshTime > 5.0)
        {
            _lastNetworkRefreshTime = context.LocalTime;
            _networkInterfaces = GetNetworkInterfaces();
        }

        var settingsChanged = _connectionSettings.Update(
                                                         localIpString,
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
        var inputValueLists = InputsValues.GetCollectedTypedInputs();

        // Get universe channels list (starting universe for each input)
        var universeChannels = UniverseChannels.GetValue(context);

        // Auto-resize UniverseChannels list to match number of inputs
        if (universeChannels == null)
        {
            universeChannels = new List<int>();
        }

        // Calculate next available universe for auto-expansion
        int nextUniverse = 1;
        if (universeChannels.Count > 0)
        {
            // Find the last input's starting universe and add its chunk count
            var lastInputIndex = universeChannels.Count - 1;
            if (lastInputIndex < inputValueLists.Count)
            {
                var lastBuffer = inputValueLists[lastInputIndex].GetValue(context);
                if (lastBuffer != null)
                {
                    int lastChunkCount = (int)Math.Ceiling(lastBuffer.Count / 512.0);
                    nextUniverse = universeChannels[lastInputIndex] + lastChunkCount;
                }
                else
                {
                    nextUniverse = universeChannels[lastInputIndex];
                }
            }
            else
            {
                nextUniverse = universeChannels[^1] + 1;
            }
        }

        // Ensure list size matches input count
        while (universeChannels.Count < inputValueLists.Count)
        {
            universeChannels.Add(nextUniverse);
            nextUniverse++;
        }

        // Update the input with the auto-resized list
        UniverseChannels.SetTypedInputValue(universeChannels);

        var preparedData = new List<(int universe, byte[] data)>();

        for (int inputIdx = 0; inputIdx < inputValueLists.Count; inputIdx++)
        {
            var input = inputValueLists[inputIdx];
            var buffer = input.GetValue(context);
            if (buffer == null) continue;

            var universeForInput = inputIdx < universeChannels.Count ? universeChannels[inputIdx] : 1;

            for (var i = 0; i < buffer.Count; i += 512)
            {
                var chunkCount = Math.Min(buffer.Count - i, 512);
                if (chunkCount == 0) continue;

                var dmxData = new byte[chunkCount];
                for (var j = 0; j < chunkCount; j++)
                {
                    dmxData[j] = (byte)buffer[i + j].Clamp(0, 255);
                }
                preparedData.Add((universeForInput, dmxData));
                universeForInput++;
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
                SourceName = SourceName.GetValue(context) ?? string.Empty,
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
        _packetBuffer[0] = 0x00; _packetBuffer[1] = 0x10; // Preamble
        _packetBuffer[2] = 0x00; _packetBuffer[3] = 0x00; // Post-amble
        Encoding.ASCII.GetBytes("ASC-E1.17", 0, 9, _packetBuffer, 4);
        _packetBuffer[13] = 0x00; _packetBuffer[14] = 0x00; _packetBuffer[15] = 0x00;

        // Flags & Length
        short rootFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | 31));
        Array.Copy(BitConverter.GetBytes(rootFlagsAndLength), 0, _packetBuffer, 16, 2);

        // VECTOR_ROOT_E131
        int vector = IPAddress.HostToNetworkOrder(0x00000004);
        Array.Copy(BitConverter.GetBytes(vector), 0, _packetBuffer, 18, 4);

        // CID
        Array.Copy(_cid, 0, _packetBuffer, 22, 16);

        // E1.31 Framing Layer
        short frameFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | 9));
        Array.Copy(BitConverter.GetBytes(frameFlagsAndLength), 0, _packetBuffer, 38, 2);

        int frameVector = IPAddress.HostToNetworkOrder(0x00000001); // VECTOR_E131_EXTENDED_SYNCHRONIZATION
        Array.Copy(BitConverter.GetBytes(frameVector), 0, _packetBuffer, 40, 4);
        
        _packetBuffer[44] = sequenceNumber;
        
        short syncUni = IPAddress.HostToNetworkOrder((short)syncUniverse);
        Array.Copy(BitConverter.GetBytes(syncUni), 0, _packetBuffer, 45, 2);
        
        _packetBuffer[47] = 0x00; _packetBuffer[48] = 0x00; // Reserved

        return 49;
    }

    private int BuildSacnDataPacket(int universe, SacnPacketOptions options, byte[] dmxData, byte sequenceNumber)
    {
        var dmxLength = (short)dmxData.Length;

        // Root Layer (38 bytes)
        _packetBuffer[0] = 0x00; _packetBuffer[1] = 0x10;
        _packetBuffer[2] = 0x00; _packetBuffer[3] = 0x00;
        Encoding.ASCII.GetBytes("ASC-E1.17", 0, 9, _packetBuffer, 4);
        _packetBuffer[13] = 0x00; _packetBuffer[14] = 0x00; _packetBuffer[15] = 0x00;

        // Flags & Length
        short rootFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | (108 + dmxLength)));
        Array.Copy(BitConverter.GetBytes(rootFlagsAndLength), 0, _packetBuffer, 16, 2);

        // VECTOR_ROOT_E131
        int vector = IPAddress.HostToNetworkOrder(0x00000004);
        Array.Copy(BitConverter.GetBytes(vector), 0, _packetBuffer, 18, 4);

        // CID
        Array.Copy(_cid, 0, _packetBuffer, 22, 16);

        // E1.31 Framing Layer (88 bytes)
        short frameFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | (86 + dmxLength)));
        Array.Copy(BitConverter.GetBytes(frameFlagsAndLength), 0, _packetBuffer, 38, 2);

        int frameVector = IPAddress.HostToNetworkOrder(0x00000002); // VECTOR_E131_DATA_PACKET
        Array.Copy(BitConverter.GetBytes(frameVector), 0, _packetBuffer, 40, 4);

        // Source name
        Array.Clear(_packetBuffer, 44, 64);
        if (!string.IsNullOrEmpty(options.SourceName))
        {
            var sourceBytes = Encoding.UTF8.GetBytes(options.SourceName);
            int copyCount = Math.Min(sourceBytes.Length, 63);
            Array.Copy(sourceBytes, 0, _packetBuffer, 44, copyCount);
        }

        _packetBuffer[108] = options.Priority;

        short syncUni = IPAddress.HostToNetworkOrder((short)(options.EnableSync ? options.SyncUniverse : 0));
        Array.Copy(BitConverter.GetBytes(syncUni), 0, _packetBuffer, 109, 2);

        _packetBuffer[111] = sequenceNumber;
        _packetBuffer[112] = 0x00; // Options

        short netUniverse = IPAddress.HostToNetworkOrder((short)universe);
        Array.Copy(BitConverter.GetBytes(netUniverse), 0, _packetBuffer, 113, 2);

        // DMP Layer
        short dmpFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | (9 + dmxLength)));
        Array.Copy(BitConverter.GetBytes(dmpFlagsAndLength), 0, _packetBuffer, 115, 2);

        _packetBuffer[117] = 0x02; // Vector
        _packetBuffer[118] = (byte)0xa1; // Address Type & Data Type
        
        // First address (0)
        _packetBuffer[119] = 0x00; _packetBuffer[120] = 0x00;
        
        // Address increment (1)
        _packetBuffer[121] = 0x00; _packetBuffer[122] = 0x01; 
        
        short propValueCount = IPAddress.HostToNetworkOrder((short)(dmxLength + 1));
        Array.Copy(BitConverter.GetBytes(propValueCount), 0, _packetBuffer, 123, 2);
        
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
                catch (SocketException) { if (_isDiscovering) break; } // Break loop if socket is closed
                catch (Exception e) { if (_isDiscovering) Log.Error($"sACN discovery listener error: {e.Message}", this); }
            }
        }
        catch (Exception e) { if (_isDiscovering) Log.Error($"sACN discovery listener failed to bind: {e.Message}", this); }
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
            try { _socket.Close(); } catch { /* Ignore */ }
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
                _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
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

    private static List<NetworkAdapterInfo> _networkInterfaces = GetNetworkInterfaces();

    private static List<NetworkAdapterInfo> GetNetworkInterfaces()
    {
        var list = new List<NetworkAdapterInfo> { new(IPAddress.Loopback, IPAddress.Parse("255.0.0.0"), "Localhost") };
        try
        {
            list.AddRange(from ni in NetworkInterface.GetAllNetworkInterfaces()
                          where ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                          from ip in ni.GetIPProperties().UnicastAddresses
                          where ip.Address.AddressFamily == AddressFamily.InterNetwork
                          select new NetworkAdapterInfo(ip.Address, ip.IPv4Mask, ni.Name));
        }
        catch (Exception e)
        {
            Log.Warning("Could not enumerate network interfaces: " + e.Message);
        }

        return list;
    }

    private sealed record NetworkAdapterInfo(IPAddress IpAddress, IPAddress SubnetMask, string Name)
    {
        public string DisplayName => $"{Name}: {IpAddress}";
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
        public IPAddress? LocalIp { get; private set; }
        public IPAddress? TargetIp { get; private set; }
        public bool SendUnicast { get; private set; }
        private string? _lastLocalIpStr, _lastTargetIpStr;
        private bool _lastSendUnicast;

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
    public void SetStatus(string m, IStatusProvider.StatusLevel l) { _lastErrorMessage = m; _lastStatusLevel = l; }

    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        if (inputId == LocalIpAddress.Id) return LocalIpAddress.Value ?? string.Empty;
        if (inputId == TargetIpAddress.Id) return TargetIpAddress.Value ?? string.Empty;
        return string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId == LocalIpAddress.Id)
        {
            _networkInterfaces = GetNetworkInterfaces();
            foreach (var adapter in _networkInterfaces)
            {
                yield return adapter.DisplayName;
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
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (string.IsNullOrEmpty(selected) || !isAListItem) return;

        if (inputId == LocalIpAddress.Id)
        {
            var foundAdapter = _networkInterfaces.FirstOrDefault(i => i.DisplayName == selected);
            if (foundAdapter == null) return;
            LocalIpAddress.SetTypedInputValue(foundAdapter.IpAddress.ToString());
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

    [Input(Guid = "B2C3D4E5-F6A7-8901-BCDE-F234567890AB")]
    public readonly InputSlot<List<int>> UniverseChannels = new();

    [Input(Guid = "f8a7e0c8-c6c7-4b53-9a3a-3e5f2a4f4e1c")]
    public readonly InputSlot<string> LocalIpAddress = new();

    [Input(Guid = "9c233633-959f-4447-b248-4d431c1b18e7")]
    public readonly InputSlot<bool> SendTrigger = new();

    [Input(Guid = "c2a9e3e3-a4e9-430b-9c6a-4e1a1e0b8e2e")]
    public readonly InputSlot<bool> Reconnect = new();

    [Input(Guid = "8c6c9a8d-29c5-489e-8c6b-9e4a3c1e2b6a")]
    public readonly InputSlot<bool> SendUnicast = new();

    [Input(Guid = "d9e8d7c6-b5a4-434a-9e3a-4e2b1d0c9a7b")]
    public readonly InputSlot<string> TargetIpAddress = new();

    [Input(Guid = "3f25c04c-0a88-42fb-93d3-05992b861e61")]
    public readonly InputSlot<bool> DiscoverSources = new();

    [Input(Guid = "4a9e2d3b-8c6f-4b1d-8d7e-9f3a5b2c1d0e")]
    public readonly InputSlot<int> Priority = new(100);

    [Input(Guid = "5b1d9c8a-7e3f-4a2b-9c8d-1e0f3a5b2c1d")]
    public readonly InputSlot<string> SourceName = new("T3 sACN Output");

    [Input(Guid = "6f5c4b3a-2e1d-4f9c-8a7b-3d2e1f0c9b8a")]
    public readonly InputSlot<int> MaxFps = new(60);

    [Input(Guid = "7a8b9c0d-1e2f-3a4b-5c6d-7e8f9a0b1c2d")]
    public readonly InputSlot<bool> EnableSync = new();

    [Input(Guid = "8b9c0d1e-2f3a-4b5c-6d7e-8f9a0b1c2d3e")]
    public readonly InputSlot<int> SyncUniverse = new(1);

    [Input(Guid = "D0E1F2A3-B4C5-4678-9012-3456789ABCDE")]
    public readonly InputSlot<bool> PrintToLog = new();
    #endregion
}
