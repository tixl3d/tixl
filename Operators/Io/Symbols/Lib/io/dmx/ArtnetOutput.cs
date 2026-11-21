#nullable enable
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.Utils;

namespace Lib.io.dmx
{
    [Guid("98efc7c8-cafd-45ee-8746-14f37e9f59f8")]
    [ExportDependencies("ArtNet.dll")]
    internal sealed class ArtnetOutput : Instance<ArtnetOutput>, IStatusProvider, ICustomDropdownHolder
    {
        private const int ArtNetPort = 6454;
        private const int MaxUniverse = 32767;
        private static readonly byte[] _artnetId = "Art-Net\0"u8.ToArray();

        [Output(Guid = "499329d0-15e9-410e-9f61-63724dbec937")]
        public readonly Slot<Command> Result = new();

        private readonly ConnectionSettings _connectionSettings = new();
        private readonly T3.Core.Show.LocalAdapterResolver _adapterResolver = new();
        private readonly object _dataLock = new();
        private readonly object _connectionLock = new();
        private readonly ConcurrentDictionary<string, string> _discoveredNodes = new();
        private readonly ConcurrentDictionary<int, IPEndPoint> _universeRoutingTable = new();
        private readonly byte[] _packetBuffer = new byte[18 + 512];
        private readonly byte[] _universeSequenceNumbers = new byte[65536];
        private readonly List<List<int>?> _inputBuffers = new();

        // --- Zero-Allocation Queue ---
        private readonly ConcurrentQueue<FrameData> _dataQueue = new();
        private readonly ConcurrentBag<FrameData> _frameDataPool = new();

        private struct InputMapping
        {
            public int Input;
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
            return new FrameData { Buffer = new int[requiredChannels + 1024], Mappings = new InputMapping[requiredMappings + 4] };
        }

        private Thread? _artPollListenerThread;
        private Timer? _artPollTimer;
        private volatile bool _connected;
        private volatile bool _isPolling;
        private string? _lastErrorMessage;
        private IStatusProvider.StatusLevel _lastStatusLevel = IStatusProvider.StatusLevel.Notice;
        private int _maxFps;
        private volatile bool _printToLog;
        private IPAddress? _selectedSubnetMask;
        private CancellationTokenSource? _senderCts;
        private double _lastRetryTime;

        private Thread? _senderThread;
        private Socket? _socket;
        private bool _syncToSend;
        private bool _useArtNet4;
        private bool _wasSendingLastFrame;

        public ArtnetOutput()
        {
            Result.UpdateAction = Update;

            // Constant part of the ArtDmx header; SendDmxPacket only writes the per-packet fields.
            Array.Copy(_artnetId, 0, _packetBuffer, 0, 8);
            _packetBuffer[10] = 0x00;
            _packetBuffer[11] = 14;
        }

