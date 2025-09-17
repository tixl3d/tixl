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
internal sealed class SacnOutput : Instance<SacnOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable // Implements IDisposable
{
    private const int SacnPort = 5568;
    private const string SacnDiscoveryIp = "239.255.250.214";

    [Output(Guid = "a3c4a2e8-bc1b-453a-9773-1952a6ea10a3")]
    public readonly Slot<Command> Result = new();

    private Thread? _discoveryListenerThread;
    private volatile bool _isDiscovering; // Made volatile
    private UdpClient? _discoveryUdpClient;
    private readonly ConcurrentDictionary<string, string> _discoveredSources = new();
    private readonly byte[] _cid = Guid.NewGuid().ToByteArray();
    private readonly Stopwatch _stopwatch = new();
    private long _nextFrameTimeTicks;
    private bool _printToLog; // Added for PrintToLog functionality

    public SacnOutput()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context); // Update printToLog flag

        var maxFps = MaxFps.GetValue(context);
        if (maxFps > 0)
        {
            if (!_stopwatch.IsRunning) _stopwatch.Start();
            while (_stopwatch.ElapsedTicks < _nextFrameTimeTicks)
            {
                if (_nextFrameTimeTicks - _stopwatch.ElapsedTicks > Stopwatch.Frequency / 1000) Thread.Sleep(1);
                else Thread.SpinWait(100);
            }
            _nextFrameTimeTicks += (long)(Stopwatch.Frequency / (double)maxFps);
        }

        _enableSending = SendTrigger.GetValue(context);
        var settingsChanged = _connectionSettings.Update(LocalIpAddress.GetValue(context), TargetIpAddress.GetValue(context), SendUnicast.GetValue(context));

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

        if (!_enableSending || !_connected || _socket == null)
        {
            if (!_connected)
                SetStatus($"Not connected. {(_lastErrorMessage ?? "Check settings.")}", IStatusProvider.StatusLevel.Warning);
            else if (!_enableSending)
                SetStatus("Sending is disabled. Enable 'Send Trigger'.", IStatusProvider.StatusLevel.Notice);
            return;
        }

        SetStatus("Connected and sending.", IStatusProvider.StatusLevel.Success); // Update status if sending

        var startUniverse = Math.Max(1, StartUniverse.GetValue(context));
        var priority = Priority.GetValue(context).Clamp(0, 200);
        var sourceName = SourceName.GetValue(context);
        var enableSync = EnableSync.GetValue(context);
        var syncUniverse = SyncUniverse.GetValue(context);

        SendData(context, startUniverse, priority, sourceName, enableSync, syncUniverse, _sequenceNumber, InputsValues.GetCollectedTypedInputs());
        _sequenceNumber++;
    }

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
        _discoveryUdpClient?.Close(); // This will unblock the Receive call in ListenForSacnDiscovery
        _discoveryListenerThread?.Join(200); // Give the thread a moment to shut down
        _discoveryListenerThread = null;
        _discoveredSources.Clear(); // Clear sources on stop
    }

    private void ListenForSacnDiscovery()
    {
        UdpClient? currentDiscoveryUdpClient = null; // Declare locally for safer cleanup
        try
        {
            currentDiscoveryUdpClient = new UdpClient();
            var localEp = new IPEndPoint(IPAddress.Any, SacnPort);
            currentDiscoveryUdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            currentDiscoveryUdpClient.Client.Bind(localEp);
            currentDiscoveryUdpClient.JoinMulticastGroup(IPAddress.Parse(SacnDiscoveryIp));

            _discoveryUdpClient = currentDiscoveryUdpClient; // Assign to member field

            if (_printToLog) Log.Debug($"sACN Output: Discovery listener bound to {localEp}, joined multicast {SacnDiscoveryIp}", this);

            while (_isDiscovering)
            {
                try
                {
                    if (_discoveryUdpClient == null) break; // Check if client was disposed externally
                    var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                    var data = _discoveryUdpClient.Receive(ref remoteEp);
                    if (data.Length <= 125) continue;

                    var sourceName = Encoding.UTF8.GetString(data, 44, 64).TrimEnd('\0');
                    var ipString = remoteEp.Address.ToString();
                    var displayName = string.IsNullOrWhiteSpace(sourceName) ? ipString : sourceName;

                    _discoveredSources[ipString] = $"{displayName} ({ipString})";
                    if (_printToLog) Log.Debug($"sACN Output: Discovered source: {displayName} ({ipString})", this);
                }
                catch (SocketException ex)
                {
                    if (_isDiscovering) // Only log if not intentionally stopping
                    {
                        Log.Warning($"sACN Output discovery receive socket error: {ex.Message} (Error Code: {ex.ErrorCode})", this);
                    }
                    break;
                }
                catch (Exception e)
                {
                    if (_isDiscovering) Log.Error($"sACN Output discovery listener failed: {e.Message}", this);
                }
            }
        }
        catch (Exception e) { if (_isDiscovering) Log.Error($"sACN Output discovery listener failed to bind: {e.Message}", this); }
        finally
        {
            currentDiscoveryUdpClient?.Close();
            if (_discoveryUdpClient == currentDiscoveryUdpClient) _discoveryUdpClient = null;
        }
    }

    private void SendData(EvaluationContext context, int startUniverse, int priority, string sourceName, bool enableSync, int syncUniverse, byte sequenceNumber, List<Slot<List<int>>> connections)
    {
        if (_socket == null) return;
        var syncAddress = enableSync ? (ushort)syncUniverse.Clamp(1, 63999) : (ushort)0;
        var universeIndex = startUniverse;
        foreach (var input in connections)
        {
            var buffer = input.GetValue(context);
            if (buffer == null || buffer.Count == 0) continue;
            for (var i = 0; i < buffer.Count; i += 512)
            {
                var chunkCount = Math.Min(buffer.Count - i, 512);
                var dmxData = new byte[chunkCount];
                for (var j = 0; j < chunkCount; j++) dmxData[j] = (byte)buffer[i + j].Clamp(0, 255);
                try
                {
                    var packet = BuildSacnDataPacket(universeIndex, priority, sourceName, dmxData, syncAddress, sequenceNumber);
                    var targetEndPoint = (_connectionSettings.SendUnicast && _connectionSettings.TargetIp != null)
                                             ? new IPEndPoint(_connectionSettings.TargetIp, SacnPort)
                                             : new IPEndPoint(GetSacnMulticastAddress(universeIndex), SacnPort);
                    _socket.SendTo(packet, targetEndPoint);
                    if (_printToLog)
                    {
                        Log.Debug($"sACN Output → DMX Universe {universeIndex} to {targetEndPoint}", this);
                    }
                }
                catch (SocketException e)
                {
                    Log.Warning($"sACN Output send failed for universe {universeIndex}: {e.Message}", this);
                    _connected = false; return;
                }
                universeIndex++;
            }
        }
        if (enableSync) SendSacnSync(syncAddress, sequenceNumber);
    }

    private void SendSacnSync(ushort syncAddress, byte sequenceNumber)
    {
        if (_socket == null) return;
        try
        {
            var syncPacket = BuildSacnSyncPacket(syncAddress, sequenceNumber);
            var syncEndPoint = new IPEndPoint(GetSacnMulticastAddress(syncAddress), SacnPort);
            _socket.SendTo(syncPacket, syncEndPoint);
            if (_printToLog)
            {
                Log.Debug($"sACN Output → Sync Packet for Universe {syncAddress} to {syncEndPoint}", this);
            }
        }
        catch (SocketException e)
        {
            Log.Warning($"sACN Output: Failed to send sACN sync packet to universe {syncAddress}: {e.Message}", this);
            _connected = false;
        }
    }

    private byte[] BuildSacnSyncPacket(ushort syncUniverse, byte sequenceNumber)
    {
        using var ms = new MemoryStream(49);
        using var writer = new BinaryWriter(ms);
        writer.Write(new byte[] { 0, 0x10, 0, 0, 65, 83, 67, 45, 69, 49, 46, 49, 55, 0, 0, 0 });
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)(0x7000 | 33)));
        writer.Write(IPAddress.HostToNetworkOrder(0x00000004));
        writer.Write(_cid);
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)(0x7000 | 9)));
        writer.Write(IPAddress.HostToNetworkOrder(0x00000001));
        writer.Write(sequenceNumber);
        writer.Write((ushort)IPAddress.HostToNetworkOrder(syncUniverse));
        writer.Write((ushort)0);
        return ms.ToArray();
    }

    private byte[] BuildSacnDataPacket(int universe, int priority, string sourceName, byte[] dmxData, ushort syncUniverse, byte sequenceNumber)
    {
        using var ms = new MemoryStream(126 + dmxData.Length);
        using var writer = new BinaryWriter(ms);
        writer.Write(new byte[] { 0, 0x10, 0, 0, 65, 83, 67, 45, 69, 49, 46, 49, 55, 0, 0, 0 });
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)(0x7000 | (110 + dmxData.Length))));
        writer.Write(IPAddress.HostToNetworkOrder(0x00000004));
        writer.Write(_cid);
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)(0x7000 | (88 + dmxData.Length))));
        writer.Write(IPAddress.HostToNetworkOrder(0x00000002));
        var sourceNameBytes = new byte[64];
        Encoding.UTF8.GetBytes(sourceName, 0, Math.Min(sourceName.Length, 63), sourceNameBytes, 0);
        writer.Write(sourceNameBytes);
        writer.Write((byte)priority);
        writer.Write((ushort)IPAddress.HostToNetworkOrder(syncUniverse));
        writer.Write(sequenceNumber);
        writer.Write((byte)0x00);
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)universe));
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)(0x7000 | (11 + dmxData.Length))));
        writer.Write((byte)0x02);
        writer.Write((byte)0xa1);
        writer.Write((ushort)0);
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)0x0001));
        writer.Write((ushort)IPAddress.HostToNetworkOrder((short)(dmxData.Length + 1)));
        writer.Write((byte)0x00);
        writer.Write(dmxData);
        return ms.ToArray();
    }

    // Standard IDisposable implementation
    public void Dispose()
    {
        StopSacnDiscovery();
        CloseSocket();
        // No managed resources to dispose directly here as they are closed in StopSacnDiscovery and CloseSocket
    }

    private void CloseSocket()
    {
        if (_printToLog) Log.Debug("sACN Output: Closing socket.", this);
        _socket?.Close();
        _socket = null;
        _connected = false;
        _lastErrorMessage = "Socket closed.";
    }

    private bool TryConnectSacn(IPAddress? localIp)
    {
        if (localIp == null)
        {
            _lastErrorMessage = "Local IP Address is not valid.";
            if (_printToLog) Log.Error($"sACN Output: Failed to connect - invalid Local IP.", this);
            return false;
        }
        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
            _socket.Bind(new IPEndPoint(localIp, 0)); // Bind to a dynamic port
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 10);
            _lastErrorMessage = null; // Clear error message on successful connect
            if (_printToLog) Log.Debug($"sACN Output: Socket bound to {localIp}.", this);
            return _connected = true;
        }
        catch (Exception e)
        {
            _lastErrorMessage = $"Failed to bind sACN socket to {localIp}: {e.Message}";
            if (_printToLog) Log.Error($"sACN Output: Failed to bind sACN socket to {localIp}: {e.Message}", this);
            CloseSocket();
            return false;
        }
    }

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

    private Socket? _socket;
    private bool _connected;
    private bool _enableSending;
    private byte _sequenceNumber;
    private readonly ConnectionSettings _connectionSettings = new();
    private string? _lastErrorMessage;

    public IStatusProvider.StatusLevel GetStatusLevel() => _connected && string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    public string? GetStatusMessage() => _lastErrorMessage;

    #region IStatusProvider implementation
    // Changed SetStatus to public
    public void SetStatus(string m, IStatusProvider.StatusLevel l) { _lastErrorMessage = m; _lastStatusLevel = l; }
    private IStatusProvider.StatusLevel _lastStatusLevel = IStatusProvider.StatusLevel.Notice;
    #endregion

    #region ICustomDropdownHolder implementation
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
        else
        {
            yield break; // For other inputIds, return empty
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (string.IsNullOrEmpty(selected) || !isAListItem) return;
        var finalIp = selected;
        if (inputId == TargetIpAddress.Id)
        {
            var match = Regex.Match(selected, @"\(([^)]*)\)");
            if (match.Success) finalIp = match.Groups[1].Value;
        }
        if (inputId == LocalIpAddress.Id) LocalIpAddress.SetTypedInputValue(finalIp);
        else if (inputId == TargetIpAddress.Id) TargetIpAddress.SetTypedInputValue(finalIp);
    }
    #endregion

    [Input(Guid = "2a8d39a3-5a41-477d-815a-8b8b9d8b1e4a")] public readonly MultiInputSlot<List<int>> InputsValues = new();
    [Input(Guid = "1b26f5d5-8141-4b13-b88d-6859ed5a4af8")] public readonly InputSlot<int> StartUniverse = new(1);
    [Input(Guid = "9C233633-959F-4447-B248-4D431C1B18E7")] public readonly InputSlot<string> LocalIpAddress = new("127.0.0.1"); // Updated GUID and default
    [Input(Guid = "9B8A7C6D-5E4F-4012-3456-7890ABCDEF12")] public readonly InputSlot<bool> SendTrigger = new(); // Updated GUID
    [Input(Guid = "C2D3E4F5-A6B7-4890-1234-567890ABCDEF")] public readonly InputSlot<bool> Reconnect = new(); // Updated GUID
    [Input(Guid = "8C6C9A8D-29C5-489E-8C6B-9E4A3C1E2B6A")] public readonly InputSlot<bool> SendUnicast = new();
    [Input(Guid = "D9E8D7C6-B5A4-434A-9E3A-4E2B1D0C9A7B")] public readonly InputSlot<string> TargetIpAddress = new();
    [Input(Guid = "3F25C04C-0A88-42FB-93D3-05992B861E61")] public readonly InputSlot<bool> DiscoverSources = new();
    [Input(Guid = "4A9E2D3B-8C6F-4B1D-8D7E-9F3A5B2C1D0E")] public readonly InputSlot<int> Priority = new(100);
    [Input(Guid = "5B1D9C8A-7E3F-4A2B-9C8D-1E0F3A5B2C1D")] public readonly InputSlot<string> SourceName = new("T3 sACN Output");
    [Input(Guid = "6F5C4B3A-2E1D-4F9C-8A7B-3D2E1F0C9B8A")] public readonly InputSlot<int> MaxFps = new(60);
    [Input(Guid = "7A8B9C0D-1E2F-3A4B-5C6D-7E8F9A0B1C2D")] public readonly InputSlot<bool> EnableSync = new();
    [Input(Guid = "8B9C0D1E-2F3A-4B5C-6D7E-8F9A0B1C2D3E")] public readonly InputSlot<int> SyncUniverse = new(64001);
    // New InputSlot for PrintToLog
    [Input(Guid = "D0E1F2A3-B4C5-4678-9012-3456789ABCDE")] // New GUID
    public readonly InputSlot<bool> PrintToLog = new();

    private sealed class ConnectionSettings
    {
        public IPAddress? LocalIp { get; private set; }
        public IPAddress? TargetIp { get; private set; }
        public bool SendUnicast { get; private set; }
        private string? _lastLocalIpStr;
        private string? _lastTargetIpStr;
        private bool _lastSendUnicast;

        public bool Update(string? localIpStr, string? targetIpStr, bool sendUnicast)
        {
            var changed = _lastLocalIpStr != localIpStr ||
                          _lastTargetIpStr != targetIpStr ||
                          _lastSendUnicast != sendUnicast;

            if (!changed) return false;

            _lastLocalIpStr = localIpStr;
            _lastTargetIpStr = targetIpStr;
            _lastSendUnicast = sendUnicast;
            SendUnicast = sendUnicast;

            IPAddress.TryParse(localIpStr, out var parsedLocalIp);
            LocalIp = parsedLocalIp;

            if (sendUnicast)
            {
                IPAddress.TryParse(targetIpStr, out var parsedTargetIp);
                TargetIp = parsedTargetIp;
            }
            else
            {
                TargetIp = null; // Ensure TargetIp is null if not unicast
            }
            return true;
        }
    }
}