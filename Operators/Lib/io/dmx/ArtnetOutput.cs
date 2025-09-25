#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

namespace Lib.io.dmx;

[Guid("98efc7c8-cafd-45ee-8746-14f37e9f59f8")]
internal sealed class ArtnetOutput : Instance<ArtnetOutput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    // --- Art-Net Protocol Constants ---
    private const int ArtNetPort = 6454;
    private const ushort OpDmx = 0x5000;
    private const ushort OpPoll = 0x2000;
    private const ushort OpSync = 0x5200;
    private const ushort ProtocolVersion = 14;
    private static readonly byte[] ArtnetId = "Art-Net\0"u8.ToArray();

    [Output(Guid = "499329d0-15e9-410e-9f61-63724dbec937")]
    public readonly Slot<Command> Result = new();

    // --- State and Configuration ---
    private readonly ConnectionSettings _connectionSettings = new();
    private volatile bool _printToLog;
    private bool _wasSendingLastFrame;
    private string? _lastErrorMessage;
    private IStatusProvider.StatusLevel _lastStatusLevel = IStatusProvider.StatusLevel.Notice;
    private IPAddress? _selectedSubnetMask;

    // --- High-Performance Sending Resources ---
    private Thread? _senderThread;
    private CancellationTokenSource? _senderCts;
    private readonly object _dataLock = new();
    private List<(int universe, byte[] data)>? _dmxDataToSend;
    private bool _syncToSend;
    private int _maxFps;
    private readonly byte[] _packetBuffer = new byte[18 + 512]; // Reusable buffer for zero-allocation packet creation

    // --- Discovery (ArtPoll) Resources ---
    private Timer? _artPollTimer;
    private Thread? _artPollListenerThread;
    private volatile bool _isPolling;
    private readonly ConcurrentDictionary<string, string> _discoveredNodes = new();

    // --- Network and Connection Management ---
    private Socket? _socket;
    private bool _connected;

    public ArtnetOutput()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _printToLog = PrintToLog.GetValue(context);

        var settingsChanged = _connectionSettings.Update(
                                                         LocalIpAddress.GetValue(context),
                                                         _selectedSubnetMask,
                                                         TargetIpAddress.GetValue(context),
                                                         SendUnicast.GetValue(context)
                                                        );

        if (Reconnect.GetValue(context) || settingsChanged)
        {
            Reconnect.SetTypedInputValue(false);
            if (_printToLog) Log.Debug("Artnet Output: Reconnecting Art-Net socket...", this);
            CloseSocket();
            _connected = TryConnectArtNet(_connectionSettings.LocalIp);
        }

        var discoverNodes = PrintArtnetPoll.GetValue(context);
        if (discoverNodes && !_isPolling) StartArtPolling();
        else if (!discoverNodes && _isPolling) StopArtPolling();

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
        var startUniverse = StartUniverse.GetValue(context);
        var inputValueLists = InputsValues.GetCollectedTypedInputs();

        const int chunkSize = 512;
        var universeIndex = startUniverse;
        var preparedData = new List<(int universe, byte[] data)>();

        foreach (var input in inputValueLists)
        {
            var buffer = input.GetValue(context);
            if (buffer == null) continue;

            for (var i = 0; i < buffer.Count; i += chunkSize)
            {
                var chunkCount = Math.Min(buffer.Count - i, chunkSize);
                if (chunkCount == 0) continue;

                // Art-Net requires an even data length, min 2
                var sendLength = Math.Max(2, chunkCount);
                if (sendLength % 2 != 0) sendLength++;

                var dmxData = new byte[sendLength];
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
            _syncToSend = SendSync.GetValue(context);
            _maxFps = MaxFps.GetValue(context);
        }
    }

    #region Sender Thread Management and Loop
    private void StartSenderThread()
    {
        if (_senderThread != null) return;

        if (_printToLog) Log.Debug("Artnet Output: Starting sender thread.", this);
        _senderCts = new CancellationTokenSource();
        var token = _senderCts.Token;

        _senderThread = new Thread(() => SenderLoop(token))
        {
            IsBackground = true, Name = "ArtNetSender", Priority = ThreadPriority.AboveNormal
        };
        _senderThread.Start();
    }

    private void StopSenderThread()
    {
        if (_senderThread == null) return;

        if (_printToLog) Log.Debug("Artnet Output: Stopping sender thread.", this);
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
            // --- Copy shared data under lock to minimize lock duration ---
            List<(int universe, byte[] data)>? dataCopy;
            bool syncCopy;
            int maxFpsCopy;
            lock (_dataLock)
            {
                dataCopy = _dmxDataToSend;
                syncCopy = _syncToSend;
                maxFpsCopy = _maxFps;
            }

            // --- Frame Rate Limiting ---
            if (maxFpsCopy > 0)
            {
                if (!stopwatch.IsRunning) stopwatch.Start();
                long now = stopwatch.ElapsedTicks;
                if (now < nextFrameTimeTicks)
                {
                    // If we have more than 1ms to wait, sleep. Otherwise, spin to be more accurate.
                    if (nextFrameTimeTicks - now > Stopwatch.Frequency / 1000)
                    {
                        Thread.Sleep(1);
                    }
                    else
                    {
                        Thread.SpinWait(100);
                    }
                    continue; // Re-evaluate wait time in the next loop iteration
                }
                // Reset timing if we fell far behind to prevent a "death spiral"
                if (now > nextFrameTimeTicks + Stopwatch.Frequency)
                    nextFrameTimeTicks = now;

                nextFrameTimeTicks += (long)(Stopwatch.Frequency / (double)maxFpsCopy);
            }

            // --- Send Data (Lock socket access to prevent race conditions with reconnection) ---
            lock (_connectionSettings)
            {
                var currentSocket = _socket;
                var targetEndPoint = _connectionSettings.TargetEndPoint;

                if (currentSocket == null || !_connected || targetEndPoint == null)
                {
                    // Sleep briefly to prevent a tight busy-loop if disconnected
                    Thread.Sleep(100);
                    continue;
                }

                if (syncCopy) SendArtSync(currentSocket, targetEndPoint);

                if (dataCopy != null)
                {
                    foreach (var (universe, data) in dataCopy)
                    {
                        if (token.IsCancellationRequested) break;
                        SendDmxPacket(currentSocket, targetEndPoint, universe, data, sequenceNumber);
                    }
                }
            }

            // Art-Net sequence number must not be 0
            sequenceNumber = (byte)((sequenceNumber % 255) + 1);
        }
    }
    #endregion

    #region Packet Sending (Zero-Allocation)
    private void SendDmxPacket(Socket socket, IPEndPoint target, int universe, byte[] dmxData, byte sequenceNumber)
    {
        try
        {
            // 0-7: ID
            Array.Copy(ArtnetId, 0, _packetBuffer, 0, 8);
            // 8-9: OpCode (Little-Endian)
            _packetBuffer[8] = (byte)(OpDmx & 0xFF);
            _packetBuffer[9] = (byte)(OpDmx >> 8);
            // 10-11: Protocol Version (Big-Endian)
            _packetBuffer[10] = (byte)(ProtocolVersion >> 8);
            _packetBuffer[11] = (byte)(ProtocolVersion & 0xFF);
            // 12: Sequence
            _packetBuffer[12] = sequenceNumber;
            // 13: Physical
            _packetBuffer[13] = 0;
            // 14-15: Universe (Little-Endian)
            _packetBuffer[14] = (byte)(universe & 0xFF);
            _packetBuffer[15] = (byte)(universe >> 8);
            // 16-17: Length (Big-Endian)
            int dataLength = dmxData.Length;
            _packetBuffer[16] = (byte)(dataLength >> 8);
            _packetBuffer[17] = (byte)(dataLength & 0xFF);
            // 18+: Data
            Array.Copy(dmxData, 0, _packetBuffer, 18, dataLength);

            socket.SendTo(_packetBuffer, 18 + dataLength, SocketFlags.None, target);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            if (_printToLog) Log.Warning($"Artnet Output: Send failed to universe {universe}: {e.Message}", this);
            _connected = false;
        }
    }

    private void SendArtSync(Socket socket, IPEndPoint target)
    {
        try
        {
            // This packet is fixed and small, so we can use a stack-allocated buffer for efficiency
            Span<byte> syncPacket = stackalloc byte[12];
            ArtnetId.CopyTo(syncPacket);
            syncPacket[8] = (byte)(OpSync & 0xFF);
            syncPacket[9] = (byte)(OpSync >> 8);
            syncPacket[10] = (byte)(ProtocolVersion >> 8);
            syncPacket[11] = (byte)(ProtocolVersion & 0xFF);

            socket.SendTo(syncPacket, target);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            if (_printToLog) Log.Warning($"Artnet Output: Failed to send ArtSync: {e.Message}", this);
            _connected = false;
        }
    }
    #endregion

    #region ArtPoll and Discovery
    private void StartArtPolling()
    {
        if (_printToLog) Log.Debug("Artnet Output: Starting ArtPoll...", this);
        _discoveredNodes.Clear();
        _isPolling = true;
        _artPollListenerThread = new Thread(ListenForArtPollReplies) { IsBackground = true, Name = "ArtNetPollListener" };
        _artPollListenerThread.Start();
        _artPollTimer = new Timer(_ => SendArtPoll(), null, 0, 3000);
    }

    private void StopArtPolling()
    {
        if (!_isPolling) return;
        if (_printToLog) Log.Debug("Artnet Output: Stopping ArtPoll.", this);
        _isPolling = false;
        _artPollTimer?.Dispose();
        _artPollTimer = null;
        _artPollListenerThread?.Join(200);
        _artPollListenerThread = null;
    }

    private void SendArtPoll()
    {
        lock (_connectionSettings)
        {
            if (_socket == null || !_isPolling || _connectionSettings.LocalIp == null || _connectionSettings.SubnetMask == null) return;
            try
            {
                Span<byte> pollPacket = stackalloc byte[14];
                ArtnetId.CopyTo(pollPacket);
                pollPacket[8] = (byte)(OpPoll & 0xFF);
                pollPacket[9] = (byte)(OpPoll >> 8);
                pollPacket[10] = (byte)(ProtocolVersion >> 8);
                pollPacket[11] = (byte)(ProtocolVersion & 0xFF);
                pollPacket[12] = 2; // TalkToMe: Send ArtPollReply
                pollPacket[13] = 0; // Priority

                var broadcastAddress = CalculateBroadcastAddress(_connectionSettings.LocalIp, _connectionSettings.SubnetMask);
                if (broadcastAddress == null) return;
                var broadcastEndPoint = new IPEndPoint(broadcastAddress, ArtNetPort);
                _socket.SendTo(pollPacket, broadcastEndPoint);
            }
            catch (Exception e)
            {
                if (_isPolling && _printToLog) Log.Warning($"Artnet Output: Failed to send ArtPoll: {e.Message}", this);
            }
        }
    }

    private void ListenForArtPollReplies()
    {
        var buffer = new byte[1024];
        while (_isPolling)
        {
            try
            {
                Socket? currentSocket;
                lock (_connectionSettings) currentSocket = _socket;
                if (currentSocket == null || currentSocket.Available == 0) { Thread.Sleep(10); continue; }

                var remoteEP = new IPEndPoint(IPAddress.Any, 0) as EndPoint;
                var receivedBytes = currentSocket.ReceiveFrom(buffer, ref remoteEP);

                // Basic validation for an ArtPollReply packet
                if (receivedBytes < 238 || !buffer.AsSpan(0, 8).SequenceEqual(ArtnetId) || buffer[8] != 0x00 || buffer[9] != 0x21) continue;

                var ipAddress = new IPAddress(new[] { buffer[10], buffer[11], buffer[12], buffer[13] });
                var shortName = Encoding.ASCII.GetString(buffer, 26, 18).TrimEnd('\0');
                var ipString = ipAddress.ToString();
                var displayName = string.IsNullOrWhiteSpace(shortName) ? ipString : shortName;
                _discoveredNodes[ipString] = $"{displayName} ({ipString})";
            }
            catch (SocketException) { if (_isPolling) break; }
            catch (Exception e) { if (_isPolling && _printToLog) Log.Warning($"Artnet Output: ArtPoll listen error: {e.Message}", this); }
        }
    }
    #endregion

    #region Connection Management and Lifecycle
    public void Dispose()
    {
        StopSenderThread();
        StopArtPolling();
        CloseSocket();
    }

    private void CloseSocket()
    {
        lock (_connectionSettings)
        {
            if (_socket == null) return;
            if (_printToLog) Log.Debug("Artnet Output: Closing socket.", this);
            try
            {
                _socket.Close();
            }
            catch (Exception e)
            {
                if (_printToLog) Log.Warning($"Artnet Output: Error closing socket: {e.Message}", this);
            }
            finally
            {
                _socket = null;
                _connected = false;
                _lastErrorMessage = "Socket closed.";
            }
        }
    }

    private bool TryConnectArtNet(IPAddress? localIp)
    {
        lock (_connectionSettings)
        {
            if (localIp == null)
            {
                _lastErrorMessage = "Local IP Address is not valid. Please select a valid network adapter.";
                return false;
            }
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                {
                    Blocking = false
                };
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _socket.Bind(new IPEndPoint(localIp, ArtNetPort));
                _lastErrorMessage = null;
                if (_printToLog) Log.Debug($"Artnet Output: Socket bound to {localIp}:{ArtNetPort}.", this);
                return _connected = true;
            }
            catch (Exception e)
            {
                _lastErrorMessage = $"Failed to bind socket to {localIp}:{ArtNetPort}: {e.Message}";
                CloseSocket();
                return false;
            }
        }
    }
    #endregion

    #region Helpers and Static Members
    private static IPAddress? CalculateBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        byte[] ipBytes = address.GetAddressBytes();
        byte[] maskBytes = subnetMask.GetAddressBytes();
        if (ipBytes.Length != maskBytes.Length) return null;
        byte[] broadcastBytes = new byte[ipBytes.Length];
        for (int i = 0; i < broadcastBytes.Length; i++) broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
        return new IPAddress(broadcastBytes);
    }

    private static readonly List<NetworkAdapterInfo> NetworkInterfaces = GetNetworkInterfaces();
    private static List<NetworkAdapterInfo> GetNetworkInterfaces()
    {
        var list = new List<NetworkAdapterInfo> { new(IPAddress.Loopback, IPAddress.Parse("255.0.0.0"), "Localhost") };
        try
        {
            list.AddRange(from ni in NetworkInterface.GetAllNetworkInterfaces()
                          where ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                          from ip in ni.GetIPProperties().UnicastAddresses
                          where ip.Address.AddressFamily == AddressFamily.InterNetwork && ip.IPv4Mask != null
                          select new NetworkAdapterInfo(ip.Address, ip.IPv4Mask, ni.Name));
        }
        catch (Exception e) { Log.Warning("Could not enumerate network interfaces: " + e.Message); }
        return list;
    }

    private sealed record NetworkAdapterInfo(IPAddress IpAddress, IPAddress SubnetMask, string Name)
    {
        public string DisplayName => $"{Name}: {IpAddress}";
    }

    private sealed class ConnectionSettings
    {
        public IPAddress? LocalIp { get; private set; }
        public IPAddress? SubnetMask { get; private set; }
        public IPEndPoint? TargetEndPoint { get; private set; }
        private string? _lastLocalIpStr, _lastTargetIpStr;
        private bool _lastSendUnicast;

        public bool Update(string localIpStr, IPAddress? subnetMask, string targetIpStr, bool sendUnicast)
        {
            if (_lastLocalIpStr == localIpStr && _lastTargetIpStr == targetIpStr && _lastSendUnicast == sendUnicast) return false;

            _lastLocalIpStr = localIpStr;
            _lastTargetIpStr = targetIpStr;
            _lastSendUnicast = sendUnicast;
            SubnetMask = subnetMask;

            IPAddress.TryParse(localIpStr, out var parsedLocalIp);
            LocalIp = parsedLocalIp;

            IPAddress? targetIp = null;
            if (sendUnicast)
            {
                IPAddress.TryParse(targetIpStr, out targetIp);
            }
            else if (LocalIp != null && SubnetMask != null)
            {
                targetIp = CalculateBroadcastAddress(LocalIp, SubnetMask);
            }

            TargetEndPoint = targetIp != null ? new IPEndPoint(targetIp, ArtNetPort) : null;
            return true;
        }
    }
    #endregion

    #region IStatusProvider and ICustomDropdownHolder implementation
    public IStatusProvider.StatusLevel GetStatusLevel() => _lastStatusLevel;
    public string? GetStatusMessage() => _lastErrorMessage;
    public void SetStatus(string m, IStatusProvider.StatusLevel l) { _lastErrorMessage = m; _lastStatusLevel = l; }

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
            foreach (var adapter in NetworkInterfaces) yield return adapter.DisplayName;
        }
        else if (inputId == TargetIpAddress.Id)
        {
            if (!_isPolling && _discoveredNodes.IsEmpty) yield return "Enable 'Discover Nodes' to search...";
            else if (_isPolling && _discoveredNodes.IsEmpty) yield return "Searching for nodes...";
            else foreach (var nodeName in _discoveredNodes.Values.OrderBy(name => name)) yield return nodeName;
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (string.IsNullOrEmpty(selected) || !isAListItem) return;

        if (inputId == LocalIpAddress.Id)
        {
            var foundAdapter = NetworkInterfaces.FirstOrDefault(i => i.DisplayName == selected);
            if (foundAdapter == null) return;
            LocalIpAddress.SetTypedInputValue(foundAdapter.IpAddress.ToString());
            _selectedSubnetMask = foundAdapter.SubnetMask;
        }
        else if (inputId == TargetIpAddress.Id)
        {
            var finalIp = selected;
            var match = Regex.Match(selected, @"\(([^)]*)\)");
            if (match.Success) finalIp = match.Groups[1].Value;
            TargetIpAddress.SetTypedInputValue(finalIp);
        }
    }
    #endregion

    #region Inputs
    [Input(Guid = "F7520A37-C2D4-41FA-A6BA-A6ED0423A4EC")] public readonly MultiInputSlot<List<int>> InputsValues = new();
    [Input(Guid = "34aeeda5-72b0-4f13-bfd3-4ad5cf42b24f")] public readonly InputSlot<int> StartUniverse = new();
    [Input(Guid = "fcbfe87b-b8aa-461c-a5ac-b22bb29ad36d")] public readonly InputSlot<string> LocalIpAddress = new();
    [Input(Guid = "168d0023-554f-46cd-9e62-8f3d1f564b8d")] public readonly InputSlot<bool> SendTrigger = new();
    [Input(Guid = "73babdb1-f88f-4e4d-aa3f-0536678b0793")] public readonly InputSlot<bool> Reconnect = new();
    [Input(Guid = "d293bb33-2fba-4048-99b8-86aa15a478f2")] public readonly InputSlot<bool> SendSync = new();
    [Input(Guid = "7c15da5f-cfa1-4339-aceb-4ed0099ea041")] public readonly InputSlot<bool> SendUnicast = new();
    [Input(Guid = "0fc76369-788a-4ffe-9dde-8eea5f10cf32")] public readonly InputSlot<string> TargetIpAddress = new();
    [Input(Guid = "65fb88ec-5772-4973-bd8b-bb2cb9f557e7")] public readonly InputSlot<bool> PrintArtnetPoll = new();
    [Input(Guid = "4A9E2D3B-8C6F-4B1D-8D7E-9F3A5B2C1D0E")] public readonly InputSlot<int> Priority = new(100);
    [Input(Guid = "5B1D9C8A-7E3F-4A2B-9C8D-1E0F3A5B2C1D")] public readonly InputSlot<string> SourceName = new("T3 Art-Net Output");
    [Input(Guid = "6F5C4B3A-2E1D-4F9C-8A7B-3D2E1F0C9B8A")] public readonly InputSlot<int> MaxFps = new(60);
    [Input(Guid = "D0E1F2A3-B4C5-4678-9012-3456789ABCDE")] public readonly InputSlot<bool> PrintToLog = new();
    #endregion
}