        private void Update(EvaluationContext context)
        {
            _printToLog = PrintToLog.GetValue(context);
            // Bind adapter comes from the Networking settings page (per machine, never part of the
            // project): "0.0.0.0" keeps the resolver's substitute-an-adapter behavior for broadcast.
            var localIpString = ProtocolInterfaces.ResolveAddressString(ProtocolInterfaces.Keys.ArtNet);
            var targetIpString = TargetIpAddress.GetValue(context);

            // The stored address may belong to another machine (a failover pair or boygroup shares one project),
            // so each machine binds to its own matching adapter instead of the stored address.
            _adapterResolver.Update(localIpString, targetIpString, context.LocalTime);
            localIpString = _adapterResolver.EffectiveIp;
            _selectedSubnetMask = _adapterResolver.SubnetMask;

            _connectionSettings.Update(localIpString ?? string.Empty, _selectedSubnetMask, targetIpString ?? string.Empty, SendUnicast.GetValue(context), out bool needsSocketRebind);

            bool needsReconnect = Reconnect.GetValue(context) || needsSocketRebind;
            bool shouldAutoRevive = !_connected && _connectionSettings.LocalIp != null && (context.LocalTime - _lastRetryTime > 2.0);

            if (needsReconnect || shouldAutoRevive)
            {
                Reconnect.SetTypedInputValue(false);
                bool needsThreadRestart = _wasSendingLastFrame && _senderThread != null;
                if (needsThreadRestart) StopSenderThread();

                lock (_connectionLock)
                {
                    CloseSocket();
                    _connected = TryConnectArtNet(_connectionSettings.LocalIp);
                    if (shouldAutoRevive) _lastRetryTime = context.LocalTime;
                }

                if (needsThreadRestart) StartSenderThread();
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

            // Show failover: while this machine is Standby the failover arbiter suppresses all
            // network output; frames stop being enqueued so the sender thread goes silent within
            // one frame, and streaming resumes on the next frame after promotion.
            if (T3.Core.Show.FailoverArbiter.IsOutputSuppressed)
            {
                SetStatus("Standby — the failover active node is streaming.", IStatusProvider.StatusLevel.Notice);
                return;
            }

            lock (_connectionLock)
            {
                if (!_connected)
                {
                    var errorMessage = _lastErrorMessage;
                    SetStatus(string.IsNullOrEmpty(errorMessage) ? "Not connected. Check settings." : "Not connected: " + errorMessage,
                              IStatusProvider.StatusLevel.Warning);
                    return;
                }
            }

            SetStatus("Connected and sending.", IStatusProvider.StatusLevel.Success);

            var inputValueLists = InputsValues.GetCollectedTypedInputs();

            // Evaluate each input once: an Animated upstream output can change length between two evaluations,
            // and the frame buffer is sized from one while the copy reads the other.
            var inputBuffers = _inputBuffers;
            inputBuffers.Clear();
            int totalChannels = 0;
            for (int i = 0; i < inputValueLists.Count; i++)
            {
                var buf = inputValueLists[i].GetValue(context);
                inputBuffers.Add(buf);
                if (buf != null) totalChannels += buf.Count;
            }

            if (totalChannels == 0) return;

            var universeChannels = UniverseChannels.GetValue(context);
            int nextUniverse = 1;
            if (universeChannels != null && universeChannels.Count > 0)
            {
                var lastInputIndex = universeChannels.Count - 1;
                if (lastInputIndex < inputValueLists.Count)
                {
                    var lastBuffer = inputBuffers[lastInputIndex];
                    if (lastBuffer != null) nextUniverse = universeChannels[lastInputIndex] + (int)Math.Ceiling(lastBuffer.Count / 512.0);
                    else nextUniverse = universeChannels[lastInputIndex];
                }
                else nextUniverse = universeChannels[^1] + 1;
            }

            if (universeChannels == null || universeChannels.Count < inputValueLists.Count)
            {
                universeChannels ??= new List<int>();
                while (universeChannels.Count < inputValueLists.Count)
                {
                    universeChannels.Add(nextUniverse);
                    nextUniverse++;
                }

                UniverseChannels.SetTypedInputValue(universeChannels);
            }
            ResolveUniverseOverlaps(universeChannels, inputValueLists, context);

            var frameData = RentFrameData(totalChannels, inputValueLists.Count);
            frameData.MappingCount = 0;
            int currentOffset = 0;

            for (int i = 0; i < inputValueLists.Count; i++)
            {
                var buf = inputBuffers[i];
                if (buf == null || buf.Count == 0) continue;
                buf.CopyTo(frameData.Buffer, currentOffset);
                frameData.Mappings[frameData.MappingCount++] = new InputMapping { Input = i, Offset = currentOffset, Count = buf.Count, StartUniverse = universeChannels[i] };
                currentOffset += buf.Count;
            }

            ReportUniverseLayout(frameData);

            lock (_dataLock)
            {
                _syncToSend = SendSync.GetValue(context);
                _maxFps = MaxFps.GetValue(context);
                _useArtNet4 = EnableArtNet4.GetValue(context);
            }

            _dataQueue.Enqueue(frameData);

            while (_dataQueue.Count > 2)
            {
                if (_dataQueue.TryDequeue(out var oldData)) _frameDataPool.Add(oldData);
            }
        }

        #region Universe layout
        private int _layoutSignature;
        private string _layoutText = string.Empty;

        private int _resolveSignature;

        /// <summary>
        /// Starting universes are stored per input, so an input that grows (more fixtures, a changed
        /// FixturesPerUniverse) runs into the universes of the next one. Both would write the same universe and
        /// the last packet wins, which looks like flickering fixtures. A later input that collides with an
        /// earlier one is moved to the first free universe behind it. Runs only when starts or sizes change.
        /// </summary>
        private void ResolveUniverseOverlaps(List<int> starts, IReadOnlyList<Slot<List<int>>> inputs, EvaluationContext context)
        {
            var signature = LayoutSignature(starts, inputs, context);
            if (signature == _resolveSignature)
                return;

            var moved = false;
            for (var i = 1; i < inputs.Count; i++)
            {
                var count = inputs[i].GetValue(context)?.Count ?? 0;
                if (count == 0)
                    continue;

                var span = (count + 511) / 512;
                bool collided;
                do
                {
                    collided = false;
                    for (var j = 0; j < i; j++)
                    {
                        var otherCount = inputs[j].GetValue(context)?.Count ?? 0;
                        if (otherCount == 0)
                            continue;

                        var otherEnd = starts[j] + (otherCount + 511) / 512 - 1;
                        if (starts[i] > otherEnd || starts[i] + span - 1 < starts[j])
                            continue;

                        Log.Info($"ArtnetOutput: input {i + 1} moved from universe {starts[i]} to {otherEnd + 1} to avoid overlapping input {j + 1}.", this);
                        starts[i] = otherEnd + 1;
                        collided = true;
                        moved = true;
                    }
                }
                while (collided);
            }

            if (moved)
                UniverseChannels.SetTypedInputValue(starts);

            _resolveSignature = LayoutSignature(starts, inputs, context);
        }

        private static int LayoutSignature(List<int> starts, IReadOnlyList<Slot<List<int>>> inputs, EvaluationContext context)
        {
            var signature = inputs.Count;
            for (var i = 0; i < inputs.Count; i++)
                signature = HashCode.Combine(signature, starts[i], inputs[i].GetValue(context)?.Count ?? 0);

            return signature;
        }

        /// <summary>Shows which universes each input occupies; rebuilt only when the layout changes.</summary>
        private void ReportUniverseLayout(FrameData frame)
        {
            var signature = frame.MappingCount;
            for (var i = 0; i < frame.MappingCount; i++)
                signature = HashCode.Combine(signature, frame.Mappings[i].StartUniverse, frame.Mappings[i].Count);

            if (signature != _layoutSignature || _layoutText.Length == 0)
            {
                _layoutSignature = signature;
                _layoutText = DescribeLayout(frame);
            }

            SetStatus(_layoutText, IStatusProvider.StatusLevel.Success);
        }

        private static int LastUniverse(in InputMapping mapping) => mapping.StartUniverse + (mapping.Count + 511) / 512 - 1;

        private static string DescribeLayout(FrameData frame)
        {
            const int maxListed = 6;
            var text = new System.Text.StringBuilder("Connected and sending. Universes ");
            for (var i = 0; i < frame.MappingCount && i < maxListed; i++)
            {
                var mapping = frame.Mappings[i];
                if (i > 0)
                    text.Append(", ");

                text.Append(mapping.StartUniverse).Append('-').Append(LastUniverse(mapping));
            }

            if (frame.MappingCount > maxListed)
                text.Append(", ...");

            return text.ToString();
        }
        #endregion

        #region Sender Thread
        private void StartSenderThread()
        {
            if (_senderThread != null) return;
            // The token is captured here: the thread may start after Stop has already cleared the field.
            var cts = new CancellationTokenSource();
            _senderCts = cts;
            var token = cts.Token;
            _senderThread = new Thread(() => SenderLoop(token)) { IsBackground = true, Name = "ArtNetSender", Priority = ThreadPriority.AboveNormal };
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

                    bool syncCopy;
                    int maxFpsCopy;
                    bool useArtNet4Copy;
                    bool isConnected;

                    lock (_dataLock) { syncCopy = _syncToSend; maxFpsCopy = _maxFps; useArtNet4Copy = _useArtNet4; }
                    lock (_connectionLock) { isConnected = _connected; }

                    if (maxFpsCopy > 0)
                    {
                        if (!stopwatch.IsRunning) stopwatch.Start();
                        while (true)
                        {
                            long now = stopwatch.ElapsedTicks;
                            if (now >= nextFrameTimeTicks)
                            {
                                if (now > nextFrameTimeTicks + Stopwatch.Frequency) nextFrameTimeTicks = now;
                                nextFrameTimeTicks += (long)(Stopwatch.Frequency / (double)maxFpsCopy);
                                break;
                            }
                            if (nextFrameTimeTicks - now > Stopwatch.Frequency / 1000) Thread.Sleep(1);
                            else Thread.Yield();
                        }
                    }

                    Socket? currentSocket;
                    IPEndPoint? targetEndPoint;

                    lock (_connectionSettings) { currentSocket = _socket; targetEndPoint = _connectionSettings.TargetEndPoint; }

                    if (currentSocket == null || !isConnected || targetEndPoint == null)
                    {
                        _frameDataPool.Add(frameData);
                        Thread.Sleep(10);
                        continue;
                    }

                    if (syncCopy) SendArtSync(currentSocket, targetEndPoint);

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

                            // Art-Net port addresses are 15 bit; anything else cannot be addressed or sequenced.
                            if ((uint)currentUniverse > 0x7FFF) break;

                            int chunkCount = Math.Min(remainingChannels, 512);
                            int sendLength = Math.Max(2, chunkCount);
                            if (sendLength % 2 != 0) sendLength++;

                            // Art-Net addresses 15-bit universes; anything outside would index out of the sequence
                            // table or wrap onto another universe, so such a chunk is skipped.
                            if (currentUniverse < 0 || currentUniverse > MaxUniverse)
                            {
                                currentOffset += chunkCount;
                                remainingChannels -= chunkCount;
                                currentUniverse++;
                                continue;
                            }

                            byte seq = _universeSequenceNumbers[currentUniverse];
                            seq = (byte)((seq % 255) + 1);
                            _universeSequenceNumbers[currentUniverse] = seq;

                            IPEndPoint specificTarget = targetEndPoint;
                            if (useArtNet4Copy && _universeRoutingTable.TryGetValue(currentUniverse, out var routedEp))
                            {
                                specificTarget = routedEp;
                            }

                            bool success = SendDmxPacket(currentSocket, specificTarget, currentUniverse, frameData.Buffer, currentOffset, chunkCount, sendLength, seq);
                            if (success) consecutiveErrors = 0;
                            else
                            {
                                consecutiveErrors++;
                                if (consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                                {
                                    lock (_connectionLock) _connected = false;
                                    consecutiveErrors = 0;
                                    T3.Core.Show.FailoverArbiter.ReportOutputFault("Art-Net send failing");
                                }
                            }

                            currentOffset += chunkCount;
                            remainingChannels -= chunkCount;
                            currentUniverse++;
                        }
                    }
                    _frameDataPool.Add(frameData);
                }
                catch (ThreadAbortException) { break; }
                catch (Exception e)
                {
                    if (_printToLog) Log.Warning($"ArtNet Sender survived error: {e.Message}", this);
                    consecutiveErrors++;
                    if (consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                    {
                        lock (_connectionLock) _connected = false;
                        consecutiveErrors = 0;
                        T3.Core.Show.FailoverArbiter.ReportOutputFault("Art-Net sender error");
                    }
                    Thread.Sleep(10);
                }
            }
        }
        #endregion

