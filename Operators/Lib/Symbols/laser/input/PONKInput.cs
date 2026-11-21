#nullable enable
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Core.Utils;

namespace Lib.laser.input;

/// <summary>
/// Receives laser frames from PONK-over-UDP senders (MadMapper, another TiXL instance, ...).
/// A background listener binds <see cref="Port"/> on the adapter chosen on the Networking settings page and joins the
/// default multicast group; chunked frames are reassembled per sender, checksummed, decoded and
/// republished as ILDA-style <see cref="LaserPoint"/>s. The listener is generation-owned and
/// joined on stop so a settings change can never leave two threads feeding the same scratch.
/// Inputs are snapshotted on the evaluator thread; packet decoding never touches input slots.
/// </summary>
[Guid("B2C3D4E5-F6A7-8901-ABCD-EF1234567890")]
internal sealed class PONKInput : Instance<PONKInput>, IStatusProvider, ICustomDropdownHolder, IDisposable
{
    /// <summary>Immutable status snapshot: the listener thread writes it while the evaluator and
    /// UI read it; two separate fields could pair a new message with an old level.</summary>
    private sealed record StatusInfo(string Message, IStatusProvider.StatusLevel Level);

    #region PONK Protocol Constants (must match the sender in T3.Core.Output.LaserOutputStream and the C++ reference implementation)
    // Header layout (52 bytes, little-endian):
    //  0-7:  "PONK-UDP" (8 ASCII bytes)
    //  8:    protocolVersion (1 byte, currently 0)
    //  9-12: senderIdentifier (4 bytes, LE int32)
    // 13-44: senderName (32 bytes, null-padded ASCII)
    // 45:   frameNumber (1 byte, mod-256)
    // 46:   chunkCount (1 byte)
    // 47:   chunkNumber (1 byte)
    // 48-51: dataCrc (4 bytes, LE uint32 - sum of all frame data bytes)
    // The crc field occupies offsets 48-51, so the payload starts at 52 - the header is NOT 48 bytes.
    private const string HeaderString = "PONK-UDP";
    private const byte ProtocolVersion = 0;
    private const int HeaderSize = 52;

    // Data formats (PONK_DATA_FORMAT_*)
    private const byte DataFormatXyRgbU16 = 0;          // 2x u16 XY + 3x u16 RGB = 10 bytes/point
    private const byte DataFormatXyF32RgbU8 = 1;        // 2x f32 XY + 3x u8 RGB = 11 bytes/point (mandatory receivers)

    // Default transport
    private static readonly IPAddress DefaultMulticastAddress = IPAddress.Parse("239.255.10.24");
    private const int DefaultPort = 5583;

    // PONK stores XY in [-1, 1]; LaserPoint X/Y are signed 16-bit ILDA-style (range ±32767).
    private const int LaserPointCoordMax = 32767;
    #endregion

    #region Inputs
    [Input(Guid = "F1E2D3C4-B5A6-7890-ABCD-EF1234567801")]
    public readonly InputSlot<bool> Active = new(true);

    [Input(Guid = "F1E2D3C4-B5A6-7890-ABCD-EF1234567803")]
    public readonly InputSlot<int> Port = new(DefaultPort);

    [Input(Guid = "F1E2D3C4-B5A6-7890-ABCD-EF1234567804")]
    public readonly InputSlot<bool> PrintToLog = new();

    [Input(Guid = "F1E2D3C4-B5A6-7890-ABCD-EF1234567805")]
    public readonly InputSlot<float> Timeout = new(1.2f);

    [Input(Guid = "F1E2D3C4-B5A6-7890-ABCD-EF1234567806")]
    public readonly InputSlot<int> ExpectedSenderId = new(0);

    [Input(Guid = "8B965258-0C3F-47F7-BDC1-29FE74C5BB78")]
    public readonly InputSlot<string> FilterSenderName = new();

    [Input(Guid = "F1E2D3C4-B5A6-7890-ABCD-EF1234567807")]
    public readonly InputSlot<float> MinX = new(-1.0f);

    [Input(Guid = "F1E2D3C4-B5A6-7890-ABCD-EF1234567808")]
    public readonly InputSlot<float> MaxX = new(1.0f);

