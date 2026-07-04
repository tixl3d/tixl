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

    [Output(Guid = "a3c4a2e8-bc1b-453a-9773-1952a6ea10a3")]
    public readonly Slot<Command> Result = new();

    // --- State and Configuration ---
    private readonly ConnectionSettings _connectionSettings = new();
    private readonly object _dataLock = new();
    private readonly object _connectionLock = new();
    private bool _wasSendingLastFrame;
    private string? _lastErrorMessage;
    private IStatusProvider.StatusLevel _lastStatusLevel = IStatusProvider.StatusLevel.Notice;
    private readonly byte[] _cid = Guid.NewGuid().ToByteArray();
    private double _lastNetworkRefreshTime;
    private double _lastRetryTime;

    // --- High-Performance Sending Resources ---
    private Thread? _senderThread;
    private CancellationTokenSource? _senderCts;
    private SacnPacketOptions _packetOptions;
    private readonly byte[] _packetBuffer = new byte[126 + 512];
    private readonly byte[] _universeSequenceNumbers = new byte[65536];

    // --- MULTI-THREADING OPTIMIZATION: Zero-Allocation Snapshot Architecture ---
    private readonly ConcurrentQueue<FrameData> _dataQueue = new();
    private readonly ConcurrentBag<FrameData> _frameDataPool = new();

    private struct InputMapping
    {
        public int Offset;
        public int Count;
        public int StartUniverse;
    }

    private sealed class FrameData
    {
        public int[] Buffer = Array.Empty<int>();
        public InputMapping[] Mappings = Array.Empty<InputMapping>();
        public int MappingCount;
    }

    private FrameData RentFrameData(int requiredChannels, int requiredMappings)
    {
        if (_frameDataPool.TryTake(out var fd))
        {
            if (fd.Buffer.Length < requiredChannels) fd.Buffer = new int[requiredChannels + 1024];
            if (fd.Mappings.Length < requiredMappings) fd.Mappings = new InputMapping[requiredMappings + 4];
            return fd;
        }
        return new FrameData
        {
            Buffer = new int[requiredChannels + 1024],
            Mappings = new InputMapping[requiredMappings + 4]
        };
    }

    // --- Discovery & Network ---
    private Thread? _discoveryListenerThread;
    private volatile bool _isDiscovering;
    private UdpClient? _discoveryUdpClient;
    private readonly ConcurrentDictionary<string, string> _discoveredSources = new();
    private Socket? _socket;
    private volatile bool _connected;

    public SacnOutput()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var localIpString = LocalIpAddress.GetValue(context);

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

        bool needsReconnect = Reconnect.GetValue(context) || settingsChanged;
        bool shouldAutoRevive = !_connected && _connectionSettings.LocalIp != null && (_lastRetryTime == 0 || context.LocalTime - _lastRetryTime > 2.0);

        if (needsReconnect || shouldAutoRevive)
        {
            Reconnect.SetTypedInputValue(false);
            bool needsThreadRestart = _wasSendingLastFrame && _senderThread != null;
            if (needsThreadRestart) StopSenderThread();

            lock (_connectionLock)
            {
                CloseSocket();
                _connected = TryConnectSacn(_connectionSettings.LocalIp);
                if (shouldAutoRevive) _lastRetryTime = context.LocalTime;
            }
            if (needsThreadRestart) StartSenderThread();
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

        lock (_connectionLock)
        {
            if (!_connected)
            {
                SetStatus($"Not connected. {(_lastErrorMessage ?? "Check settings.")}", IStatusProvider.StatusLevel.Warning);
                return;
            }
        }
        SetStatus("Connected and sending.", IStatusProvider.StatusLevel.Success);

        // --- Calculate Size and Prepare Offload ---
        var inputValueLists = InputsValues.GetCollectedTypedInputs();
        int totalChannels = 0;

        // Fast pre-pass to determine memory size
        for (int i = 0; i < inputValueLists.Count; i++)
        {
            var buf = inputValueLists[i].GetValue(context);
            if (buf != null) totalChannels += buf.Count;
        }

        if (totalChannels == 0) return;

        var universeChannels = UniverseChannels.GetValue(context);
        if (universeChannels == null || universeChannels.Count < inputValueLists.Count)
        {
            if (universeChannels == null) universeChannels = new List<int>();
            int nextUniverse = universeChannels.Count > 0 ? universeChannels[^1] + 1 : 1;
            while (universeChannels.Count < inputValueLists.Count)
            {
                universeChannels.Add(nextUniverse);
                nextUniverse++;
            }
            UniverseChannels.SetTypedInputValue(universeChannels);
        }

        // Rent pooled object for background thread (No allocation!)
        var frameData = RentFrameData(totalChannels, inputValueLists.Count);
        frameData.MappingCount = 0;
        int currentOffset = 0;

        // Perform hyper-fast native memory copy. 
        // We do ZERO math on the main thread now.
        for (int i = 0; i < inputValueLists.Count; i++)
        {
            var buf = inputValueLists[i].GetValue(context);
            if (buf == null || buf.Count == 0) continue;

            buf.CopyTo(frameData.Buffer, currentOffset);

            frameData.Mappings[frameData.MappingCount++] = new InputMapping
            {
                Offset = currentOffset,
                Count = buf.Count,
                StartUniverse = universeChannels[i]
            };

            currentOffset += buf.Count;
        }

        _packetOptions.MaxFps = MaxFps.GetValue(context);
        _packetOptions.Priority = (byte)Priority.GetValue(context).Clamp(0, 200);
        _packetOptions.SourceName = SourceName.GetValue(context) ?? string.Empty;
        _packetOptions.EnableSync = EnableSync.GetValue(context);
        _packetOptions.SyncUniverse = (ushort)SyncUniverse.GetValue(context).Clamp(1, 63999);

        _dataQueue.Enqueue(frameData);

        // Keep queue extremely tight. 1 active, 1 waiting max.
        while (_dataQueue.Count > 2)
        {
            if (_dataQueue.TryDequeue(out var oldData))
            {
                _frameDataPool.Add(oldData);
            }
        }
    }

    #region Sender Thread Management and Loop
    private void StartSenderThread()
    {
        if (_senderThread != null) return;
        _senderCts = new CancellationTokenSource();
        _senderThread = new Thread(() => SenderLoop(_senderCts.Token))
        {
            IsBackground = true, Name = "sACNSender", Priority = ThreadPriority.AboveNormal
        };
        _senderThread.Start();
    }

    private void StopSenderThread()
    {
        if (_senderThread == null) return;
        _senderCts?.Cancel();
        if (_senderThread.Join(500)) _senderCts?.Dispose();
        _senderCts = null;
        _senderThread = null;
    }

    private void SenderLoop(CancellationToken token)
    {
        var stopwatch = new Stopwatch();
        long nextFrameTimeTicks = 0;
        byte syncSequenceNumber = 0;
        int consecutiveErrors = 0;
        const int MAX_CONSECUTIVE_ERRORS = 10;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_dataQueue.TryDequeue(out var frameData) || frameData == null)
                {
                    Thread.Sleep(1);
                    continue;
                }

                SacnPacketOptions optionsCopy;
                bool isConnected;
                lock (_dataLock) optionsCopy = _packetOptions;
                lock (_connectionLock) isConnected = _connected;

                if (optionsCopy.MaxFps > 0)
                {
                    if (!stopwatch.IsRunning) stopwatch.Start();
                    while (true)
                    {
                        long now = stopwatch.ElapsedTicks;
                        if (now >= nextFrameTimeTicks)
                        {
                            if (now > nextFrameTimeTicks + Stopwatch.Frequency) nextFrameTimeTicks = now;
                            nextFrameTimeTicks += (long)(Stopwatch.Frequency / (double)optionsCopy.MaxFps);
                            break;
                        }
                        if (nextFrameTimeTicks - now > Stopwatch.Frequency / 1000) Thread.Sleep(1);
                        else Thread.SpinWait(10);
                    }
                }

                Socket? currentSocket;
                bool useUnicast;
                IPEndPoint? cachedTargetEndPoint;

                lock (_connectionSettings)
                {
                    currentSocket = _socket;
                    if (currentSocket == null || !isConnected)
                    {
                        _frameDataPool.Add(frameData);
                        Thread.Sleep(10);
                        continue;
                    }

                    useUnicast = (_connectionSettings.SendUnicast && _connectionSettings.TargetIp != null)
                                 || _connectionSettings.TargetIp != null;
                    cachedTargetEndPoint = useUnicast ? new IPEndPoint(_connectionSettings.TargetIp!, SacnPort) : null;
                }

                // Background thread now does the math and packet building
                for (int m = 0; m < frameData.MappingCount; m++)
                {
                    if (token.IsCancellationRequested) break;

                    var mapping = frameData.Mappings[m];
                    int remainingChannels = mapping.Count;
                    int currentOffset = mapping.Offset;
                    int currentUniverse = mapping.StartUniverse;

                    while (remainingChannels > 0)
                    {
                        if (token.IsCancellationRequested) break;

                        int chunkCount = Math.Min(remainingChannels, 512);
                        byte seq = _universeSequenceNumbers[currentUniverse]++;

                        var packetLength = BuildSacnDataPacket(currentUniverse, optionsCopy, frameData.Buffer, currentOffset, chunkCount, seq);
                        var targetEndPoint = cachedTargetEndPoint ?? new IPEndPoint(GetSacnMulticastAddress(currentUniverse), SacnPort);

                        bool success = SendSacnPacket(currentSocket, targetEndPoint, _packetBuffer, packetLength, currentUniverse);
                        if (success) consecutiveErrors = 0;
                        else
                        {
                            consecutiveErrors++;
                            if (consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                            {
                                lock (_connectionLock) _connected = false;
                                consecutiveErrors = 0;
                            }
                        }

                        currentOffset += chunkCount;
                        remainingChannels -= chunkCount;
                        currentUniverse++;
                    }
                }

                // Return native block memory to pool
                _frameDataPool.Add(frameData);

                if (optionsCopy.EnableSync) SendSacnSync(currentSocket, optionsCopy.SyncUniverse, syncSequenceNumber++);
            }
            catch (ThreadAbortException) { break; }
            catch (Exception)
            {
                consecutiveErrors++;
                if (consecutiveErrors >= MAX_CONSECUTIVE_ERRORS) { lock (_connectionLock) _connected = false; consecutiveErrors = 0; }
                Thread.Sleep(10);
            }
        }
    }
    #endregion

    #region Packet Sending 
    private bool SendSacnPacket(Socket socket, IPEndPoint target, byte[] packetBuffer, int packetLength, int universe)
    {
        try { socket.SendTo(packetBuffer, packetLength, SocketFlags.None, target); return true; }
        catch (SocketException e) { return e.SocketErrorCode is SocketError.WouldBlock or SocketError.NoBufferSpaceAvailable or SocketError.ConnectionReset; }
        catch (ObjectDisposedException) { return false; }
    }

    private void SendSacnSync(Socket socket, ushort syncAddress, byte sequenceNumber)
    {
        try
        {
            var packetLength = BuildSacnSyncPacket(syncAddress, sequenceNumber);
            var useUnicast = _connectionSettings.TargetIp != null;
            var syncEndPoint = useUnicast ? new IPEndPoint(_connectionSettings.TargetIp!, SacnPort) : new IPEndPoint(GetSacnMulticastAddress(syncAddress), SacnPort);
            socket.SendTo(_packetBuffer, packetLength, SocketFlags.None, syncEndPoint);
        }
        catch (SocketException e) { if (e.SocketErrorCode is not (SocketError.WouldBlock or SocketError.NoBufferSpaceAvailable or SocketError.ConnectionReset)) lock (_connectionLock) _connected = false; }
        catch (ObjectDisposedException) { lock (_connectionLock) _connected = false; }
    }

    private int BuildSacnSyncPacket(ushort syncUniverse, byte sequenceNumber)
    {
        _packetBuffer[0] = 0x00; _packetBuffer[1] = 0x10; _packetBuffer[2] = 0x00; _packetBuffer[3] = 0x00;
        Encoding.ASCII.GetBytes("ASC-E1.17", 0, 9, _packetBuffer, 4);
        _packetBuffer[13] = 0x00; _packetBuffer[14] = 0x00; _packetBuffer[15] = 0x00;
        short rootFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | 31));
        Array.Copy(BitConverter.GetBytes(rootFlagsAndLength), 0, _packetBuffer, 16, 2);
        int vector = IPAddress.HostToNetworkOrder(0x00000004);
        Array.Copy(BitConverter.GetBytes(vector), 0, _packetBuffer, 18, 4);
        Array.Copy(_cid, 0, _packetBuffer, 22, 16);
        short frameFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | 9));
        Array.Copy(BitConverter.GetBytes(frameFlagsAndLength), 0, _packetBuffer, 38, 2);
        int frameVector = IPAddress.HostToNetworkOrder(0x00000001);
        Array.Copy(BitConverter.GetBytes(frameVector), 0, _packetBuffer, 40, 4);
        _packetBuffer[44] = sequenceNumber;
        short syncUni = IPAddress.HostToNetworkOrder((short)syncUniverse);
        Array.Copy(BitConverter.GetBytes(syncUni), 0, _packetBuffer, 45, 2);
        _packetBuffer[47] = 0x00; _packetBuffer[48] = 0x00;
        return 49;
    }

    private int BuildSacnDataPacket(int universe, SacnPacketOptions options, int[] rawData, int offset, int chunkCount, byte sequenceNumber)
    {
        var dmxLength = (short)chunkCount;

        _packetBuffer[0] = 0x00; _packetBuffer[1] = 0x10; _packetBuffer[2] = 0x00; _packetBuffer[3] = 0x00;
        Encoding.ASCII.GetBytes("ASC-E1.17", 0, 9, _packetBuffer, 4);
        _packetBuffer[13] = 0x00; _packetBuffer[14] = 0x00; _packetBuffer[15] = 0x00;
        short rootFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | (108 + dmxLength)));
        Array.Copy(BitConverter.GetBytes(rootFlagsAndLength), 0, _packetBuffer, 16, 2);
        int vector = IPAddress.HostToNetworkOrder(0x00000004);
        Array.Copy(BitConverter.GetBytes(vector), 0, _packetBuffer, 18, 4);
        Array.Copy(_cid, 0, _packetBuffer, 22, 16);
        short frameFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | (86 + dmxLength)));
        Array.Copy(BitConverter.GetBytes(frameFlagsAndLength), 0, _packetBuffer, 38, 2);
        int frameVector = IPAddress.HostToNetworkOrder(0x00000002);
        Array.Copy(BitConverter.GetBytes(frameVector), 0, _packetBuffer, 40, 4);

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
        _packetBuffer[112] = 0x00;
        short netUniverse = IPAddress.HostToNetworkOrder((short)universe);
        Array.Copy(BitConverter.GetBytes(netUniverse), 0, _packetBuffer, 113, 2);
        short dmpFlagsAndLength = IPAddress.HostToNetworkOrder((short)(0x7000 | (9 + dmxLength)));
        Array.Copy(BitConverter.GetBytes(dmpFlagsAndLength), 0, _packetBuffer, 115, 2);
        _packetBuffer[117] = 0x02; _packetBuffer[118] = (byte)0xa1;
        _packetBuffer[119] = 0x00; _packetBuffer[120] = 0x00;
        _packetBuffer[121] = 0x00; _packetBuffer[122] = 0x01;
        short propValueCount = IPAddress.HostToNetworkOrder((short)(dmxLength + 1));
        Array.Copy(BitConverter.GetBytes(propValueCount), 0, _packetBuffer, 123, 2);
        _packetBuffer[125] = 0x00;

        // CRITICAL PERFORMANCE LOOP: Execute Math directly into the sending buffer
        for (int i = 0; i < chunkCount; i++)
        {
            int val = rawData[offset + i];
            _packetBuffer[126 + i] = (byte)(val < 0 ? 0 : (val > 255 ? 255 : val));
        }

        return 126 + dmxLength;
    }
    #endregion

    #region Discovery & Boilerplate Helpers
    private void StartSacnDiscovery()
    {
        _isDiscovering = true;
        _discoveredSources.Clear();
        _discoveryListenerThread = new Thread(ListenForSacnDiscovery) { IsBackground = true, Name = "sACNDiscoveryListener" };
        _discoveryListenerThread.Start();
    }

    private void StopSacnDiscovery()
    {
        if (!_isDiscovering) return;
        _isDiscovering = false;
        _discoveryUdpClient?.Close();
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
                catch (SocketException) { if (_isDiscovering) break; }
                catch (Exception e) { if (_isDiscovering) Log.Error($"sACN discovery listener error: {e.Message}", this); }
            }
        }
        catch (Exception e) { if (_isDiscovering) Log.Error($"sACN discovery listener failed to bind: {e.Message}", this); }
        finally { _discoveryUdpClient?.Close(); _discoveryUdpClient = null; }
    }

    public void Dispose() { StopSenderThread(); StopSacnDiscovery(); CloseSocket(); }

    private void CloseSocket()
    {
        lock (_connectionLock)
        {
            lock (_connectionSettings)
            {
                if (_socket == null) return;
                try { _socket.Close(); }
                catch { /* Ignore */ }
                finally { _socket = null; _connected = false; _lastErrorMessage = "Socket closed."; }
            }
        }
    }

    private bool TryConnectSacn(IPAddress? localIp)
    {
        lock (_connectionLock)
        {
            lock (_connectionSettings)
            {
                if (localIp == null) { _lastErrorMessage = "Local IP Address is not valid."; return false; }
                try
                {
                    _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    _socket.SendBufferSize = 10 * 1024 * 1024;
                    _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
                    try { _socket.IOControl(-1744830452, new byte[] { 0 }, null); } catch { /* Ignore SIO_UDP_CONNRESET */ }
                    _socket.Bind(new IPEndPoint(localIp, 0));
                    _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                    _lastErrorMessage = null;
                    return _connected = true;
                }
                catch (Exception e) { _lastErrorMessage = $"Failed to bind: {e.Message}"; CloseSocket(); return false; }
            }
        }
    }

    private static IPAddress GetSacnMulticastAddress(int universe)
    {
        var u = (ushort)universe.Clamp(1, 63999);
        return new IPAddress(new byte[] { 239, 255, (byte)(u >> 8), (byte)(u & 0xFF) });
    }

    private static List<NetworkAdapterInfo> _networkInterfaces = GetNetworkInterfaces();

    private static List<NetworkAdapterInfo> GetNetworkInterfaces()
    {
        var list = new List<NetworkAdapterInfo> { new(IPAddress.Loopback, IPAddress.Parse("255.0.0.0"), "Localhost (127.0.0.1)") };
        try
        {
            list.AddRange(from ni in NetworkInterface.GetAllNetworkInterfaces()
                          where ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                          from ip in ni.GetIPProperties().UnicastAddresses
                          where ip.Address.AddressFamily == AddressFamily.InterNetwork
                          select new NetworkAdapterInfo(ip.Address, ip.IPv4Mask, ni.Name));
        }
        catch { /* Ignore */ }
        return list;
    }

    private sealed record NetworkAdapterInfo(IPAddress IpAddress, IPAddress SubnetMask, string Name) { public string DisplayName => $"{Name}: {IpAddress}"; }
    private struct SacnPacketOptions { public int MaxFps; public byte Priority; public string SourceName; public bool EnableSync; public ushort SyncUniverse; }

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
            _lastLocalIpStr = localIpStr; _lastTargetIpStr = targetIpStr; _lastSendUnicast = sendUnicast; SendUnicast = sendUnicast;
            IPAddress.TryParse(localIpStr, out var parsedLocalIp); LocalIp = parsedLocalIp;
            IPAddress? targetIp = null;
            if (LocalIp != null && LocalIp.Equals(IPAddress.Loopback)) targetIp = IPAddress.Loopback;
            else if (sendUnicast) IPAddress.TryParse(targetIpStr, out targetIp);
            TargetIp = targetIp;
            return true;
        }
    }

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
            foreach (var adapter in _networkInterfaces) yield return adapter.DisplayName;
        }
        else if (inputId == TargetIpAddress.Id)
        {
            if (!_isDiscovering && _discoveredSources.IsEmpty) yield return "Enable 'Discover Sources' to search...";
            else if (_isDiscovering && _discoveredSources.IsEmpty) yield return "Searching for sources...";
            else foreach (var sourceName in _discoveredSources.Values.OrderBy(name => name)) yield return sourceName;
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
    [Input(Guid = "2a8d39a3-5a41-477d-815a-8b8b9d8b1e4a")] public readonly MultiInputSlot<List<int>> InputsValues = new();
    [Input(Guid = "B2C3D4E5-F6A7-8901-BCDE-F234567890AB")] public readonly InputSlot<List<int>> UniverseChannels = new();
    [Input(Guid = "f8a7e0c8-c6c7-4b53-9a3a-3e5f2a4f4e1c")] public readonly InputSlot<string> LocalIpAddress = new();
    [Input(Guid = "9c233633-959f-4447-b248-4d431c1b18e7")] public readonly InputSlot<bool> SendTrigger = new();
    [Input(Guid = "c2a9e3e3-a4e9-430b-9c6a-4e1a1e0b8e2e")] public readonly InputSlot<bool> Reconnect = new();
    [Input(Guid = "8c6c9a8d-29c5-489e-8c6b-9e4a3c1e2b6a")] public readonly InputSlot<bool> SendUnicast = new();
    [Input(Guid = "d9e8d7c6-b5a4-434a-9e3a-4e2b1d0c9a7b")] public readonly InputSlot<string> TargetIpAddress = new();
    [Input(Guid = "3f25c04c-0a88-42fb-93d3-05992b861e61")] public readonly InputSlot<bool> DiscoverSources = new();
    [Input(Guid = "4a9e2d3b-8c6f-4b1d-8d7e-9f3a5b2c1d0e")] public readonly InputSlot<int> Priority = new(100);
    [Input(Guid = "5b1d9c8a-7e3f-4a2b-9c8d-1e0f3a5b2c1d")] public readonly InputSlot<string> SourceName = new("T3 sACN Output");
    [Input(Guid = "6f5c4b3a-2e1d-4f9c-8a7b-3d2e1f0c9b8a")] public readonly InputSlot<int> MaxFps = new(60);
    [Input(Guid = "7a8b9c0d-1e2f-3a4b-5c6d-7e8f9a0b1c2d")] public readonly InputSlot<bool> EnableSync = new();
    [Input(Guid = "8b9c0d1e-2f3a-4b5c-6d7e-8f9a0b1c2d3e")] public readonly InputSlot<int> SyncUniverse = new(1);
    #endregion
}