        #region Fast Packet Sending
        private bool SendDmxPacket(Socket socket, IPEndPoint target, int universe, int[] rawData, int offset, int chunkCount, int sendLength, byte sequenceNumber)
        {
            try
            {
                _packetBuffer[8] = 0x00;
                _packetBuffer[9] = 0x50;
                _packetBuffer[12] = sequenceNumber;
                _packetBuffer[13] = 0x00;
                _packetBuffer[14] = (byte)(universe & 0xFF);
                _packetBuffer[15] = (byte)((universe >> 8) & 0x7F);
                _packetBuffer[16] = (byte)(sendLength >> 8);
                _packetBuffer[17] = (byte)(sendLength & 0xFF);

                for (int i = 0; i < chunkCount; i++)
                {
                    int val = rawData[offset + i];
                    _packetBuffer[18 + i] = (byte)(val < 0 ? 0 : (val > 255 ? 255 : val));
                }

                for (int i = chunkCount; i < sendLength; i++) _packetBuffer[18 + i] = 0;

                socket.SendTo(_packetBuffer, 18 + sendLength, SocketFlags.None, target);
                return true;
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode is SocketError.WouldBlock or SocketError.NoBufferSpaceAvailable or SocketError.ConnectionReset) return true;
                return false;
            }
            catch (ObjectDisposedException) { return false; }
        }

        private void SendArtSync(Socket socket, IPEndPoint target)
        {
            try
            {
                Span<byte> syncPacket = stackalloc byte[12];
                _artnetId.CopyTo(syncPacket);
                syncPacket[8] = 0x00; syncPacket[9] = 0x52;
                syncPacket[10] = 0x00; syncPacket[11] = 14;
                socket.SendTo(syncPacket, target);
            }
            catch { /* Ignore */ }
        }
        #endregion

        #region Discovery & Setup
        private void StartArtPolling()
        {
            _discoveredNodes.Clear();
            _universeRoutingTable.Clear();
            _isPolling = true;
            _artPollListenerThread = new Thread(ListenForArtPollReplies) { IsBackground = true, Name = "ArtNetPollListener" };
            _artPollListenerThread.Start();
            _artPollTimer = new Timer(_ => SendArtPoll(), null, 0, 3000);
        }

        private void StopArtPolling()
        {
            if (!_isPolling) return;
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
                if (_socket == null || !_isPolling || _connectionSettings.LocalIp == null || _connectionSettings.TargetEndPoint == null) return;
                try
                {
                    Span<byte> pollPacket = stackalloc byte[14];
                    _artnetId.CopyTo(pollPacket);
                    pollPacket[8] = 0x00; pollPacket[9] = 0x20;
                    pollPacket[10] = 0x00; pollPacket[11] = 14;
                    pollPacket[12] = 2; pollPacket[13] = 0;
                    _socket.SendTo(pollPacket, _connectionSettings.TargetEndPoint);
                }
                catch { /* Swallowed */ }
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

                    var remoteEp = new IPEndPoint(IPAddress.Any, 0) as EndPoint;
                    var receivedBytes = currentSocket.ReceiveFrom(buffer, ref remoteEp);

                    if (receivedBytes < 238 || !buffer.AsSpan(0, 8).SequenceEqual(_artnetId) || buffer[8] != 0x00 || buffer[9] != 0x21) continue;

                    var ipAddress = new IPAddress(new[] { buffer[10], buffer[11], buffer[12], buffer[13] });
                    var shortName = Encoding.ASCII.GetString(buffer, 26, 18).TrimEnd('\0');
                    var ipString = ipAddress.ToString();
                    var displayName = string.IsNullOrWhiteSpace(shortName) ? ipString : shortName;
                    _discoveredNodes[ipString] = $"{displayName} ({ipString})";

                    var nodeEndPoint = new IPEndPoint(ipAddress, ArtNetPort);
                    var netSwitch = buffer[18];
                    var subSwitch = buffer[19];
                    var numPorts = buffer[173] | (buffer[172] << 8);

                    for (int i = 0; i < 4; i++)
                    {
                        if (i < numPorts)
                        {
                            var swOut = buffer[190 + i];
                            var universeAddress = (netSwitch << 8) | (subSwitch << 4) | (swOut & 0x0F);
                            _universeRoutingTable[universeAddress] = nodeEndPoint;
                        }
                    }
                }
                catch { if (_isPolling) Thread.Sleep(10); }
            }
        }

        /// <summary>The framework only calls this hook; a plain Dispose() would leave the sender thread running and the port bound.</summary>
        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                StopSenderThread();
                StopArtPolling();
                CloseSocket();
            }

            base.Dispose(isDisposing);
        }

        private void CloseSocket()
        {
            lock (_connectionLock)
            {
                lock (_connectionSettings)
                {
                    if (_socket == null) return;
                    try { _socket.Close(); } catch { } finally { _socket = null; _connected = false; _lastErrorMessage = "Socket closed."; }
                }
            }
        }

        private bool TryConnectArtNet(IPAddress? localIp)
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

                        // Broadcast is allowed even on localhost now
                        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

                        try { _socket.IOControl(-1744830452, new byte[] { 0 }, null); } catch { }

                        _socket.Bind(new IPEndPoint(localIp, ArtNetPort));
                        _lastErrorMessage = null;
                        return _connected = true;
                    }
                    catch (Exception e)
                    {
                        // CloseSocket reports "Socket closed."; the bind failure is the message worth showing.
                        var message = $"Failed to bind socket: {e.Message}";
                        CloseSocket();
                        _lastErrorMessage = message;
                        return false;
                    }
                }
            }
        }

        private static IPAddress? CalculateBroadcastAddress(IPAddress address, IPAddress subnetMask)
        {
            byte[] ipBytes = address.GetAddressBytes();
            byte[] maskBytes = subnetMask.GetAddressBytes();
            if (ipBytes.Length != maskBytes.Length) return null;
            byte[] broadcastBytes = new byte[ipBytes.Length];
            for (int i = 0; i < broadcastBytes.Length; i++) broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
            return new IPAddress(broadcastBytes);
        }

        private sealed class ConnectionSettings
        {
            private string? _lastLocalIpStr;
            public IPAddress? LocalIp { get; private set; }
            public IPEndPoint? TargetEndPoint { get; private set; }

            public void Update(string localIpStr, IPAddress? subnetMask, string targetIpStr, bool sendUnicast, out bool needsSocketRebind)
            {
                needsSocketRebind = false;

                if (_lastLocalIpStr != localIpStr)
                {
                    _lastLocalIpStr = localIpStr;
                    IPAddress.TryParse(localIpStr, out var parsedLocalIp);
                    LocalIp = parsedLocalIp;
                    needsSocketRebind = true;
                }

                IPAddress? targetIp = null;
                if (sendUnicast && !string.IsNullOrWhiteSpace(targetIpStr))
                {
                    IPAddress.TryParse(targetIpStr, out targetIp);
                }
                else if (!sendUnicast)
                {
                    // Fallback securely to subnet broadcast (or global broadcast if it fails). Works with loopback correctly now!
                    targetIp = (LocalIp != null && subnetMask != null) ? CalculateBroadcastAddress(LocalIp, subnetMask) ?? IPAddress.Broadcast : IPAddress.Broadcast;
                }

                TargetEndPoint = targetIp != null ? new IPEndPoint(targetIp, ArtNetPort) : null;
            }
        }
        #endregion

        #region UI & Inputs
        public IStatusProvider.StatusLevel GetStatusLevel() => _lastStatusLevel;
        public string? GetStatusMessage() => _lastErrorMessage;
        public void SetStatus(string m, IStatusProvider.StatusLevel l) { _lastErrorMessage = m; _lastStatusLevel = l; }

        string ICustomDropdownHolder.GetValueForInput(Guid inputId) { if (inputId == TargetIpAddress.Id) return TargetIpAddress.Value ?? string.Empty; return string.Empty; }
        IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId) { if (inputId != TargetIpAddress.Id) yield break; if (!_isPolling && _discoveredNodes.IsEmpty) yield return "Enable 'Discover Nodes' to search..."; else if (_isPolling && _discoveredNodes.IsEmpty) yield return "Searching for nodes..."; else foreach (var nodeName in _discoveredNodes.Values.OrderBy(name => name)) yield return nodeName; }
        void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem) { if (string.IsNullOrEmpty(selected) || !isAListItem || inputId != TargetIpAddress.Id) return; var finalIp = selected; var match = Regex.Match(selected, @"\(([^)]*)\)"); if (match.Success) finalIp = match.Groups[1].Value; TargetIpAddress.SetTypedInputValue(finalIp); }

        [Input(Guid = "F7520A37-C2D4-41FA-A6BA-A6ED0423A4EC")] public readonly MultiInputSlot<List<int>> InputsValues = new();
        [Input(Guid = "B2C3D4E5-F6A7-8901-BCDE-F234567890AB")] public readonly InputSlot<List<int>> UniverseChannels = new();
        [Input(Guid = "168d0023-554f-46cd-9e62-8f3d1f564b8d")] public readonly InputSlot<bool> SendTrigger = new();
        [Input(Guid = "73babdb1-f88f-4e4d-aa3f-0536678b0793")] public readonly InputSlot<bool> Reconnect = new();
        [Input(Guid = "d293bb33-2fba-4048-99b8-86aa15a478f2")] public readonly InputSlot<bool> SendSync = new();
        [Input(Guid = "7c15da5f-cfa1-4339-aceb-4ed0099ea041")] public readonly InputSlot<bool> SendUnicast = new();
        [Input(Guid = "32DB51FA-EF32-478A-A8C4-C1F93F5451E9")] public readonly InputSlot<bool> EnableArtNet4 = new(true);
        [Input(Guid = "0fc76369-788a-4ffe-9dde-8eea5f10cf32")] public readonly InputSlot<string> TargetIpAddress = new();
        [Input(Guid = "65fb88ec-5772-4973-bd8b-bb2cb9f557e7")] public readonly InputSlot<bool> PrintArtnetPoll = new();
        [Input(Guid = "6F5C4B3A-2E1D-4F9C-8A7B-3D2E1F0C9B8A")] public readonly InputSlot<int> MaxFps = new(60);
        [Input(Guid = "D0E1F2A3-B4C5-4678-9012-3456789ABCDE")] public readonly InputSlot<bool> PrintToLog = new();
        #endregion
    }
}