    [Input(Guid = "F1E2D3C4-B5A6-7890-ABCD-EF1234567809")]
    public readonly InputSlot<float> MinY = new(-1.0f);

    [Input(Guid = "F1E2D3C4-B5A6-7890-ABCD-EF123456780A")]
    public readonly InputSlot<float> MaxY = new(1.0f);
    #endregion

    #region Outputs
    [Output(Guid = "E1D2C3B4-A5F6-7890-ABCD-EF1234567890", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<StructuredList> LaserPoints = new();

    [Output(Guid = "F2E3D4C5-B6A7-8901-ABCD-EF1234567891", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> StatusMessage = new();

    [Output(Guid = "A3B4C5D6-C7D8-9012-ABCD-EF1234567892", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> FrameCount = new();

    [Output(Guid = "B4C5D6E7-D8E9-0123-ABCD-EF1234567893", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> SenderName = new();

    [Output(Guid = "C5D6E7F8-E9F0-1234-ABCD-EF1234567894", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> SenderId = new();
    #endregion

    #region Private Fields
    // Listener state. A generation is joined before a replacement starts, so two listener
    // threads can never own the same port and race on the per-frame scratch.
    private Thread? _listenerThread;
    private UdpClient? _udpClient;
    private readonly object _listenerLock = new();
    private volatile bool _runListener;
    private volatile bool _printToLog;
    private string _lastLocalIp = string.Empty;
    private int _lastPort = DefaultPort;
    private bool _wasActive;
    private double _lastRetryTime;
    private volatile bool _disposed;

    // Inputs snapshotted by the evaluator every frame. The listener thread decodes packets
    // at arbitrary times and must never touch input slots - those are only safe during
    // evaluation.
    private volatile int _cachedExpectedSenderId;
    private volatile string _cachedNameFilter = string.Empty;
    private volatile float _cachedMinX = -1f;
    private volatile float _cachedMaxX = 1f;
    private volatile float _cachedMinY = -1f;
    private volatile float _cachedMaxY = 1f;

    // Frame reconstruction - PONK frames are split into chunks; we reassemble per sender AND frame
    // number. The frame number alone is not unique: two senders on the group (two TiXL instances, a
    // TiXL next to MadMapper) both count 0..255, and merging their chunks made every packet reset the
    // buffer - nothing ever completed and the receiver starved although it lists both senders.
    private readonly ConcurrentDictionary<(int SenderId, byte FrameNumber), FrameBuffer> _frameBuffers = new();
    private volatile int _lastCompletedSenderId;            // listener thread writes, evaluator reads
    private volatile string _lastCompletedSenderName = string.Empty;
    private long _lastFrameTicks;

    /** Frames waiting for their remaining chunks; beyond this the table is dropped. */
    private const int MaxPendingFrames = 256;

    /** Hard bound on the bytes held by in-progress frames across all senders. */
    private const long MaxPendingBytes = 64L * 1024 * 1024;
    private long _pendingBytes;

    /** Distinct sender names kept for the filter dropdown. */
    private const int MaxKnownStreams = 256;

    private readonly List<(int SenderId, byte FrameNumber)> _staleFrameKeys = [];
    private readonly List<string> _staleStreamKeys = [];

    // Live stream discovery: every valid packet header updates the sender's last-seen stamp.
    // The FilterSenderName dropdown lists these.
    private sealed record StreamInfo(string SenderName, string RemoteIp, DateTime LastSeen);
    private readonly ConcurrentDictionary<string, StreamInfo> _knownStreams = new(StringComparer.Ordinal);

    // Stats
    private long _totalFramesReceived;   // Interlocked: listener thread increments, evaluator reads
    private long _totalPointsReceived;
    private long _packetsReceived;
    private long _invalidPackets;
    private long _chunksOrphaned;

    // Converted point buffer (re-used to avoid per-frame allocations on the listener thread)
    private readonly List<LaserPoint> _pointAccumulator = new(2048);

    // Status (one immutable snapshot for lock-free readers)
    private volatile StatusInfo _status = new("Inactive", IStatusProvider.StatusLevel.Notice);

    // Discovery writes every valid header; skip the dictionary churn while the pair is unchanged.
    private string _lastRecordedName = string.Empty;
    private IPAddress? _lastRecordedAddress;

    // Output snapshot
    private volatile StructuredList? _currentPoints;
    #endregion

    #region Frame Buffer
    private sealed class FrameBuffer
    {
        public byte FrameNumber;
        public byte ChunkCount;
        public uint DataCrc;
        public int SenderId;
        public DateTime FirstChunkTime;
        public DateTime LastChunkTime;
        public readonly Dictionary<byte, byte[]> Chunks = new(8);
    }
    #endregion

    #region Constructor & Dispose
    public PONKInput()
    {
        LaserPoints.UpdateAction += Update;
        StatusMessage.UpdateAction += Update;
        FrameCount.UpdateAction += Update;
        SenderName.UpdateAction += Update;
        SenderId.UpdateAction += Update;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopListening();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Dispose();

        base.Dispose(disposing);
    }
    #endregion

    #region IStatusProvider
    public IStatusProvider.StatusLevel GetStatusLevel() => _status.Level;
    public string GetStatusMessage() => _status.Message;
    #endregion

    #region ICustomDropdownHolder
    string ICustomDropdownHolder.GetValueForInput(Guid id)
    {
        if (id == FilterSenderName.Id) return FilterSenderName.Value ?? string.Empty;
        return string.Empty;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid id)
    {
        if (id == FilterSenderName.Id)
        {
            if (_knownStreams.IsEmpty)
            {
                yield return "No streams seen yet (needs Active on)";
                yield break;
            }

            yield return "(any sender)";
            foreach (var stream in _knownStreams.Values.OrderBy(s => s.SenderName, StringComparer.Ordinal))
                yield return $"{stream.SenderName} ({stream.RemoteIp})";
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid id, string? value, bool isListItem)
    {
        if (string.IsNullOrEmpty(value) || !isListItem) return;
        if (id == FilterSenderName.Id)
        {
            // Entries look like "Name (ip)" - store just the name; "(any sender)" clears the filter
            if (value.StartsWith("(any", StringComparison.Ordinal))
            {
                FilterSenderName.SetTypedInputValue(string.Empty);
            }
            else
            {
                var paren = value.LastIndexOf('(');
                var name = paren > 0 ? value[..paren].TrimEnd() : value.TrimEnd();
                FilterSenderName.SetTypedInputValue(name);
            }
        }
    }
    #endregion

    #region Update
    private void Update(EvaluationContext context)
    {
        // Top-level safety net – TiXL will terminate on any unhandled operator exception.
        try
        {
            _printToLog = PrintToLog.GetValue(context);
            var active = Active.GetValue(context);
            var localIp = ProtocolInterfaces.ResolveAddressString(ProtocolInterfaces.Keys.Ponk);
            var port = Port.GetValue(context);

            // Snapshot the decode-time inputs for the listener thread
            _cachedExpectedSenderId = ExpectedSenderId.GetValue(context);
            var nameFilter = FilterSenderName.GetValue(context);
            _cachedNameFilter = nameFilter ?? string.Empty;
            _cachedMinX = MinX.GetValue(context);
            _cachedMaxX = MaxX.GetValue(context);
            _cachedMinY = MinY.GetValue(context);
            _cachedMaxY = MaxY.GetValue(context);

            var settingsChanged = active != _wasActive
                                  || !string.Equals(localIp, _lastLocalIp, StringComparison.Ordinal)
                                  || port != _lastPort;
            if (settingsChanged)
            {
                StopListening();
                if (active) StartListening(localIp, port);
                _wasActive = active;
                _lastLocalIp = localIp;
                _lastPort = port;
            }
            else if (active && (_listenerThread == null || !_listenerThread.IsAlive))
            {
                // Auto-recover if the thread died (e.g. socket error)
                if (context.LocalTime - _lastRetryTime > 2.0)
                {
                    _lastRetryTime = context.LocalTime;
                    StartListening(localIp, port);
                }
            }

            var timeout = Timeout.GetValue(context);
            CleanupStaleFrames(timeout);

            // A sender that vanished mid-show must not leave its last frame replaying forever:
            // the scanner would keep drawing stale content with no live source behind it.
            if (_currentPoints != null && float.IsFinite(timeout) && timeout > 0)
            {
                var lastTicks = Interlocked.Read(ref _lastFrameTicks);
                var ageSeconds = lastTicks == 0
                                     ? double.MaxValue
                                     : (Stopwatch.GetTimestamp() - lastTicks) / (double)Stopwatch.Frequency;
                if (ageSeconds > timeout)
                {
                    _currentPoints = null;
                    SetStatus($"No data for {timeout:0.0}s - output blanked", IStatusProvider.StatusLevel.Warning);
                }
            }

            // Surface the most recent frame on the output slot
            LaserPoints.Value = _currentPoints ?? s_emptyList;
            FrameCount.Value = (int)Math.Min(Interlocked.Read(ref _totalFramesReceived), int.MaxValue);
            SenderId.Value = _lastCompletedSenderId;
            SenderName.Value = _lastCompletedSenderName;
            StatusMessage.Value = _status.Message;
        }
        catch (Exception e)
        {
            Log.Error($"PONKInput: update failed – {e.Message}", this);
            // Degrade to safe defaults.
            LaserPoints.Value = s_emptyList;
            FrameCount.Value = (int)Math.Min(Interlocked.Read(ref _totalFramesReceived), int.MaxValue);
            SenderId.Value = _lastCompletedSenderId;
            SenderName.Value = _lastCompletedSenderName;
            StatusMessage.Value = $"Error: {e.Message}";
        }
    }

    private static readonly StructuredList s_emptyList = new StructuredList<LaserPoint>(Array.Empty<LaserPoint>());
    #endregion

    #region Listener Lifecycle
    private void StartListening(string localIpString, int port)
    {
        lock (_listenerLock)
        {
            if (_disposed || _listenerThread is { IsAlive: true })
                return;

            _runListener = true;

            // Resolve the bind settings on the evaluation thread - the listener reads only
            // its parameters from here on, never input slots.
            var listenIp = IPAddress.Any;
            if (!string.IsNullOrWhiteSpace(localIpString) && IPAddress.TryParse(localIpString, out var parsed))
                listenIp = parsed;

            _listenerThread = new Thread(() => ListenLoop(listenIp, port))
                                  {
                                      IsBackground = true,
                                      Name = "PONKInputListener",
                                  };
            _listenerThread.Start();
        }

        if (_printToLog) Log.Debug($"PONK Input: Starting listener thread on {localIpString}:{port}.", this);
    }

    private void StopListening()
    {
        Thread? thread;
        lock (_listenerLock)
        {
            if (!_runListener && _listenerThread == null)
                return;

            _runListener = false;
            if (_printToLog) Log.Debug("PONK Input: Stopping listener.", this);

            // Close the socket owned by the current generation. It may have been assigned
            // before Bind, so a stop during bring-up still releases the port.
            try { Interlocked.Exchange(ref _udpClient, null)?.Close(); }
            catch { /* ignore */ }

            thread = _listenerThread;
        }

        // Join before allowing a replacement: two listeners on the same port would both
        // receive every packet and feed the same per-frame scratch from two threads.
        if (thread != null && thread != Thread.CurrentThread && thread.IsAlive)
        {
            try { thread.Join(300); }
            catch { /* ignore */ }
        }

        lock (_listenerLock)
        {
            if (thread == null || !thread.IsAlive)
            {
                _listenerThread = null;
                SetStatus("Listener stopped", IStatusProvider.StatusLevel.Notice);
            }
            else
            {
                SetStatus("Listener is still shutting down - retrying", IStatusProvider.StatusLevel.Warning);
                _runListener = true; // let the update loop try the stop again
            }
        }
    }
    #endregion

    #region Listen Loop
    private void ListenLoop(IPAddress listenIp, int port)
    {
        UdpClient? currentUdpClient = null;
        try
        {
            currentUdpClient = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = false };
            // Publish before Bind so a stop during bring-up can still close this socket.
            Interlocked.Exchange(ref _udpClient, currentUdpClient);
            currentUdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            currentUdpClient.Client.Bind(new IPEndPoint(listenIp, port));

            // Join the PONK default multicast group on the chosen interface. Unicast packets
            // sent to the same port land in the same socket, so we get both for free.
            try
            {
                var mreq = new MulticastOption(DefaultMulticastAddress, listenIp);
                currentUdpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, mreq);
            }
            catch (Exception ex)
            {
                if (_printToLog) Log.Warning($"PONK Input: AddMembership failed ({ex.Message}); continuing unicast-only", this);
            }

            SetStatus($"Listening on {listenIp}:{port} (multicast {DefaultMulticastAddress}:{DefaultPort})",
                      IStatusProvider.StatusLevel.Notice);
            if (_printToLog)
                Log.Debug($"PONK Input: Bound to {listenIp}:{port}, joined {DefaultMulticastAddress}", this);

            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            while (_runListener && !_disposed)
            {
                byte[] data;
                try
                {
                    if (currentUdpClient == null) break;
                    data = currentUdpClient.Receive(ref remoteEndPoint);
                }
                catch (SocketException ex)
                {
                    if (_runListener && !_disposed)
                        Log.Warning($"PONK Input socket error: {ex.Message}", this);
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (data.Length == 0) continue;
                Interlocked.Increment(ref _packetsReceived);
                ProcessPacket(data, remoteEndPoint.Address);
            }
        }
        catch (Exception e)
        {
            SetStatus($"PONK Input bind error: {e.Message}", IStatusProvider.StatusLevel.Error);
            if (_printToLog) Log.Error($"PONK Input: {e.Message}", this);
        }
        finally
        {
            try { currentUdpClient?.Close(); } catch { /* ignore */ }
            Interlocked.CompareExchange(ref _udpClient, null, currentUdpClient);
        }
    }
    #endregion

    #region Packet Processing
    private void ProcessPacket(byte[] data, IPAddress remoteAddress)
    {
        try
        {
            if (data.Length < HeaderSize) { Interlocked.Increment(ref _invalidPackets); return; }

            // Magic
            for (var i = 0; i < 8; i++)
            {
                if (data[i] != (byte)HeaderString[i]) { Interlocked.Increment(ref _invalidPackets); return; }
            }

            // Version
            if (data[8] > ProtocolVersion) { Interlocked.Increment(ref _invalidPackets); return; }

            var senderId = data[9] | (data[10] << 8) | (data[11] << 16) | (data[12] << 24);
            var senderName = ReadSenderName(data, 13, 32);

            var frameNumber = data[45];
            var chunkCount = data[46];
            var chunkNumber = data[47];
            var dataCrc = (uint)(data[48] | (data[49] << 8) | (data[50] << 16) | (data[51] << 24));

            if (chunkCount == 0 || chunkNumber >= chunkCount)
            {
                Interlocked.Increment(ref _invalidPackets);
                return;
            }

            // UDP itself bounds a datagram at 65,507 bytes; reject a claimed payload beyond
            // that instead of letting a malformed packet size the reassembly allocation.
            if (data.Length - HeaderSize > 65507)
            {
                Interlocked.Increment(ref _invalidPackets);
                return;
            }

            // Discovery: record every valid-header sender before any filtering, so the
            // FilterSenderName dropdown lists streams we are deliberately ignoring.
            RecordStream(senderName, remoteAddress);

            // Filter decisions use the evaluator's snapshots, not the input slots
            var expectedSender = _cachedExpectedSenderId;
            if (expectedSender != 0 && senderId != expectedSender) return;

            var nameFilter = _cachedNameFilter;
            if (!string.IsNullOrEmpty(nameFilter) && !string.Equals(senderName, nameFilter, StringComparison.Ordinal)) return;

            // Get or create the per-frame buffer. Bound both the frame count and the bytes
            // held by partially reassembled frames: an unauthenticated UDP sender must not
            // be able to reserve hundreds of megabytes by claiming chunks it never sends.
            if (_frameBuffers.Count > MaxPendingFrames
                || Interlocked.Read(ref _pendingBytes) > MaxPendingBytes)
            {
                ClearPendingFrames();
            }

            var frameKey = (senderId, frameNumber);
            var frame = _frameBuffers.GetOrAdd(frameKey, _ => new FrameBuffer
            {
                FrameNumber = frameNumber,
                FirstChunkTime = DateTime.UtcNow,
                LastChunkTime = DateTime.UtcNow,
            });

            // First chunk for this frame number - lock to make sure the chunk count/CRC are consistent
            var complete = false;
            lock (frame)
            {
                if (frame.Chunks.Count == 0)
                {
                    frame.ChunkCount = chunkCount;
                    frame.DataCrc = dataCrc;
                    frame.SenderId = senderId;
                }
                else if (frame.ChunkCount != chunkCount || frame.DataCrc != dataCrc || frame.SenderId != senderId)
                {
                    // A later chunk disagrees with the first - drop the frame and start over
                    Interlocked.Add(ref _pendingBytes, -SumChunkBytes(frame));
                    frame.Chunks.Clear();
                    frame.ChunkCount = chunkCount;
                    frame.DataCrc = dataCrc;
                    frame.SenderId = senderId;
                    Interlocked.Increment(ref _chunksOrphaned);
                }

                // Copy payload (data.Length - HeaderSize bytes). Zero-length chunks are
                // stored too: canonical senders emit an empty trailing chunk when the
                // frame data is an exact multiple of the chunk quantum, and the chunk
                // count must be reachable for the frame to complete.
                var payloadLength = data.Length - HeaderSize;
                var payload = new byte[payloadLength];
                if (payloadLength > 0)
                    Array.Copy(data, HeaderSize, payload, 0, payloadLength);

                // A repeated chunk replaces its previous buffer; account for the difference.
                if (frame.Chunks.TryGetValue(chunkNumber, out var previous))
                    Interlocked.Add(ref _pendingBytes, payloadLength - previous.Length);
                else
                    Interlocked.Add(ref _pendingBytes, payloadLength);

                frame.Chunks[chunkNumber] = payload;
                frame.LastChunkTime = DateTime.UtcNow;
                complete = frame.Chunks.Count >= frame.ChunkCount;
            }

            if (!complete) return;

            // All chunks received - reassemble and verify CRC
            byte[] frameData;
            lock (frame)
            {
                frameData = CombineChunks(frame);
            }

            var actualCrc = ComputeChecksum(frameData);
            if (actualCrc != dataCrc)
            {
                Interlocked.Increment(ref _invalidPackets);
                if (_printToLog)
                    Log.Warning($"PONK Input: Frame {frameNumber} CRC mismatch (expected {dataCrc:X8}, got {actualCrc:X8})", this);
                if (_frameBuffers.TryRemove(frameKey, out var rejected))
                    Interlocked.Add(ref _pendingBytes, -SumChunkBytes(rejected));
                return;
            }

            // Parse the frame and publish points
            ParseFrameAndPublish(frameData, senderId, senderName);
            if (_frameBuffers.TryRemove(frameKey, out var completed))
                Interlocked.Add(ref _pendingBytes, -SumChunkBytes(completed));
            Interlocked.Increment(ref _totalFramesReceived);
            _lastCompletedSenderId = senderId;
            _lastCompletedSenderName = senderName;
            Interlocked.Exchange(ref _lastFrameTicks, Stopwatch.GetTimestamp());
            // No dirty-flag invalidations here: this runs on the listener thread and the
            // output slots are Animated, so the evaluator republishes them every frame.

            if (_printToLog)
                Log.Debug($"PONK Input: Frame {frameNumber} from '{senderName}' (id {senderId}) - {Interlocked.Read(ref _totalPointsReceived)} points total", this);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _invalidPackets);
            if (_printToLog) Log.Warning($"PONK Input: Packet parse error - {ex.Message}", this);
        }
    }

    private static long SumChunkBytes(FrameBuffer frame)
    {
        // Chunks is written under lock(frame) on the listener thread; this runs on
        // evaluator/other threads (also right after TryRemove, when the listener can still
        // deliver a late chunk into the same buffer), so enumerate under the same lock.
        lock (frame)
        {
            long sum = 0;
            foreach (var chunk in frame.Chunks.Values)
                sum += chunk.Length;
            return sum;
        }
    }

    /// <summary>Drops every partially reassembled frame and its byte accounting. Listener thread only.</summary>
    private void ClearPendingFrames()
    {
        var removed = 0;
        foreach (var pair in _frameBuffers)
        {
            if (!_frameBuffers.TryRemove(pair.Key, out var frame))
                continue;

            Interlocked.Add(ref _pendingBytes, -SumChunkBytes(frame));
            removed++;
        }

        Interlocked.Add(ref _chunksOrphaned, removed);
    }

    private static byte[] CombineChunks(FrameBuffer frame)
    {
        // Per spec: chunks should be received in order but the receiver accepts any order.
        var totalSize = 0;
        for (var i = 0; i < frame.ChunkCount; i++)
        {
            if (frame.Chunks.TryGetValue((byte)i, out var c)) totalSize += c.Length;
        }
        var combined = new byte[totalSize];
        var offset = 0;
        for (var i = 0; i < frame.ChunkCount; i++)
        {
            if (!frame.Chunks.TryGetValue((byte)i, out var c)) continue;
            Array.Copy(c, 0, combined, offset, c.Length);
            offset += c.Length;
        }
        return combined;
    }

    private void RecordStream(string senderName, IPAddress remoteAddress)
    {
        // The dropdown only needs one entry per sender; rewriting it for every chunk at
        // PONK packet rates was pure allocation and dictionary churn.
        if (string.Equals(senderName, _lastRecordedName, StringComparison.Ordinal)
            && Equals(remoteAddress, _lastRecordedAddress))
            return;

        _lastRecordedName = senderName;
        _lastRecordedAddress = remoteAddress;

        var remoteIp = remoteAddress.ToString();
        var key = string.IsNullOrEmpty(senderName) ? remoteIp : senderName;
        if (_knownStreams.Count > MaxKnownStreams)
            _knownStreams.Clear();

        _knownStreams[key] = new StreamInfo(string.IsNullOrEmpty(senderName) ? key : senderName, remoteIp, DateTime.UtcNow);
    }

    private static string ReadSenderName(byte[] data, int offset, int length)
    {
        // 32-byte null-padded ASCII
        var end = offset;
        while (end < offset + length && data[end] != 0) end++;
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    /// <summary>
    /// "CRC" in the PONK spec is the sum of all frame-data bytes, mod 2^32. Not a polynomial CRC.
    /// </summary>
    private static uint ComputeChecksum(byte[] data)
    {
        uint sum = 0;
        for (var i = 0; i < data.Length; i++) sum += data[i];
        return sum;
    }
    #endregion

    #region Frame Parsing
    private void ParseFrameAndPublish(byte[] data, int senderId, string senderName)
    {
        // Use the evaluator's snapshots of the range inputs
        var minX = _cachedMinX;
        var maxX = _cachedMaxX;
        var minY = _cachedMinY;
        var maxY = _cachedMaxY;

        _pointAccumulator.Clear();
        var offset = 0;

        while (offset < data.Length)
        {
            // Data format
            if (offset + 1 > data.Length) break;
            var dataFormat = data[offset++];

            // Meta data count
            if (offset + 1 > data.Length) break;
            var metaCount = data[offset++];
            if (offset + metaCount * 12 > data.Length) break;
            offset += metaCount * 12; // skip metadata - TiXL doesn't apply render hints server-side

            // Point count
            if (offset + 2 > data.Length) break;
            var pointCount = data[offset] | (data[offset + 1] << 8);
            offset += 2;

            int bytesPerPoint;
            switch (dataFormat)
            {
                case DataFormatXyF32RgbU8: bytesPerPoint = 11; break; // 2*4 + 3*1
                case DataFormatXyRgbU16: bytesPerPoint = 10; break;   // 5*2
                default:
                    if (_printToLog)
                        Log.Warning($"PONK Input: Unsupported data format {dataFormat}, aborting frame", this);
                    return;
            }

            if (offset + (long)pointCount * bytesPerPoint > data.Length) break;

            for (var i = 0; i < pointCount; i++)
            {
                float x, y;
                int r, g, b;

                if (dataFormat == DataFormatXyF32RgbU8)
                {
                    // PONK is explicitly little-endian; BitConverter.ToSingle is machine-endian
                    // and would decode garbage on a big-endian host.
                    x = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, 4));
                    y = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4, 4));
                    r = data[offset + 8];
                    g = data[offset + 9];
                    b = data[offset + 10];
                }
                else // DataFormatXyRgbU16
                {
                    var xu = ReadU16(data, offset);
                    var yu = ReadU16(data, offset + 2);
                    var ru = ReadU16(data, offset + 4);
                    var gu = ReadU16(data, offset + 6);
                    var bu = ReadU16(data, offset + 8);

                    // u16 -> [-1, 1] (PONK convention) -> f32
                    x = (xu / 65535f) * 2f - 1f;
                    y = (yu / 65535f) * 2f - 1f;
                    // u16 0..65535 -> u8
                    r = ru >> 8;
                    g = gu >> 8;
                    b = bu >> 8;
                }

                offset += bytesPerPoint;

                // A non-finite coordinate from the wire has no position: (int)NaN is int.MinValue,
                // which the safety clamp turns into a lit point in the field corner. Keep the
                // point (the path stays intact) but dark, at the centre.
                if (!float.IsFinite(x) || !float.IsFinite(y))
                {
                    _pointAccumulator.Add(LaserPoint.CreateBlanked(0, 0));
                    continue;
                }

                // Remap PONK's [-1, 1] XY to the user-configured output range, then scale to ILDA 16-bit
                var lx = RemapAndScale(x, minX, maxX);
                var ly = RemapAndScale(y, minY, maxY);
                _pointAccumulator.Add(new LaserPoint(lx, ly, Scale8To16(r), Scale8To16(g), Scale8To16(b)));
            }
        }

        Interlocked.Add(ref _totalPointsReceived, _pointAccumulator.Count);

        if (_pointAccumulator.Count == 0) return;

        // Publish: copy out of the accumulator (the listener thread keeps reusing it)
        var snapshot = new LaserPoint[_pointAccumulator.Count];
        _pointAccumulator.CopyTo(snapshot);
        _currentPoints = new StructuredList<LaserPoint>(snapshot);
    }

    private static int RemapAndScale(float value, float min, float max)
    {
        // Map the PONK normal range onto the user-specified range, then scale to ILDA 16-bit.
        // Broken range inputs (NaN/infinity/reversed) collapse to the full field instead of
        // producing NaN coordinates that the scanner cannot reject.
        if (!float.IsFinite(min) || !float.IsFinite(max))
        {
            min = -1f;
            max = 1f;
        }
        else if (min > max)
        {
            (min, max) = (max, min);
        }

        if (max - min < 1e-6f)
            return 0;

        var t = Math.Clamp((value - min) / (max - min), 0f, 1f);
        return (int)(t * 2f * LaserPointCoordMax) - LaserPointCoordMax;
    }

    private static int Scale8To16(int v8) => v8 << 8; // 0..255 -> 0..65280
    private static int ReadU16(byte[] data, int offset) => data[offset] | (data[offset + 1] << 8);
    #endregion

    #region Cleanup & Status
    private void CleanupStaleFrames(float timeoutSeconds)
    {
        if (!float.IsFinite(timeoutSeconds) || timeoutSeconds <= 0)
            return;

        var cutoff = DateTime.UtcNow.AddSeconds(-timeoutSeconds);
        _staleFrameKeys.Clear();
        foreach (var kvp in _frameBuffers)
        {
            // Expire on the LAST chunk: a large frame still arriving on a slow link must not
            // be purged merely because its first chunk is older than the timeout.
            var lastSeen = kvp.Value.LastChunkTime > kvp.Value.FirstChunkTime
                               ? kvp.Value.LastChunkTime
                               : kvp.Value.FirstChunkTime;
            if (lastSeen < cutoff)
                _staleFrameKeys.Add(kvp.Key);
        }

        foreach (var key in _staleFrameKeys)
        {
            if (!_frameBuffers.TryRemove(key, out var frame))
                continue;

            Interlocked.Add(ref _pendingBytes, -SumChunkBytes(frame));
            Interlocked.Add(ref _chunksOrphaned, frame.Chunks.Count);
        }

        // Streams that went quiet drop out of the FilterSenderName dropdown
        _staleStreamKeys.Clear();
        foreach (var kvp in _knownStreams)
        {
            if (kvp.Value.LastSeen < cutoff) _staleStreamKeys.Add(kvp.Key);
        }
        foreach (var key in _staleStreamKeys)
        {
            _knownStreams.TryRemove(key, out _);
        }
    }

    private void SetStatus(string message, IStatusProvider.StatusLevel level)
    {
        _status = new StatusInfo(message, level);
    }
    #endregion
}
