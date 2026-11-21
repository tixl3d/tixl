#nullable enable
using System;
using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LaserCore.EtherDream.Net.Device;
using LaserCore.EtherDream.Net.Discovery;
using LaserCore.EtherDream.Net.Dto;
using LaserCore.EtherDream.Net.Enums;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator.Interfaces;

namespace T3.Core.Output;

/// <summary>
/// The device side of a laser output, owned by the Output Setup instead of graph operators: one
/// <see cref="LaserOutputStream"/> per bound laser plug streams the frames the setup's laser
/// pipeline produces to that plug's hardware. The stream runs its own connection/send threads and
/// applies the plug's device settings (scan rate, brightness ceiling, channel order) as the final
/// audit before encoding.
///
/// Safety model: the output's safety limits (safe zone, jump/dwell limits, brightness) are
/// re-applied by <see cref="SubmitFrame"/> at the stream door, so nothing unclamped can reach an
/// encode even from a caller that skipped the setup pipeline; the global
/// <see cref="LaserMasterEnable"/> blanks the beam while the scanners keep scanning the last
/// geometry (settled blanking), a frame watchdog blanks the beam when no fresh frames arrive
/// within its grace period, and every frame leads with dark approach points so a fresh frame
/// never streaks the beam from the scanners' park position.
/// </summary>
public sealed class LaserOutputStream : IDisposable
{
    /// <summary>Frame watchdog grace period: no fresh frames for this long blanks the beam.</summary>
    private const float WatchdogTimeoutSec = 1f;

    public static LaserOutputStream? Create(LaserPlug plug)
    {
        try
        {
            return plug.Kind switch
                       {
                           LaserPlug.Kinds.EtherDream => new LaserOutputStream(new EtherDreamStream(plug)),
                           LaserPlug.Kinds.Ponk => new LaserOutputStream(new PonkStream(plug)),
                           LaserPlug.Kinds.Citp => new LaserOutputStream(new CitpStream(plug)),
                           LaserPlug.Kinds.ShowNet => new LaserOutputStream(new ShowNetStream(plug)),
                           _ => null,
                       };
        }
        catch (Exception e)
        {
            Log.Warning($"Laser output stream for plug '{plug.Name}' failed to start - {e.Message}");
            return null;
        }
    }

    private LaserOutputStream(IStream inner)
    {
        _inner = inner;
        _safetyStats = new LaserSafetyTransforms.Stats();
    }

    private readonly IStream _inner;
    private readonly List<LaserPoint> _safetyWorkList = new(4096);
    private readonly List<LaserPoint> _safetySpareList = new(4096);
    private LaserPoint[] _safetyOutput = Array.Empty<LaserPoint>();
    private LaserSafetyTransforms.Stats _safetyStats;
    private readonly object _safetyLock = new();
    private int _disposed;

    /// <summary>
    /// Thread-safe status snapshot. The send/connection threads write it while the UI and the
    /// driver read it; a two-field write (message + level) could otherwise be observed torn.
    /// </summary>
    private sealed record StatusInfo(string Message, IStatusProvider.StatusLevel Level);

    /// <summary>
    /// Queues a frame for the send thread, re-applying the output's safety limits at the door -
    /// the non-bypassable stage that runs no matter what the caller did upstream. Copies out of
    /// the caller's buffer. Safe to call from the render thread and from a preview/debug caller:
    /// the safety scratch is serialized, and the transports copy the submitted frame before
    /// returning, so the reusable output buffer is never visible to two threads at once.
    /// Callers whose pipeline already ran the same config this frame pass
    /// <paramref name="alreadySafetyApplied"/> to skip the identical second pass.
    /// </summary>
    public void SubmitFrame(in LaserFrame frame, in LaserSafetyTransforms.Config safety, bool alreadySafetyApplied = false)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        var points = frame.Points;
        if (points == null)
            return;

        var count = Math.Min(frame.Count, points.Length);
        if (count <= 0)
            return;

        // The submitted frame may carry a Count smaller than its capacity; always hand the
        // transport an exact frame so it never streams the producer's blanked tail.
        var exactFrame = new LaserFrame(points, count, frame.SafetyApplied);
        if (alreadySafetyApplied)
        {
            _inner.SubmitFrame(exactFrame);
            return;
        }

        lock (_safetyLock)
        {
            // The safety transforms are idempotent (clamps, subdivision, dwell blanking), so
            // re-applying the stage the setup pipeline already ran is safe and never inflates
            // the point count. Stats reset inside Apply, so a reused instance stays per-frame.
            _safetyWorkList.Clear();
            for (var i = 0; i < count; i++)
                _safetyWorkList.Add(points[i]);

            var result = LaserSafetyTransforms.Apply(_safetyWorkList, _safetySpareList, in safety, ref _safetyStats);
            var resultCount = Math.Min(result.Count, result.Capacity);
            if (_safetyOutput.Length < resultCount)
                _safetyOutput = new LaserPoint[resultCount];

            for (var i = 0; i < resultCount; i++)
                _safetyOutput[i] = result[i];

            _inner.SubmitFrame(new LaserFrame(_safetyOutput, resultCount, true));
        }
    }

    /// <summary>
    /// Takes the beam dark right away while keeping the connection warm: for a muted output, or
    /// content that went empty. Without it the stream keeps looping the last lit frame until the
    /// frame watchdog fires a second later. A single blanked point at the field centre - the
    /// transports lead it with their dark approach, so the scanners travel there beam-off.
    /// </summary>
    public void SubmitBlank()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _inner.SubmitFrame(new LaserFrame(DarkFrame, DarkFrame.Length, true));
    }

    private static readonly LaserPoint[] DarkFrame = [LaserPoint.CreateBlanked(0, 0)];

    public bool IsConnected => _inner.IsConnected;

    public IStatusProvider.StatusLevel StatusLevel => _inner.StatusLevel;
    public string StatusMessage => _inner.StatusMessage;

    /// <summary>
    /// True while the stream still runs with the values it was built from. Device settings are
    /// applied when a stream is created (scan rate, brightness ceiling, channel order, keepalive,
    /// address), so the driver rebuilds the stream when they no longer match - otherwise editing
    /// them on the plug card would only take effect after a restart.
    /// </summary>
    public bool MatchesPlug(LaserPlug plug) => _inner.DescribeMismatch(plug) == null;

    /// <summary>Applies the plug settings that a running stream takes without a reconnect (brightness
    /// ceiling, channel order, scan rate, keepalive). Call it each pump, before <see cref="MatchesPlug"/>.</summary>
    public void ApplyLiveSettings(LaserPlug plug) => _inner.ApplyLiveSettings(plug);

    /// <summary>The plug setting that no longer matches this stream, or null when it still does.
    /// A persistent mismatch makes the driver rebuild the stream repeatedly - the reason a plug
    /// card can read "connected, then instantly disconnected" over and over.</summary>
    public string? DescribeMismatch(LaserPlug plug) => _inner.DescribeMismatch(plug);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Dispose synchronously: the transport teardown itself is non-blocking (sockets are
        // closed/cancelled, DAC disposal is gated by the transport's own pending-disposal
        // table). Fire-and-forget here used to make a rebuilt stream race the old one for the
        // same DAC - both connected, both sending, the old one's Stop halting the new show.
        try
        {
            _inner.Dispose();
        }
        catch
        {
            // Sockets may already be dead during shutdown.
        }
    }

    /// <summary>Protocol-specific transport behind a <see cref="LaserOutputStream"/>.</summary>
    private interface IStream : IDisposable
    {
        void SubmitFrame(in LaserFrame frame);
        bool IsConnected { get; }
        IStatusProvider.StatusLevel StatusLevel { get; }
        string StatusMessage { get; }

        /// <summary>The plug setting that needs a new connection, or null when the live stream still serves the plug.</summary>
        string? DescribeMismatch(LaserPlug plug);

        /// <summary>Applies the plug settings the running stream reads per chunk; transports without any do nothing.</summary>
        void ApplyLiveSettings(LaserPlug plug)
        {
        }
    }

    #region Shared helpers (blanking, watchdog, dark approach)

    /// <summary>Returns a blanked copy (positions kept, colors zeroed) via a reusable scratch buffer.</summary>
    internal static LaserPoint[] BlankFrame(LaserPoint[] source, ref LaserPoint[] scratch)
    {
        if (scratch.Length != source.Length)
            scratch = new LaserPoint[source.Length];
        LaserSafetyTransforms.BlankCopyInto(source, scratch);
        return scratch;
    }

    /// <summary>True when no fresh frame has been submitted within the watchdog window.</summary>
    internal static bool WatchdogElapsed(ref long lastSubmitTimestamp)
    {
        var last = Interlocked.Read(ref lastSubmitTimestamp);
        if (last == 0)
            return false;
        return Environment.TickCount64 - last > (long)(WatchdogTimeoutSec * 1000f);
    }

    /// <summary>Leads the frame with a dark ramp from the scanners' current position to the
    /// frame's first point: they travel from wherever they are parked (or wherever the previous
    /// frame ended) with the beam off, in bounded steps instead of one maximal-acceleration
    /// jump. A fresh frame after (re)begin starts from the park position (0,0); while looping,
    /// it starts from the previous frame's exit so consecutive passes chain smoothly.</summary>
    internal static LaserPoint[] PrependDarkApproach(LaserPoint[] frame, int fromX, int fromY, ref LaserPoint[] scratch)
    {
        const int approachPoints = 6;
        var total = frame.Length + approachPoints;
        if (scratch.Length != total)
            scratch = new LaserPoint[total];

        var first = frame[0];
        var targetX = first.X;
        var targetY = first.Y;

        // The ramp fills all but the last sample; the final sample sits on the target so the
        // scanners are settled there before the first lit point arrives.
        for (var i = 0; i < approachPoints; i++)
        {
            var t = (i + 1) / (float)approachPoints;
            var x = (int)MathF.Round(fromX + (targetX - fromX) * t);
            var y = (int)MathF.Round(fromY + (targetY - fromY) * t);
            scratch[i] = LaserPoint.CreateBlanked(x, y);
        }

        Array.Copy(frame, 0, scratch, approachPoints, frame.Length);
        return scratch;
    }

    /// <summary>
    /// Builds one data-command payload for a pull-model DAC: the unsent tail of the current frame,
    /// then - when the frame is small next to the DAC's free room - whole repeats of it (each behind
    /// its own dark approach from the previous end point, same as a fresh loop pass). A small frame
    /// (a 180-point aim rectangle is 6 ms of playback at 30 kpps) otherwise empties the DAC buffer
    /// faster than the sender's wake-up cadence can refill it (a Windows timer tick is ~15 ms), and the
    /// DAC drops to idle and has to be re-prepared over and over. Repeats are added whole, never cut
    /// mid-frame, so the stream always ends on the frame's last point and the next pass continues
    /// seamlessly. <paramref name="frameAfterApproach"/> is what the tail was cut from.
    /// </summary>
    internal static LaserPoint[] BuildTopUpChunk(LaserPoint[] frameAfterApproach, int offset, LaserPoint[] rawFrame, int room,
                                                 ref LaserPoint[] scratch, out int consumedFromFrame)
    {
        var remaining = frameAfterApproach.Length - offset;
        consumedFromFrame = Math.Min(room, remaining);

        if (remaining >= room || rawFrame.Length == 0)
        {
            var plain = new LaserPoint[consumedFromFrame];
            Array.Copy(frameAfterApproach, offset, plain, 0, consumedFromFrame);
            return plain;
        }

        var chunk = new LaserPoint[room];
        Array.Copy(frameAfterApproach, offset, chunk, 0, remaining);
        var count = remaining;

        while (true)
        {
            var last = chunk[count - 1];
            var copyLength = rawFrame.Length + 6; // PrependDarkApproach adds a 6-point ramp
            if (count + copyLength > room)
                break;

            var next = PrependDarkApproach(rawFrame, last.X, last.Y, ref scratch);
            Array.Copy(next, 0, chunk, count, next.Length);
            count += next.Length;
        }

        if (count == room)
            return chunk;

        var trimmed = new LaserPoint[count];
        Array.Copy(chunk, 0, trimmed, 0, count);
        return trimmed;
    }

    /// <summary>
    /// The buffer capacity to hand the device library. Its default is the original Ether Dream's
    /// 1799 points; newer hardware advertises a larger buffer in its discovery broadcast, and a
    /// DAC that is already connected stops broadcasting - so the fill level from its own status is
    /// the fallback proof that the buffer is bigger than the assumption. The library paces its
    /// sends against this number, so the stream must never use a different one.
    /// </summary>
    internal static ushort ResolveBufferCapacity(ushort advertisedCapacity, ushort observedFullness = 0)
    {
        var capacity = advertisedCapacity > 0 ? advertisedCapacity : DefaultBufferCapacity;
        return observedFullness > capacity ? observedFullness : capacity;
    }

    /// <summary>The library's own default for hardware that never advertised its capacity.</summary>
    internal const ushort DefaultBufferCapacity = 1799;

    /// <summary>A plug left at its default scan rate (0) streams at 30 kpps — the rule every
    /// transport shares; the clamp bounds must not drift between protocols.</summary>
    internal static int NormalizeScanRate(int scanRate)
    {
        var clamped = Math.Clamp(scanRate, 100, 100000);
        return clamped == 100 && scanRate < 100 ? 30000 : clamped;
    }

    /// <summary>Queue bookkeeping shared by both transports: bounds the queue, copies the frame
    /// out of the upstream buffer (producers reuse their allocation every frame) and resets the
    /// watchdog clock.</summary>
    internal static LaserPoint[]? CopyForQueue(in LaserFrame frame, ConcurrentQueue<LaserPoint[]> queue, int maxQueuedFrames)
    {
        var count = frame.Count;
        if (count <= 0 || frame.Points == null)
            return null;

        while (queue.Count >= maxQueuedFrames)
        {
            if (!queue.TryDequeue(out _))
                break;
        }

        // Exactly the frame, never the producer buffer's capacity: the tail past Count is
        // padding, and streaming it would spend datagram budget on blanked origin dwells.
        var copy = GC.AllocateUninitializedArray<LaserPoint>(count);
        Array.Copy(frame.Points, copy, count);
        return copy;
    }

    /// <summary>
    /// One PONK path-metadata entry: an 8-byte key followed by a little-endian float32. The key field
    /// is exactly 8 bytes with no terminator slot - the protocol's own keys (`PATHNUMB`,
    /// `MAXSPEED`) fill it completely and are matched as 8 bytes by receivers - so a shorter key is
    /// zero-padded and an 8-character key must not lose its last byte.
    /// </summary>
    internal static void WriteMetaEntry(Span<byte> destination, string key, float value)
    {
        destination[..8].Clear();
        Encoding.ASCII.GetBytes(key.AsSpan(0, Math.Min(key.Length, 8)), destination[..8]);
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(8, 4), value);
    }

    internal static void WriteMetaEntry(BinaryWriter writer, string key, float value)
    {
        Span<byte> entry = stackalloc byte[MetaEntrySize];
        WriteMetaEntry(entry, key, value);
        writer.Write(entry);
    }

    /** 8 byte key + float32 value. */
    internal const int MetaEntrySize = 12;
    #endregion

    #region EtherDream transport
    /// <summary>
    /// Streams to an Ether Dream DAC over TCP, lifted from the former [EtherDreamOutput] operator.
    /// The unkillable rules of the send loop carry over unchanged: StreamPoints is only called once
    /// a status poll confirmed buffer room (a full buffer blocks inside the library's socket lock
    /// forever when the engine has halted), that room is computed from the SAME capacity the
    /// library was constructed with (otherwise the two disagree and a healthy link looks stalled),
    /// and a halted engine is recovered IN PLACE (Prepare discards the stuck queue; reconnect churn
    /// is what wedges this DAC's firmware).
    /// </summary>
    private sealed class EtherDreamStream : IStream
    {
        public EtherDreamStream(LaserPlug plug)
        {
            _plug = plug;
            var device = plug.EtherDreamSettings;
            _scanRate = NormalizeScanRate(device.ScanRate);
            _plugAddress = plug.Address ?? string.Empty;
            _maxBrightness = Math.Clamp(device.MaxBrightness, 0f, 1f);
            _swapColorChannels = device.SwapColorChannels;

            // The plug picker lists DACs while a stream runs: a connected DAC stops its
            // discovery broadcasts, so start the shared listener with the stream.
            LaserPlugDiscovery.EnsureEtherDreamDiscovery();

            StartConnection();
        }

        private readonly LaserPlug _plug;
        private readonly string _plugAddress;

        // Read per chunk by the send loop, so a settings edit applies without a reconnect.
        private volatile int _scanRate;
        private volatile float _maxBrightness;
        private volatile bool _swapColorChannels;

        // Library instances / connection state
        private Dac? _dac;
        private CancellationTokenSource? _connectionCts;
        private Task? _connectionTask;
        private readonly ConcurrentQueue<LaserPoint[]> _pointQueue = new();
        private volatile LaserPoint[]? _lastFrame;   // written by the send loop, cleared on teardown

        // Timing / retry tuning (operator-proven values)
        private const int SendRetryDelayMs = 250;
        private const int WarmupRetryDelayMs = 500;
        private const int MaxWarmupRetries = 120;      // ~60s for the light engine to become ready
        private const int MaxConsecutiveSendErrors = 5;
        private const int SendStallTimeoutMs = 3000;   // StreamPoints spins on a full, stopped buffer — abandon and reconnect
        private const int PlaybackStallDelayMs = 400;  // not-playing for this long while streaming → explicit re-begin
        private const int RebeginMinIntervalMs = 1000;
        private const int PendingDisposeWaitMs = 2000; // bounded wait for a previous session's socket to close
        private const int MaxQueuedFrames = 2;
        private const int MinChunkPoints = 32;
        private const int DiscoveryPollDelayMs = 500;

        public bool IsConnected => _isConnected;
        public IStatusProvider.StatusLevel StatusLevel => _status.Level;
        public string StatusMessage => _status.Message;

        /// <summary>
        /// Only the device address needs a new connection. Closing a session mid-write leaves the DAC's
        /// firmware holding a stale full buffer until it is power-cycled, so everything else is applied
        /// to the running stream here: scan rate, brightness ceiling and channel order are read per
        /// chunk, and the interface is not used by this transport at all.
        /// </summary>
        public void ApplyLiveSettings(LaserPlug plug)
        {
            var device = plug.EtherDreamSettings;
            _scanRate = NormalizeScanRate(device.ScanRate);
            _maxBrightness = Math.Clamp(device.MaxBrightness, 0f, 1f);
            _swapColorChannels = device.SwapColorChannels;
        }

        public string? DescribeMismatch(LaserPlug plug)
        {
            var address = plug.Address ?? string.Empty;
            if (address != _plugAddress && !IsAutoSelectedDevice(address))
                return $"address '{_plugAddress}' -> '{plug.Address}'";

            return null;
        }

        /// <summary>A plug left empty streams to the first discovered DAC; the setup card then writes that
        /// DAC's address into the plug, which is the connection the stream already has.</summary>
        private bool IsAutoSelectedDevice(string address)
        {
            return _plugAddress.Length == 0
                   && address.Length > 0
                   && (address == _autoSelectedIp || address == _lastConnectIp);
        }

        public void SubmitFrame(in LaserFrame frame)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            var copy = CopyForQueue(frame, _pointQueue, MaxQueuedFrames);
            if (copy == null)
                return;

            Interlocked.Exchange(ref _lastEnqueueTimestamp, Environment.TickCount64);
            _watchdogBlanked = false;
            _pointQueue.Enqueue(copy);
        }

        private void StartConnection()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            TearDown();

            var cts = new CancellationTokenSource();
            _connectionCts = cts;
            var token = cts.Token;
            _connectionTask = Task.Run(async () =>
                            {
                                try
                                {
                                    // A plug without an address connects to the first DAC discovery
                                    // reports - the listener starts together with this stream, so
                                    // wait for its first announcement instead of giving up.
                                    var address = ResolveAddress();
                                    while (string.IsNullOrWhiteSpace(address))
                                    {
                                        SetStatus("No DAC address (discovering...)", IStatusProvider.StatusLevel.Notice);
                                        await Task.Delay(DiscoveryPollDelayMs, token);
                                        address = ResolveAddress();
                                    }

                                    await ConnectionLoopAsync(address, token);
                                }
                                catch (OperationCanceledException)
                                {
                                    // Torn down while still waiting for discovery
                                }
                                finally
                                {
                                    // The loop owns the CTS; by the time we get here cancellation was
                                    // already processed, so disposing cannot race with Task.Delay registrations
                                    cts.Dispose();
                                }
                            });
        }

        /// <summary>The plug's address, or the first discovered DAC when empty (zero-touch).</summary>
        private string ResolveAddress()
        {
            if (!string.IsNullOrWhiteSpace(_plug.Address))
                return _plug.Address;

            try
            {
                if (_autoSelectedIp == null || !DeviceDiscovery.DiscoveredDevices.ContainsKey(_autoSelectedIp))
                {
                    var first = DeviceDiscovery.DiscoveredDevices.Values.FirstOrDefault();
                    var picked = first.Ip;
                    if (!string.IsNullOrWhiteSpace(picked) && picked != _autoSelectedIp)
                    {
                        _autoSelectedIp = picked;
                        Log.Debug($"EtherDream stream: auto-selected DAC at {_autoSelectedIp}");
                    }
                }

                return _autoSelectedIp ?? string.Empty;
            }
            catch
            {
                // The discovery loop mutates the dictionary concurrently - a transient
                // race just skips this frame's auto-pick
                return _autoSelectedIp ?? string.Empty;
            }
        }

        private volatile string? _autoSelectedIp;

        /// <summary>
        /// Teardowns in flight, keyed by DAC address. The socket close runs on the thread pool
        /// because it can block on the library's socket lock while an abandoned send unwinds;
        /// without the gate a stream rebuilt onto the same DAC would dial it while the old
        /// session still holds it, and the old session's Stop could halt the new show.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Task> PendingDisposals = new(StringComparer.OrdinalIgnoreCase);

        private static async Task WaitForPreviousSessionAsync(string ipAddress, CancellationToken token)
        {
            if (!PendingDisposals.TryGetValue(ipAddress, out var pending))
                return;

            // Bounded: a teardown stuck on a dead socket must not keep the laser dark.
            await Task.WhenAny(pending, Task.Delay(PendingDisposeWaitMs, token));
            token.ThrowIfCancellationRequested();
        }

        /// <summary>The discovery entry for an address, if the listener heard the DAC while it was idle.</summary>
        private static bool TryGetDiscoveredDevice(string ipAddress, out DacDto device)
        {
            device = default;
            try
            {
                return DeviceDiscovery.DiscoveredDevices.TryGetValue(ipAddress, out device);
            }
            catch
            {
                // The discovery loop mutates the table concurrently
                return false;
            }
        }

        private void TearDown()
        {
            var cts = Interlocked.Exchange(ref _connectionCts, null);
            if (cts != null)
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            // Give the send loop a bounded chance to leave before its Dac is disposed; otherwise
            // it can call Stop/Ping/StreamPoints on a disposed library object while the teardown
            // task runs. Cancellation aborts its waits, so this is normally milliseconds.
            var connectionTask = Interlocked.Exchange(ref _connectionTask, null);
            if (connectionTask is { IsCompleted: false })
            {
                try { connectionTask.Wait(500); }
                catch { /* the loop's own catch handles its failure */ }
            }

            var dac = Interlocked.Exchange(ref _dac, null);
            if (dac != null)
            {
                var ip = _lastConnectIp ?? string.Empty;
                var disposal = Task.Run(() =>
                                            {
                                                try { DisposeDacQuietly(dac); }
                                                catch { }
                                            });

                // A new stream to the same DAC must wait for this teardown: the device serves one
                // host at a time, and the stop below would otherwise land on the new session.
                if (ip.Length > 0)
                {
                    PendingDisposals[ip] = disposal;
                    _ = disposal.ContinueWith(_ =>
                    {
                        if (PendingDisposals.TryGetValue(ip, out var current) && ReferenceEquals(current, disposal))
                            PendingDisposals.TryRemove(ip, out var removed);
                    }, TaskContinuationOptions.ExecuteSynchronously);
                }

                LaserPlugDiscovery.ClearConnectedDevice(ip);
            }

            _isConnected = false;
            _isPlaying = false;
            _pointQueue.Clear();
            _lastFrame = null;
        }

        /// <summary>
        /// A session that ended while its queue was still full greets the next connect with points
        /// on an idle engine - a Stop (our own <see cref="DisposeDacQuietly"/> among them) leaves
        /// them in place. Prepare is the protocol's own reset: it discards the queue, so the stream
        /// starts empty instead of waiting for the send loop's in-place recovery at every reconnect.
        /// </summary>
        private static void DiscardStalePoints(Dac dac, DacStatusDto status, string ipAddress)
        {
            if (status.BufferFullness == 0 || status.PlayBackState == PlayBackEngineState.Playing)
                return;

            try
            {
                // 'p' is only accepted from Idle; a session that stopped after a begin reports
                // Prepared, where a Stop is the valid step back to Idle first.
                if (status.PlayBackState != PlayBackEngineState.Idle)
                    dac.Stop();

                var ack = DacResponse.ParseAckCode(dac.Prepare().Response);
                Log.Debug($"EtherDream stream: discarded {status.BufferFullness} stale points at {ipAddress} ({ack})");
            }
            catch (Exception e)
            {
                // The send loop's in-place recovery still covers a failed clear
                Log.Debug($"EtherDream stream: clearing stale points at {ipAddress} failed - {e.Message}");
            }
        }

        /// <summary>
        /// Runs on a background thread because the Dac constructor performs a blocking TCP connect
        /// that can stall for seconds when the device is unreachable. Retries until cancelled with
        /// progressive backoff 1s → 30s — hammering an unreachable or just-rebooted DAC with fixed
        /// fast retries is exactly the churn that wedges its firmware.
        /// </summary>
        private async Task ConnectionLoopAsync(string ipAddress, CancellationToken token)
        {
            var attempt = 0;

            while (!token.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
            {
                attempt++;
                Dac? dac = null;
                var connectedAtUtc = DateTime.UtcNow;
                try
                {
                    SetStatus(attempt == 1
                                  ? $"Connecting to {ipAddress}..."
                                  : $"Connecting to {ipAddress} (attempt {attempt})...", IStatusProvider.StatusLevel.Notice);

                    await WaitForPreviousSessionAsync(ipAddress, token);

                    var discovered = TryGetDiscoveredDevice(ipAddress, out var device);
                    var capacity = ResolveBufferCapacity(discovered ? device.Identity.BufferCapacity : (ushort)0);
                    var maxPointRate = discovered ? device.Identity.MaxPointRate : 0u;

                    dac = new Dac(ipAddress, capacity, maxPointRate);
                    if (token.IsCancellationRequested)
                    {
                        DisposeDacQuietly(dac);
                        return;
                    }

                    try
                    {
                        var helloStatus = dac.Ping().DacStatus;

                        // The status reports the fill level but never the capacity. A DAC that
                        // discovery never heard (a connected device stops broadcasting) leaves the
                        // library on its default 1799-point assumption - its own fullness is the
                        // proof that the buffer is bigger, and the number to pace against.
                        var observedFullness = helloStatus.BufferFullness;
                        var resolved = ResolveBufferCapacity(capacity, observedFullness);
                        if (resolved != capacity)
                        {
                            DisposeDacQuietly(dac);
                            dac = new Dac(ipAddress, resolved, maxPointRate);
                            helloStatus = dac.Ping().DacStatus;
                            Log.Debug($"EtherDream stream: DAC at {ipAddress} reported {observedFullness} buffered points - "
                                      + $"using {resolved} as its buffer capacity instead of {capacity}");
                        }

                        DiscardStalePoints(dac, helloStatus, ipAddress);
                    }
                    catch { /* the normal send path reports connection failures */ }

                    // A stale Dac can still raise events after a reconnect; only the current
                    // session may write this stream's shared playback state.
                    dac.StatusUpdated += (ack, play, light, fullness) =>
                                         {
                                             if (ReferenceEquals(Volatile.Read(ref _dac), dac))
                                                 HandleStatusUpdate(ack, play, light, fullness);
                                         };
                    dac.DeviceConnected += () =>
                                           {
                                               if (ReferenceEquals(Volatile.Read(ref _dac), dac))
                                                   HandleConnectionStateChanged(true);
                                           };
                    dac.DeviceDisconnected += () =>
                                              {
                                                  if (ReferenceEquals(Volatile.Read(ref _dac), dac))
                                                      HandleConnectionStateChanged(false);
                                              };
                    dac.SafetyFaultDetected += reason =>
                           {
                               if (ReferenceEquals(Volatile.Read(ref _dac), dac))
                                   ReportSafetyFault(reason);
                           };

                    // A connected DAC stops its discovery broadcasts — report it so the plug picker
                    // can still offer the device a show is streaming to.
                    LaserPlugDiscovery.SetConnectedDevice("Ether Dream", ipAddress);

                    _dac = dac;
                    _lastConnectIp = ipAddress;

                    // TearDown cancels before it takes _dac, so a teardown that ran during the
                    // hello above missed this DAC - release it here or its session stays open.
                    if (token.IsCancellationRequested)
                    {
                        if (Interlocked.CompareExchange(ref _dac, null, dac) == dac)
                        {
                            DisposeDacQuietly(dac);
                            LaserPlugDiscovery.ClearConnectedDevice(ipAddress);
                        }

                        return;
                    }

                    _isConnected = true;
                    _killedSocket = false;
                    // Arm the stall watchdog from connect time: the light engine NACKs prepare/begin
                    // for a moment while warming up, and a kick fired from stale (default) timestamps
                    // would flag that normal warmup as a stall on the very first send.
                    Interlocked.Exchange(ref _lastPlayingUtcTicks, DateTime.UtcNow.Ticks);
                    SetStatus($"Connected to {ipAddress}", IStatusProvider.StatusLevel.Success);

                    // Runs until cancelled or until a fatal error is thrown
                    await SendLoopAsync(dac, token);
                    return;
                }
                catch (OperationCanceledException)
                {
                    DisposeDacQuietly(dac);
                    return;
                }
                catch (Exception e)
                {
                    DisposeDacQuietly(dac);
                    _isConnected = false;

                    if (token.IsCancellationRequested)
                        return;

                    SetStatus($"Connection lost: {e.Message} - retrying...", IStatusProvider.StatusLevel.Warning);

                    // Name every dropped session, not only the first failed connect: the next
                    // connect reports the stale full queue this drop left behind, and without a
                    // line here the log only ever shows that symptom.
                    var heldFor = DateTime.UtcNow - connectedAtUtc;
                    if (heldFor > TimeSpan.FromSeconds(30))
                    {
                        Log.Warning($"EtherDream stream: connection to {ipAddress} dropped after {heldFor.TotalSeconds:0}s - "
                                    + $"{e.GetType().Name}: {e.Message}");
                    }
                    else if (attempt == 1)
                    {
                        Log.Warning($"EtherDream stream: connection to {ipAddress} failed - {e.Message}");
                    }
                    else
                    {
                        Log.Debug($"EtherDream stream: connection to {ipAddress} attempt {attempt} failed - {e.Message}");
                    }

                    // A connection that held for a while earns a fresh ladder - it served the
                    // show and its death is a new incident, not more of the same failure.
                    if (DateTime.UtcNow - connectedAtUtc > TimeSpan.FromSeconds(30))
                        attempt = 1;
                }

                var backoffMs = (int)Math.Min(1000L << Math.Min(attempt - 1, 5), 30_000);
                try
                {
                    await Task.Delay(backoffMs, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Streams queued points to the DAC under two unkillable rules. (1) StreamPoints is only
        /// called once a status poll confirmed the DAC actually has buffer room - a full buffer
        /// blocks inside the library's socket lock forever when the engine has halted. (2) A
        /// halted engine is recovered IN PLACE: Prepare() discards the stuck queue and the next
        /// send re-runs the library's p→d→b session.
        /// </summary>
        private async Task SendLoopAsync(Dac dac, CancellationToken token)
        {
            var consecutiveErrors = 0;
            var warmupRetries = 0;

            // The same capacity the library paces its own sends against. Growing it from observed
            // fullness (as an earlier version did) made the two disagree: the stream then handed
            // StreamPoints chunks the library considered too big for the buffer, and it pinged
            // inside its socket lock until the send watchdog recycled a healthy connection.
            var capacity = dac.BufferCapacity > 0 ? (int)dac.BufferCapacity : DefaultBufferCapacity;

            var recoverEscalation = 0;              // 0 = soft Prepare, 1+ = full Stop+ClearEStop+Prepare
            var lastRecoverUtc = DateTime.MinValue; // rate-limits the in-place recovery
            var lastPingUtc = DateTime.MinValue;
            var pingFailures = 0;

            // Every call is a NEW DAC session: the firmware starts from an empty buffer with the
            // scanners parked, so nothing carried over from the previous connection is valid. An
            // in-flight frame would continue at its mid-frame offset - the first chunk opens the
            // beam on whatever point that is, one lit maximal jump away from park. Forget the
            // partial frame and the loop frame its approach would ramp from, so the next frame
            // starts with a dark approach from center (the same state a fresh stream has).
            ResetSessionStreamingState();

            // Seed the buffer state from the device before the first send: after a connect that
            // found (or failed to clear) a full queue, a zero fullness would compute room the
            // device does not have.
            PollDac(dac, ref lastPingUtc, ref pingFailures);

            while (!token.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
            {
                if (_isPlaying)
                    recoverEscalation = 0;

                // Master off: STOP the DAC. Streaming a blanked copy of the last frame keeps the
                // scanners sweeping the geometry with zeroed colors - correct for ILDA-compliant
                // blanking, but a projector that emits on zero-color points keeps drawing. The
                // stop command halts playback outright (light source off, engine idle) while the
                // TCP session stays warm, so re-enabling the master resumes within one poll.
                var masterOff = !LaserMasterEnable.Enabled;
                if (masterOff)
                {
                    _pointQueue.Clear();
                    _sendFrame = null;
                    _needsDarkApproach = true;

                    if (!_masterStopped)
                    {
                        _masterStopped = true;
                        try
                        {
                            dac.Stop();
                            SetStatus("Laser master off - DAC stopped", IStatusProvider.StatusLevel.Notice);
                        }
                        catch (Exception e)
                        {
                            // A dead stop command takes the normal error ladder; the state below
                            // still skips streaming, so nothing lit can leave this stream.
                            SetStatus($"Master off, stop failed: {e.Message}", IStatusProvider.StatusLevel.Warning);
                        }
                    }

                    // Hold: heartbeat only, no points out. The 5 ms idle poll also detects a
                    // dead link so the connection loop can rebuild it while disabled.
                    PollDac(dac, ref lastPingUtc, ref pingFailures);
                    try { await Task.Delay(5, token); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }

                if (_masterStopped)
                {
                    // Master came back: reset the engine's state machine so the send path's
                    // own prepare/begin cycle starts a clean session.
                    _masterStopped = false;

                    // The operator re-armed: this is the one place an E-stop is cleared. A cause
                    // that is still present makes the device report E-stop again, which disarms
                    // the master again (see LatchDeviceEStop).
                    _deviceEStopLatched = false;
                    try
                    {
                        dac.ClearEStop();
                        dac.Prepare();
                    }
                    catch
                    {
                        // The normal send path re-prepares per chunk; a failure here surfaces there.
                    }

                    // The stop left the scanners wherever playback happened to be; the engine now
                    // starts over. Forget the loop frame so the first approach ramps from the park
                    // position instead of from the exit of a frame the scanners never finished.
                    ResetSessionStreamingState();
                }

                // Pick the frame to stream: fresh frames first, otherwise loop the last one
                // so the buffer stays fed between the driver's ~30 Hz submissions. Looping
                // bypasses the watchdog clock, which measures FRESH frames only.
                if (_sendFrame == null || _sendOffset >= _sendFrame.Length)
                {
                    LaserPoint[]? frame = null;
                    var previousFrame = _lastFrame;
                    if (_pointQueue.TryDequeue(out var points) && points.Length > 0)
                    {
                        frame = points;
                        // Every fresh frame leads beam-off: the approach to its first point is
                        // drawn dark no matter what the content's own first point looks like,
                        // so a pattern flip can never streak the beam across the show.
                        _needsDarkApproach = true;
                    }
                    else if (previousFrame is { Length: > 0 })
                    {
                        // The loop seam (this frame's exit back to its own start) gets the same
                        // dark ramp as a fresh frame: the DAC replays the frame ~40 times a
                        // second, so an unapproached seam would ring the scanners at loop rate.
                        _needsDarkApproach = true;

                        if (WatchdogElapsed(ref _lastEnqueueTimestamp))
                        {
                            if (!_watchdogBlanked)
                            {
                                _watchdogBlanked = true;
                                Log.Warning("EtherDream stream: frame watchdog - no new frames, blanking output");
                                SetStatus("Frame watchdog - no new frames, output blanked", IStatusProvider.StatusLevel.Warning);
                            }
                            frame = BlankFrame(previousFrame, ref _blankFrameScratch);
                        }
                        else
                        {
                            frame = previousFrame;
                        }
                    }

                    if (frame == null)
                    {
                        // Idle (no content yet): heartbeat keeps the connection warm instead of
                        // letting it go stale - a live show must never drop the DAC link.
                        PollDac(dac, ref lastPingUtc, ref pingFailures);
                        try { await Task.Delay(5, token); }
                        catch (OperationCanceledException) { return; }
                        continue;
                    }

                    // Galvo safety: the lead-in must be a dark ramp so the travel to the shape
                    // start happens beam-off AND in bounded steps (a single-sample jump is a
                    // maximal-acceleration demand that rings the scanners). After an engine
                    // (re)start the scanners sit parked at center; while streaming they sit
                    // wherever the previous frame ended - ramp from the real position.
                    _sendFrameRaw = frame;
                    if (_needsDarkApproach && frame.Length > 0)
                    {
                        if (previousFrame is { Length: > 0 } last)
                        {
                            var lastPoint = last[last.Length - 1];
                            frame = PrependDarkApproach(frame, lastPoint.X, lastPoint.Y, ref _approachScratch);
                        }
                        else
                        {
                            frame = PrependDarkApproach(frame, 0, 0, ref _approachScratch);
                        }

                        _needsDarkApproach = false;
                    }

                    _sendFrame = frame;
                    _sendOffset = 0;
                }

                // The library paces against this very number: a fullness above it can only be a
                // transient reading between commands, never extra room to spend.
                var room = capacity - Math.Min(_bufferFullness, capacity);

                // In-place playback recovery: after a halt the engine sits idle while the buffer
                // stays full. Prepare() discards the stuck queue; the chunk sends then re-run the
                // library's own p→d→b session and playback resumes. Gated on a FULL buffer: while
                // there is room, our own chunk sends re-prepare on the fly, and a recovery here
                // would fight the streaming and reset playback every couple of seconds.
                if (!_isPlaying
                    && room < MinChunkPoints
                    && DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastPlayingUtcTicks) > TimeSpan.FromMilliseconds(PlaybackStallDelayMs).Ticks
                    && DateTime.UtcNow - lastRecoverUtc > TimeSpan.FromMilliseconds(RebeginMinIntervalMs))
                {
                    lastRecoverUtc = DateTime.UtcNow;
                    try
                    {
                        if (recoverEscalation == 0)
                        {
                            dac.Prepare();
                        }
                        else
                        {
                            // Soft recovery did not take - reset the DAC's state machine fully.
                            // Never ClearEStop here: an E-stop is released by the operator only.
                            dac.Stop();
                            dac.Prepare();
                        }

                        Log.Warning($"EtherDream stream: playback halt - in-place recovery (level {recoverEscalation}) sent");
                        SetStatus($"Playback halted - recovering (level {recoverEscalation})...", IStatusProvider.StatusLevel.Warning);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"EtherDream stream: in-place recovery failed - {e.Message} (recycling connection)");
                        throw;
                    }

                    recoverEscalation++;
                    pingFailures = 0;
                    _sendOffset = 0;
                    _needsDarkApproach = true;
                    continue;
                }

                // Chunked streaming: push exactly as many points as the DAC reports room for,
                // in one data command each - the socket lock is held only for milliseconds.
                if (room < MinChunkPoints)
                {
                    PollDac(dac, ref lastPingUtc, ref pingFailures);
                    try { await Task.Delay(2, token); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }

                var chunk = BuildTopUpChunk(_sendFrame, _sendOffset, _sendFrameRaw ?? _sendFrame, room, ref _approachScratch, out var chunkLength);

                try
                {
                    // Each chunk fits the reported room, so this returns after a single data
                    // command. The bounded wait is belt and braces for a socket-level hang; on
                    // timeout the socket is killed (unwinding the abandoned call) and the
                    // connection recycled. Await (not blocking Wait) keeps the send loop off
                    // a thread-pool thread while the library's synchronous call runs.
                    var sendTask = Task.Run(() => dac.StreamPoints(ConvertPoints(chunk), (uint)_scanRate));
                    await sendTask.WaitAsync(TimeSpan.FromMilliseconds(SendStallTimeoutMs), token).ConfigureAwait(false);

                    consecutiveErrors = 0;
                    warmupRetries = 0;
                    pingFailures = 0;
                    _sendOffset += chunkLength;
                    if (_sendOffset >= _sendFrame.Length)
                    {
                        if (_sendFrameRaw is { } rawFrame)
                            RememberFrame(rawFrame);
                        _sendFrame = null;

                        // A completed frame after a warning (stall, halt recovery) means the show
                        // is back - the status line must not stay on the old warning forever.
                        // But the frame watchdog's blank frames complete too: while the watchdog
                        // is active its Warning must persist (the fresh-frame enqueue clears
                        // _watchdogBlanked, and the next completed frame returns to Streaming).
                        if (_status.Level != IStatusProvider.StatusLevel.Success && !_watchdogBlanked)
                            SetStatus("Streaming", IStatusProvider.StatusLevel.Success);
                    }
                }
                catch (TimeoutException) when (!token.IsCancellationRequested)
                {
                    // WaitAsync timed out: the library call is abandoned inside the socket
                    // lock. Kill the TCP socket first so that call unwinds, then recycle the
                    // connection - reusing it would deadlock every later command.
                    _killedSocket = true;
                    KillDacSocket(dac);
                    throw;
                }
                catch (InvalidOperationException ex) when (!token.IsCancellationRequested)
                {
                    // Light engine not ready yet - warm-up needs waiting out
                    if (++warmupRetries > MaxWarmupRetries)
                        throw;

                    if (ex.Message.Contains("EmergencyStop"))
                    {
                        LatchDeviceEStop();
                    }
                    else
                    {
                        SetStatus($"Waiting for light engine to become ready ({warmupRetries * WarmupRetryDelayMs / 1000}s)...", IStatusProvider.StatusLevel.Notice);
                    }

                    await Task.Delay(WarmupRetryDelayMs, token);
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    consecutiveErrors++;

                    // A timed-out send leaves an abandoned StreamPoints spinning on the library's
                    // socket lock - every later command would deadlock on it. Its socket was
                    // killed, which unwinds that call; recycle the whole connection.
                    if (consecutiveErrors >= MaxConsecutiveSendErrors || _killedSocket)
                        throw;

                    SetStatus($"Connection problems - retrying ({consecutiveErrors}/{MaxConsecutiveSendErrors})...", IStatusProvider.StatusLevel.Warning);
                    await Task.Delay(SendRetryDelayMs, token);
                }
            }
        }

        /// <summary>
        /// Throttled DAC status poll while no points flow (or the buffer is full): refreshes
        /// buffer fullness and playback state straight from the response. Throws after repeated
        /// failures so a silently-dead link gets recycled instead of spinning forever.
        /// </summary>
        private void PollDac(Dac dac, ref DateTime lastPingUtc, ref int pingFailures)
        {
            // Fast while waiting for buffer room: the poll cadence caps sustainable send rate.
            if (DateTime.UtcNow - lastPingUtc < TimeSpan.FromMilliseconds(4))
                return;

            lastPingUtc = DateTime.UtcNow;
            try
            {
                var status = dac.Ping().DacStatus;
                pingFailures = 0;
                _bufferFullness = status.BufferFullness;
                var playing = status.PlayBackState == PlayBackEngineState.Playing;
                _isPlaying = playing;
                if (playing)
                {
                    Interlocked.Exchange(ref _lastPlayingUtcTicks, DateTime.UtcNow.Ticks);
                }
                else
                {
                    _needsDarkApproach = true;
                }
            }
            catch
            {
                if (++pingFailures >= 5)
                    throw new InvalidOperationException("DAC heartbeat lost");
            }
        }

        /// <summary>Queued frames are never mutated, so they are remembered by reference. The blank
        /// scratch is rewritten on the next pass and has to be copied.</summary>
        private void RememberFrame(LaserPoint[] points)
        {
            if (!ReferenceEquals(points, _blankFrameScratch))
            {
                _lastFrame = points;
                return;
            }

            if (_lastFrame == null || _lastFrame.Length != points.Length || ReferenceEquals(_lastFrame, points))
                _lastFrame = new LaserPoint[points.Length];

            Array.Copy(points, _lastFrame, points.Length);
        }

        /// <summary>Drops the streaming state that belongs to one DAC session (see
        /// <see cref="SendLoopAsync"/>): the partial frame, its raw source and the loop frame.</summary>
        private void ResetSessionStreamingState()
        {
            _sendFrame = null;
            _sendFrameRaw = null;
            _sendOffset = 0;
            _lastFrame = null;
            _needsDarkApproach = true;
        }

        private void HandleStatusUpdate(AckCode ack, PlayBackEngineState playBackEngineState, LightEngineState lightEngineState, ushort bufferFullness)
        {
            // Runs on a library callback thread - only plain field writes here.
            if (lightEngineState == LightEngineState.EmergencyStop)
                LatchDeviceEStop();

            _bufferFullness = bufferFullness;
            var playing = playBackEngineState == PlayBackEngineState.Playing;
            _isPlaying = playing;
            if (playing)
            {
                Interlocked.Exchange(ref _lastPlayingUtcTicks, DateTime.UtcNow.Ticks);
            }
            else
            {
                // Engine went idle - the scanners will park. Arm the dark approach so the next
                // frame travels to its start point beam-off.
                _needsDarkApproach = true;
            }
        }

        /// <summary>
/// A device E-stop (hardware interlock, a network E-stop from another host, over-temperature)
/// is never cleared by this stream: restarting after an emergency stop must take a
/// deliberate operator action. The global LASER master is disarmed, so every output goes
/// dark, and re-arming it is that action - the re-arm sends ClearEStop, and if the cause is
/// still there the device reports E-stop again and the master drops again.
/// </summary>
private void LatchDeviceEStop()
{
    if (_deviceEStopLatched)
        return;

    _deviceEStopLatched = true;
    var wasArmed = LaserMasterEnable.Enabled;
    LaserMasterEnable.Enabled = false;
    Log.Warning(wasArmed
                    ? $"EtherDream stream: device at {_lastConnectIp} reports EMERGENCY STOP - LASER master disarmed; clear the cause, then re-arm"
                    : $"EtherDream stream: device at {_lastConnectIp} reports EMERGENCY STOP");
    SetStatus("Device E-stop - clear the cause, then re-arm the LASER master", IStatusProvider.StatusLevel.Error);
}

/// <summary>The library raises a fault on every DAC response while the flag stays set (each
/// poll, every few ms); only a new reason, or the same one after a quiet minute, is logged.</summary>
private void ReportSafetyFault(string reason)
{
    var now = Environment.TickCount64;
    if (reason == _lastFaultReason && now - Interlocked.Read(ref _lastFaultLogTicks) < 60_000)
        return;

    _lastFaultReason = reason;
    Interlocked.Exchange(ref _lastFaultLogTicks, now);
    Log.Warning($"EtherDream stream: safety fault - {reason}");
}

private volatile string? _lastFaultReason;
private long _lastFaultLogTicks;

/// <summary>Set when the device reported E-stop; cleared only by the operator re-arming the master.</summary>
private volatile bool _deviceEStopLatched;

private void HandleConnectionStateChanged(bool isConnected)
        {
            _isConnected = isConnected;
            if (isConnected)
                _needsDarkApproach = true;
            SetStatus(isConnected ? "Connected" : "Disconnected", isConnected ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Notice);
        }

        private DacPointDto[] ConvertPoints(LaserPoint[] points)
        {
            // Final output audit: coordinates/colors are clamped into hardware range and
            // scaled by the brightness ceiling right before encoding - independent of
            // whatever upstream content sent.
            var scale = _maxBrightness;
            var swap = _swapColorChannels;
            var dacPoints = new DacPointDto[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                var p = points[i];
                if (p.I <= 0)
                    p = LaserPoint.CreateBlanked(p.X, p.Y); // dark on every projector, also those that ignore I

                dacPoints[i] = new DacPointDto
                                   {
                                       Control = 0,
                                       X = (short)Math.Clamp(p.X, short.MinValue, short.MaxValue),
                                       Y = (short)Math.Clamp(p.Y, short.MinValue, short.MaxValue),
                                       R = (ushort)Math.Clamp((int)MathF.Round(Math.Max(swap ? p.B : p.R, 0) * scale), 0, ushort.MaxValue),
                                       G = (ushort)Math.Clamp((int)MathF.Round(Math.Max(p.G, 0) * scale), 0, ushort.MaxValue),
                                       B = (ushort)Math.Clamp((int)MathF.Round(Math.Max(swap ? p.R : p.B, 0) * scale), 0, ushort.MaxValue),
                                       I = (ushort)Math.Clamp((int)MathF.Round(Math.Max(p.I, 0) * scale), 0, ushort.MaxValue),
                                       U1 = 0,
                                       U2 = 0,
                                   };
            }

            return dacPoints;
        }

        private static void DisposeDacQuietly(Dac? dac)
        {
            if (dac == null)
                return;

            // Ask the DAC to stop, then discard its queue BEFORE the socket dies. Stop alone
            // returns to Idle with the points still buffered - the state the next session then
            // reports as "buffer full while idle"; Prepare clears it. Killing the connection
            // mid-data is what leaves stale points behind in the first place. The stop travels
            // on the still-open socket; the grace lets both commands flush.
            //
            // Both commands take the library's socket lock. On a wedged/half-open DAC they can
            // block forever, so they run on a bounded task: once the bound expires, the socket
            // is killed to unwind them rather than leaking the task and holding the DAC session
            // open for the next stream's connect.
            var graceful = Task.Run(() =>
                                        {
                                            try
                                            {
                                                dac.Stop();
                                                dac.Prepare();
                                                Thread.Sleep(50);
                                            }
                                            catch
                                            {
                                                // Already dead - the kill below is all that's left
                                            }
                                        });
            if (!graceful.Wait(300))
                KillDacSocket(dac);

            try
            {
                KillDacSocket(dac);
            }
            catch
            {
                // Reflection failed - Dispose below still tries the polite path
            }

            try
            {
                dac.Dispose();
            }
            catch
            {
                // Socket may already be dead
            }
        }

        /// <summary>
        /// Closes the DAC's TCP socket directly, bypassing the library's socket lock. The only
        /// writer that can hold that lock forever is an abandoned StreamPoints spinning on a
        /// halted engine - and Dispose itself takes the lock, so without this a stalled send
        /// would deadlock the whole teardown.
        /// </summary>
        private static void KillDacSocket(Dac dac)
        {
            if (DacSocketField?.GetValue(dac) is not TcpClient socket)
            {
                if (Interlocked.Exchange(ref _socketFieldMissingLogged, 1) == 0)
                    Log.Warning("EtherDream stream: Dac._socket is unavailable - a stalled send can no longer be unwound by closing the socket");
                return;
            }

            try { socket.Client?.Close(); }
            catch { }

            try { socket.Close(); }
            catch { }
        }

        /// <summary>Cached once: reflection on the library's private socket is the only way to
        /// unwind an abandoned StreamPoints call, and a per-call lookup on a trimmed build costs
        /// the teardown path its escape hatch.</summary>
        private static readonly FieldInfo? DacSocketField = typeof(Dac).GetField("_socket", BindingFlags.Instance | BindingFlags.NonPublic);
        private static int _socketFieldMissingLogged;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            TearDown();
        }

        private void SetStatus(string message, IStatusProvider.StatusLevel level)
        {
            // One immutable snapshot: the UI must never see the new message with the old level.
            _status = new StatusInfo(message, level);
        }

        // State (send/connection threads write, UI reads)
        private volatile StatusInfo _status = new("Disconnected", IStatusProvider.StatusLevel.Notice);
        private int _disposed;
        private volatile bool _isConnected;
        private volatile bool _isPlaying;
        private long _lastPlayingUtcTicks = DateTime.UtcNow.Ticks;
        private volatile string? _lastConnectIp;
        private volatile int _bufferFullness;
        private long _lastEnqueueTimestamp;
        private volatile bool _watchdogBlanked;

        /// <summary>True while the master is off and the DAC has been commanded to stop; the
        /// send loop holds (heartbeat only) until the master comes back.</summary>
        private bool _masterStopped;

        private LaserPoint[] _blankFrameScratch = Array.Empty<LaserPoint>();
        private LaserPoint[] _approachScratch = Array.Empty<LaserPoint>();
        private volatile bool _killedSocket;
        private volatile bool _needsDarkApproach;
        private LaserPoint[]? _sendFrame;
        private int _sendOffset;
        private LaserPoint[]? _sendFrameRaw;
    }
    #endregion

    #region PONK transport
    /// <summary>
    /// Streams laser frames as PONK-over-UDP (MadMapper's laser protocol), lifted from the former
    /// [PONKOutput] operator: 52-byte chunk headers, up to 1420 bytes of frame data per datagram,
    /// one virtual sender per stream. UDP is best-effort — frames are dropped, never queued up.
    /// </summary>
    private sealed class PonkStream : IStream
    {
        #region PONK Protocol Constants (from Common/Cpp/PonkDefs.h in the MadMapper/Ponk repo)
        private const string HeaderString = "PONK-UDP";
        private static readonly byte[] HeaderMagicBytes = Encoding.ASCII.GetBytes(HeaderString);
        private const byte ProtocolVersion = 0;
        // 8 magic + 1 version + 4 sender id + 32 name + 1 frame + 1 chunk count + 1 chunk number + 4 crc.
        private const int HeaderSize = 52;
        private const byte DataFormatXyF32RgbU8 = 1; // Mandatory minimum receivers must support
        private const int MaxChunkPayload = 1472 - HeaderSize;
        private const int MaxDatagramSize = 1472;
        private const int MaxPointsPerFrame = 32000;
        internal static readonly IPAddress DefaultMulticastAddress = IPAddress.Parse("239.255.10.24");
        private const int DefaultPort = 5583;
        private const int MaxSenderNameBytes = 32; // Null-terminated, so 31 usable chars

        /// <summary>An empty sender name resolves to the plug's name ("Laser 3"), so several TiXL
        /// PONK senders are distinguishable on receivers instead of a pile of identical "TiXL"s.</summary>
        private static string ResolveSenderName(LaserPlug plug, string? senderName)
        {
            if (!string.IsNullOrWhiteSpace(senderName))
                return senderName;

            return string.IsNullOrWhiteSpace(plug.Name) ? "TiXL" : plug.Name.Trim();
        }
        private const int SendRetryDelayMs = 50;
        private const int MaxConsecutiveSendErrors = 20;
        private const int MaxQueuedFrames = 5;
        private const int ConnectRetryDelayMs = 2000;

        /** Between fresh frames the last (or blanked) frame is re-sent at the driver's pump rate so receivers stay current - not at line rate. */
        private const int RepeatIntervalMs = 33;
        #endregion

        public PonkStream(LaserPlug plug)
        {
            _plug = plug;
            var device = plug.PonkSettings;
            _useMulticast = device.UseMulticast;
            _senderName = ResolveSenderName(plug, device.SenderName);
            var senderNameBytes = Encoding.ASCII.GetBytes(_senderName);
            Array.Copy(senderNameBytes, _senderNameBytes, Math.Min(senderNameBytes.Length, _senderNameBytes.Length));
            _maxScanSpeed = MathF.Max(0.01f, device.MaxScanSpeed);
            _maxBrightness = Math.Clamp(device.MaxBrightness, 0f, 1f);
            _swapColorChannels = device.SwapColorChannels;
            _keepalive = device.Keepalive;
            if (device.SenderId == 0)
            {
                // First run of a plug that predates persisted ids: generate once, write back so
                // every later session reuses it.
                device.SenderId = Random.Shared.Next(int.MinValue, int.MaxValue);
            }

            _senderId = device.SenderId;
            _localAddress = plug.LocalAddress ?? string.Empty;
            _targetAddress = string.IsNullOrWhiteSpace(plug.Address) ? DefaultMulticastAddress.ToString() : plug.Address;
            _port = plug.Port > 0 ? plug.Port : DefaultPort;

            StartConnection();
        }

        private readonly LaserPlug _plug;
        private readonly bool _keepalive;
        private readonly bool _useMulticast;
        private readonly string _senderName;
        private readonly float _maxScanSpeed;
        private readonly float _maxBrightness;
        private readonly bool _swapColorChannels;
        private readonly string _localAddress;
        private readonly string _targetAddress;
        private readonly int _port;

        // Sender identity persisted on the plug: receivers (MadMapper keys its Ponk inputs on
        // this int) must recognize the plug across editor restarts instead of seeing a new
        // sender per session. 0 (old plugs) = generated once and written back to the device.
        private readonly int _senderId;

        private readonly ConcurrentQueue<LaserPoint[]> _pointQueue = new();
        private CancellationTokenSource? _connectionCts;
        private Socket? _socket;
        private volatile LaserPoint[]? _lastFrame; // replaced wholesale (never mutated)
        private byte _frameNumber;

        private int _disposed;
        private volatile bool _isSending;
        private volatile StatusInfo _status = new("Disconnected", IStatusProvider.StatusLevel.Notice);

        // Fixed 32-byte header field, encoded once instead of once per chunk.
        private readonly byte[] _senderNameBytes = new byte[MaxSenderNameBytes];

        public bool IsConnected => _isSending;
        public IStatusProvider.StatusLevel StatusLevel => _status.Level;
        public string StatusMessage => _status.Message;

        public string? DescribeMismatch(LaserPlug plug)
        {
            var device = plug.PonkSettings;
            var senderName = ResolveSenderName(plug, device.SenderName);
            var target = string.IsNullOrWhiteSpace(plug.Address) ? DefaultMulticastAddress.ToString() : plug.Address;
            var port = plug.Port > 0 ? plug.Port : DefaultPort;

            if (device.UseMulticast != _useMulticast)
                return "multicast mode changed";
            if (senderName != _senderName)
                return $"sender name '{_senderName}' -> '{senderName}'";
            if (MathF.Max(0.01f, device.MaxScanSpeed) != _maxScanSpeed)
                return $"scan speed {_maxScanSpeed} -> {device.MaxScanSpeed}";
            if (Math.Clamp(device.MaxBrightness, 0f, 1f) != _maxBrightness)
                return $"brightness {_maxBrightness} -> {device.MaxBrightness}";
            if (device.SwapColorChannels != _swapColorChannels)
                return "swap channels changed";
            if (device.Keepalive != _keepalive)
                return "keepalive changed";
            if (target != _targetAddress)
                return $"target '{_targetAddress}' -> '{target}'";
            if (port != _port)
                return $"port {_port} -> {port}";
            if ((plug.LocalAddress ?? string.Empty) != _localAddress)
                return $"local address '{_localAddress}' -> '{plug.LocalAddress}'";

            return null;
        }

        public void SubmitFrame(in LaserFrame frame)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            var copy = CopyForQueue(frame, _pointQueue, MaxQueuedFrames);
            if (copy == null)
                return;

            Interlocked.Exchange(ref _lastEnqueueTimestamp, Environment.TickCount64);
            _watchdogBlanked = false;
            _pointQueue.Enqueue(copy);
        }

        private void StartConnection()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            TearDown();

            if (!IPAddress.TryParse(_targetAddress, out var targetIp))
            {
                SetStatus($"Invalid target IP '{_targetAddress}'", IStatusProvider.StatusLevel.Error);
                return;
            }

            IPAddress? localIp = null;
            if (!string.IsNullOrWhiteSpace(_localAddress) && IPAddress.TryParse(_localAddress, out var parsed))
                localIp = parsed;

            var cts = new CancellationTokenSource();
            _connectionCts = cts;
            var token = cts.Token;
            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await ConnectionLoopAsync(_useMulticast, localIp, targetIp, _port, token);
                                }
                                finally
                                {
                                    cts.Dispose();
                                }
                            });
        }

        private void TearDown()
        {
            var cts = Interlocked.Exchange(ref _connectionCts, null);
            if (cts != null)
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            var socket = Interlocked.Exchange(ref _socket, null);
            if (socket != null)
            {
                // Closing the socket off-thread protects against SO_LINGER / multicast leave hangs
                _ = Task.Run(() =>
                                 {
                                     try { socket.Close(); }
                                     catch { }
                                 });
            }

            _isSending = false;
            _pointQueue.Clear();
            _lastFrame = null;
        }

        /// <summary>
        /// Owns socket bring-up. UDP socket setup is cheap and non-blocking, but creating a
        /// socket on a chosen interface requires a bind - if the bind fails (port in use, bad
        /// interface) we retry with backoff. Once the socket is up, the send loop runs until
        /// cancelled.
        /// </summary>
        private async Task ConnectionLoopAsync(bool useMulticast, IPAddress? localIp, IPAddress targetIp,
                                               int port, CancellationToken token)
        {
            var attempt = 0;
            while (!token.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
            {
                attempt++;
                Socket? socket = null;
                try
                {
                    SetStatus(attempt == 1
                                  ? $"Opening UDP socket to {targetIp}:{port}..."
                                  : $"Reopening UDP socket to {targetIp}:{port} (attempt {attempt})...",
                              IStatusProvider.StatusLevel.Notice);

                    socket = CreateSendSocket(localIp, useMulticast, targetIp);
                    if (token.IsCancellationRequested)
                    {
                        CloseQuietly(socket);
                        return;
                    }

                    _socket = socket;
                    var sendEndPoint = new IPEndPoint(targetIp, port);
                    _isSending = true;
                    _consecutiveErrors = 0;
                    SetStatus($"Sending to {targetIp}:{port}", IStatusProvider.StatusLevel.Success);

                    await SendLoopAsync(socket, sendEndPoint, token);
                    return;
                }
                catch (OperationCanceledException)
                {
                    CloseQuietly(socket);
                    return;
                }
                catch (Exception e)
                {
                    CloseQuietly(socket);
                    _isSending = false;

                    if (token.IsCancellationRequested)
                        return;

                    SetStatus($"Socket error: {e.Message} - retrying...", IStatusProvider.StatusLevel.Warning);
                    if (attempt == 1)
                        Log.Warning($"PONK stream: socket setup failed - {e.Message}");
                }

                try { await Task.Delay(ConnectRetryDelayMs, token); }
                catch (OperationCanceledException) { return; }
            }
        }

        private static Socket CreateSendSocket(IPAddress? localIp, bool useMulticast, IPAddress targetIp)
        {
            // Using a per-sender ephemeral port keeps multiple TiXL instances (or the stream
            // running alongside MadMapper) from colliding.
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                             {
                                 Blocking = false,
                                 EnableBroadcast = true,
                             };

            // Allow address reuse so receivers running on the same machine aren't blocked
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // Bump send buffer - laser frames can be large bursts
            try { socket.SendBufferSize = 256 * 1024; }
            catch { }

            // Bind to the chosen local interface (or Any) on an ephemeral port. Bind is required
            // so multicast packets leave on the right interface. A pinned interface that can't
            // bind (NIC unplugged, IP changed under us) falls back to OS routing instead of
            // killing the stream - the retry loop keeps the pinned address for when it returns.
            var bindAddr = localIp ?? IPAddress.Any;
            try
            {
                socket.Bind(new IPEndPoint(bindAddr, 0));
            }
            catch (SocketException)
            {
                if (bindAddr.Equals(IPAddress.Any))
                    throw;

                bindAddr = IPAddress.Any;
                socket.Bind(new IPEndPoint(bindAddr, 0));
            }

            if (useMulticast)
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                                           bindAddr.GetAddressBytes());
                }
                catch { /* IPAny may reject; multicast group routing is OS-defined */ }

                // Default multicast TTL is 1; bump so it crosses subnets like MadMapper does
                try { socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 32); }
                catch { }

                // Some NICs complain if we don't join the group we send to, so opt-in (best effort)
                try
                {
                    var mreq = new MulticastOption(targetIp, bindAddr);
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, mreq);
                }
                catch { /* not all platforms support AddMembership on a send socket */ }
            }

            return socket;
        }

        private static void CloseQuietly(Socket? s)
        {
            if (s == null)
                return;
            try { s.Close(); }
            catch { }
        }

        private async Task SendLoopAsync(Socket socket, IPEndPoint target, CancellationToken token)
        {
            // Buffers are owned by this connection attempt. TearDown does not join the old send
            // loop before a new connection starts, so a shared instance field would let two
            // overlapping loops interleave their encodes into one datagram.
            var headerBuffer = new byte[MaxDatagramSize];
            var encodeBuffer = new byte[MaxDatagramSize];
            var lastSendTicks = 0L;

            while (!token.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
            {
                // Master off: settled blanking. The blanked copy of the last frame must keep
                // being SENT - receivers render whatever arrived last, so going silent leaves
                // the last lit frame up until their own timeout blanks it. With the plug's
                // keepalive on, content streams unblanked even while the master is off - a
                // deliberate preview override (PONK receivers can drive real scanners, so the
                // default is off). The pick below ignores queued frames while blanked.
                var masterOff = !LaserMasterEnable.Enabled && !_keepalive;
                var overrideLive = !LaserMasterEnable.Enabled && _keepalive;
                if (overrideLive != _keepaliveOverrideReported)
                {
                    // The override streams live content past the emergency master - it must never be silent.
                    _keepaliveOverrideReported = overrideLive;
                    if (overrideLive)
                    {
                        Log.Warning($"PONK stream '{_senderName}': LASER master is off but Keepalive is on - content keeps streaming");
                        SetStatus("Master off - Keepalive override is streaming LIVE content", IStatusProvider.StatusLevel.Warning);
                    }
                }

                var lastFrame = _lastFrame;
                if (masterOff)
                {
                    _pointQueue.Clear();
                    if (lastFrame is { Length: > 0 } && !_masterBlankApplied)
                    {
                        lastFrame = BlankFrameCopy(lastFrame);
                        _lastFrame = lastFrame;
                        _lastFrameBlanked = true;
                        _masterBlankApplied = true;
                    }
                }
                else
                {
                    _masterBlankApplied = false;
                }

                LaserPoint[]? frame = null;
                var frameIsBlanked = false;
                if (!masterOff && _pointQueue.TryDequeue(out var points) && points.Length > 0)
                {
                    frame = points;
                }
                else if (lastFrame is { Length: > 0 } && Environment.TickCount64 - lastSendTicks >= RepeatIntervalMs)
                {
                    if (masterOff)
                    {
                        frame = lastFrame;
                    }
                    else
                    {
                        // Frame watchdog: a stalled driver must not leave the last lit frame frozen
                        // on the receiver. Once the grace period expires, keep looping the geometry
                        // but with the beam blanked.
                        var watchdogBlank = WatchdogElapsed(ref _lastEnqueueTimestamp);
                        if (watchdogBlank && !_watchdogBlanked)
                        {
                            _watchdogBlanked = true;
                            Log.Warning("PONK stream: frame watchdog - no new frames, blanking output");
                            SetStatus("Frame watchdog - no new frames, output blanked", IStatusProvider.StatusLevel.Warning);
                        }

                        frame = watchdogBlank && !_lastFrameBlanked ? BlankFrameCopy(lastFrame) : lastFrame;
                        frameIsBlanked = watchdogBlank || _lastFrameBlanked;
                    }
                }

                if (frame == null)
                {
                    try { await Task.Delay(1, token); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }

                var frameLength = EncodeFrame(frame, ref encodeBuffer);
                var crc = ComputeChecksum(encodeBuffer, frameLength);
                var chunkCount = ComputeChunkCount(frameLength);

                try
                {
                    SendFrame(socket, target, headerBuffer, encodeBuffer, frameLength, crc, chunkCount, token);
                    lastSendTicks = Environment.TickCount64;
                    _consecutiveErrors = 0;
                    _lastFrame = frame;
                    _lastFrameBlanked = frameIsBlanked;

                    // A good frame after a warning means the show is back - clear it, but never
                    // while the watchdog is still blanking (its Warning persists until fresh
                    // frames arrive) or while the keepalive override session is active (its
                    // Warning is the documented visibility of the master bypass).
                    if (!_watchdogBlanked && !_keepaliveOverrideReported
                                          && _status.Level != IStatusProvider.StatusLevel.Success)
                        SetStatus("Streaming", IStatusProvider.StatusLevel.Success);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    if (token.IsCancellationRequested)
                        return;

                    _consecutiveErrors++;
                    if (_consecutiveErrors >= MaxConsecutiveSendErrors)
                    {
                        // Surface the failure to the outer loop so it can rebuild the socket
                        SetStatus($"Send error: {e.Message} - reconnecting...", IStatusProvider.StatusLevel.Warning);
                        throw;
                    }

                    SetStatus($"Send error: {e.Message}", IStatusProvider.StatusLevel.Warning);
                    try { await Task.Delay(SendRetryDelayMs, token); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }

        private void SendFrame(Socket socket, IPEndPoint target, byte[] headerBuffer, byte[] frameData, int frameLength, uint crc,
                               int chunkCount, CancellationToken token)
        {
            // PONK chunks are header-prefixed; each chunk's payload is a slice of the frame data.
            // All chunks of a frame carry the same CRC so receivers can drop incomplete frames.
            // The frame number (mod 256) advances even for a frame dropped half-way, so its
            // leftover chunks can never be merged with the next frame's.
            try
            {
                SendFrameChunks(socket, target, headerBuffer, frameData, frameLength, crc, chunkCount, token);
            }
            finally
            {
                unchecked { _frameNumber++; }
            }
        }

        private void SendFrameChunks(Socket socket, IPEndPoint target, byte[] headerBuffer, byte[] frameData, int frameLength, uint crc,
                                     int chunkCount, CancellationToken token)
        {
            var written = 0;
            for (var chunkNumber = 0; chunkNumber < chunkCount; chunkNumber++)
            {
                token.ThrowIfCancellationRequested();

                var payloadSize = Math.Min(MaxChunkPayload, frameLength - written);
                var totalSize = HeaderSize + payloadSize;

                WriteHeader(headerBuffer, _senderNameBytes, _senderId, _frameNumber, (byte)chunkCount, (byte)chunkNumber, crc);
                Array.Copy(frameData, written, headerBuffer, HeaderSize, payloadSize);

                var sent = 0;
                var deadline = Environment.TickCount64 + 10;
                while (sent < totalSize)
                {
                    try
                    {
                        sent += socket.SendTo(headerBuffer, sent, totalSize - sent, SocketFlags.None, target);
                    }
                    catch (SocketException se) when (se.SocketErrorCode == SocketError.WouldBlock
                                                     || se.SocketErrorCode == SocketError.TryAgain
                                                     || se.SocketErrorCode == SocketError.IOPending
                                                     || se.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
                    {
                        if (token.IsCancellationRequested)
                            throw new OperationCanceledException(token);

                        // UDP is best-effort: a saturated send buffer means this frame is
                        // dropped, not that the send loop should stall (and stop applying the
                        // master-off/watchdog transitions). The receiver's CRC drops a partial
                        // frame too.
                        if (Environment.TickCount64 >= deadline)
                            return;

                        Thread.Sleep(1); // brief yield; the next SendTo resumes
                    }
                }

                written += payloadSize;
            }
        }

        /// <summary>Encodes the frame into <paramref name="buffer"/>, growing it if needed, and returns the encoded length.</summary>
        private int EncodeFrame(LaserPoint[] points, ref byte[] buffer)
        {
            // pointCount is a ushort and chunkCount a byte, so cap the frame at a point count
            // whose chunking stays inside both limits.
            var count = Math.Min(points.Length, MaxPointsPerFrame);
            var length = PayloadHeaderSize + count * BytesPerPoint;
            if (buffer.Length < length)
                buffer = new byte[length];

            var data = buffer;

            // One path per frame: PATHNUMB identifies the shape across frames so receivers can
            // dispatch; MAXSPEED tells the receiver the requested scan-speed multiplier.
            data[0] = DataFormatXyF32RgbU8;
            data[1] = 2;
            WriteMetaEntry(data.AsSpan(2, MetaEntrySize), "PATHNUMB", 1);
            WriteMetaEntry(data.AsSpan(2 + MetaEntrySize, MetaEntrySize), "MAXSPEED", _maxScanSpeed);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2 + 2 * MetaEntrySize, 2), (ushort)count);

            var offset = PayloadHeaderSize;
            for (var i = 0; i < count; i++)
            {
                var p = points[i];
                if (p.I <= 0)
                    p = LaserPoint.CreateBlanked(p.X, p.Y); // PONK has no intensity channel: I = 0 must mean no colour

                // LaserPoint.X/Y are ILDA-style 16-bit signed (-32768..32767); normalize to
                // PONK's mandatory float32 [-1, 1] XY + uint8 RGB format. Colors are scaled by
                // the brightness ceiling right before encoding - the final output audit.
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset, 4), Math.Clamp(p.X / 32767f, -1f, 1f));
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset + 4, 4), Math.Clamp(p.Y / 32767f, -1f, 1f));
                data[offset + 8] = (byte)Math.Clamp((int)MathF.Round(Math.Max(_swapColorChannels ? p.B : p.R, 0) * _maxBrightness) >> 8, 0, 255);
                data[offset + 9] = (byte)Math.Clamp((int)MathF.Round(Math.Max(p.G, 0) * _maxBrightness) >> 8, 0, 255);
                data[offset + 10] = (byte)Math.Clamp((int)MathF.Round(Math.Max(_swapColorChannels ? p.R : p.B, 0) * _maxBrightness) >> 8, 0, 255);
                offset += BytesPerPoint;
            }

            return length;
        }

        /** Data format, path-metadata count, two metadata entries and the point count. */
        private const int PayloadHeaderSize = 2 + 2 * MetaEntrySize + 2;

        /** float32 X, float32 Y, uint8 R, G, B. */
        private const int BytesPerPoint = 11;

        private static void WriteHeader(byte[] buffer, byte[] senderNameBytes, int senderId, byte frameNumber, byte chunkCount,
                                        byte chunkNumber, uint crc)
        {
            // All multi-byte fields are little-endian per the PONK reference implementation:
            //  0-7 "PONK-UDP", 8 version, 9-12 sender id LE, 13-44 name (32B null-padded ASCII),
            //  45 frameNumber, 46 chunkCount, 47 chunkNumber, 48-51 dataCrc LE.
            for (var i = 0; i < 8; i++)
                buffer[i] = i < HeaderMagicBytes.Length ? HeaderMagicBytes[i] : (byte)' ';

            buffer[8] = ProtocolVersion;

            buffer[9] = (byte)(senderId & 0xFF);
            buffer[10] = (byte)((senderId >> 8) & 0xFF);
            buffer[11] = (byte)((senderId >> 16) & 0xFF);
            buffer[12] = (byte)((senderId >> 24) & 0xFF);

            for (var i = 0; i < MaxSenderNameBytes; i++)
                buffer[13 + i] = senderNameBytes[i];

            buffer[45] = frameNumber;
            buffer[46] = chunkCount;
            buffer[47] = chunkNumber;

            buffer[48] = (byte)(crc & 0xFF);
            buffer[49] = (byte)((crc >> 8) & 0xFF);
            buffer[50] = (byte)((crc >> 16) & 0xFF);
            buffer[51] = (byte)((crc >> 24) & 0xFF);
        }

        private static int ComputeChunkCount(int frameDataLength)
        {
            if (frameDataLength <= 0)
                return 0;
            // ceil(frameData / MaxChunkPayload) - matches the C++ sample sender
            return (frameDataLength + MaxChunkPayload - 1) / MaxChunkPayload;
        }

        /// <summary>
        /// PONK's "CRC" is a sum of all frame-data bytes (mod 2^32). It's not a polynomial CRC;
        /// the field name is kept for protocol compatibility. Same value in every chunk of a frame.
        /// </summary>
        private static uint ComputeChecksum(byte[] data, int length)
        {
            uint sum = 0;
            for (var i = 0; i < length; i++)
                sum += data[i];
            return sum;
        }

        /// <summary>Returns a fresh blanked copy (positions kept, colors zeroed) — the watchdog
        /// loop aliases the last frame, so a shared scratch buffer would tear it.</summary>
        private static LaserPoint[] BlankFrameCopy(LaserPoint[] source)
        {
            var copy = new LaserPoint[source.Length];
            LaserSafetyTransforms.BlankCopyInto(source, copy);
            return copy;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            TearDown();
        }

        private void SetStatus(string message, IStatusProvider.StatusLevel level)
        {
            _status = new StatusInfo(message, level);
        }

        private bool _keepaliveOverrideReported;
        private int _consecutiveErrors;
        private long _lastEnqueueTimestamp;
        private volatile bool _watchdogBlanked;
        private bool _masterBlankApplied;

        /** The remembered frame already has its colors zeroed, so repeats need no new blanked copy. */
        private bool _lastFrameBlanked;
    }
    #endregion

    #region ShowNet transport
    /// <summary>
    /// Streams to a showNET laser over UDP with the protocol's request/response handshake. The
    /// session: direct sends to ip:41855 from an ephemeral local port — handshake (command 0x04,
    /// full obfuscation, reply awaited), configure (0x12, reply ignored), then frames (0x1A) and
    /// NOPs. UDP is best-effort: frames are dropped, never queued up; the watchdog plus blanked
    /// repeats keep the beam honest while the device loops its frame memory.
    /// </summary>
    private sealed class ShowNetStream : IStream
    {
        public ShowNetStream(LaserPlug plug)
        {
            _plug = plug;
            var device = plug.ShowNetSettings;
            _scanRate = NormalizeScanRate(device.ScanRate);
            _plugAddress = plug.Address ?? string.Empty;
            _localAddress = plug.LocalAddress ?? string.Empty;
            _maxBrightness = Math.Clamp(device.MaxBrightness, 0f, 1f);
            _swapColorChannels = device.SwapColorChannels;

            // The plug picker lists lasers while a stream runs — start the shared probe listener
            // together with the stream.
            LaserPlugDiscovery.EnsureShowNetDiscovery();

            StartConnection();
        }

        private readonly LaserPlug _plug;
        private readonly string _plugAddress;
        private readonly string _localAddress;

        // Read per frame by the send loop, so a settings edit applies without a reconnect.
        private volatile int _scanRate;
        private volatile float _maxBrightness;
        private volatile bool _swapColorChannels;

        private Socket? _socket;
        private CancellationTokenSource? _connectionCts;
        private readonly ConcurrentQueue<LaserPoint[]> _pointQueue = new();
        private volatile LaserPoint[]? _lastFrame;
        private readonly ShowNetSessionInfo _session = new();
        private byte[] _bitstream = Array.Empty<byte>();

        // Per-session counters, the reference's semantics.
        private int _sequenceId = 1000;
        private int _frameId;
        private int _packetId;

        private int _disposed;
        private volatile bool _isConnected;
        private long _lastEnqueueTimestamp;
        private volatile bool _watchdogBlanked;
        private volatile string? _lastConnectIp;
        private volatile string? _autoSelectedIp;
        private bool _frameOverBudget;

        private volatile StatusInfo _status = new("Disconnected", IStatusProvider.StatusLevel.Notice);

        private const int HandshakeTimeoutMs = 700;
        private const int HandshakeAttempts = 5;
        private const int ConnectRetryDelayMs = 2000;
        private const int MaxConsecutiveSendErrors = 20;
        private const int MaxQueuedFrames = 2;
        private const int RepeatIntervalMs = 33;
        private const int NopIntervalMs = 500;
        private const int DiscoveryPollDelayMs = 500;
        private const int FrameMemorySize = 0x8000;

        public bool IsConnected => _isConnected;
        public IStatusProvider.StatusLevel StatusLevel => _status.Level;
        public string StatusMessage => _status.Message;

        /// <summary>Only the endpoint (device address or the local interface bind) needs a new
        /// session; scan rate, brightness ceiling and channel order are read per frame.</summary>
        public void ApplyLiveSettings(LaserPlug plug)
        {
            var device = plug.ShowNetSettings;
            _scanRate = NormalizeScanRate(device.ScanRate);
            _maxBrightness = Math.Clamp(device.MaxBrightness, 0f, 1f);
            _swapColorChannels = device.SwapColorChannels;
        }

        public string? DescribeMismatch(LaserPlug plug)
        {
            var address = plug.Address ?? string.Empty;
            if (address != _plugAddress && !IsAutoSelectedDevice(address))
                return $"address '{_plugAddress}' -> '{plug.Address}'";
            if ((plug.LocalAddress ?? string.Empty) != _localAddress)
                return $"local address '{_localAddress}' -> '{plug.LocalAddress}'";
            return null;
        }

        public void SubmitFrame(in LaserFrame frame)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            var copy = CopyForQueue(frame, _pointQueue, MaxQueuedFrames);
            if (copy == null)
                return;

            Interlocked.Exchange(ref _lastEnqueueTimestamp, Environment.TickCount64);
            _watchdogBlanked = false;
            _pointQueue.Enqueue(copy);
        }

        private void StartConnection()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            TearDown();

            var cts = new CancellationTokenSource();
            _connectionCts = cts;
            var token = cts.Token;
            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await ConnectionLoopAsync(token);
                                }
                                finally
                                {
                                    cts.Dispose();
                                }
                            });
        }

        private void TearDown()
        {
            var cts = Interlocked.Exchange(ref _connectionCts, null);
            if (cts != null)
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            var socket = Interlocked.Exchange(ref _socket, null);
            if (socket != null)
            {
                // Closing off-thread: a close can block while an abandoned send unwinds.
                _ = Task.Run(() =>
                                 {
                                     try { socket.Close(); }
                                     catch { }
                                 });
            }

            // The connected-device slot is shared across protocols; a stale showNET entry would
            // otherwise show up in the other pickers (and suppress this one's "probing..." hint).
            var ip = _lastConnectIp;
            if (ip != null)
                LaserPlugDiscovery.ClearConnectedDevice(ip);

            _isConnected = false;
            _pointQueue.Clear();
            _lastFrame = null;
        }

        /// <summary>
        /// Owns socket bring-up: wait for an address (typed or the first discovered laser), bind,
        /// handshake, configure, stream. Retries with a fixed 2 s delay — the handshake's own
        /// timeout ladder covers a device that is still booting.
        /// </summary>
        private async Task ConnectionLoopAsync(CancellationToken token)
        {
            var attempt = 0;
            while (!token.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
            {
                attempt++;
                Socket? socket = null;
                var address = string.Empty;
                try
                {
                    address = ResolveAddress();
                    while (string.IsNullOrWhiteSpace(address))
                    {
                        SetStatus("No showNET address (discovering...)", IStatusProvider.StatusLevel.Notice);
                        await Task.Delay(DiscoveryPollDelayMs, token);
                        address = ResolveAddress();
                    }

                    socket = CreateSocket(_localAddress);
                    if (token.IsCancellationRequested)
                        return;

                    _socket = socket;
                    var remote = new IPEndPoint(IPAddress.Parse(address), ShowNetProtocol.LaserPort);
                    SetStatus($"Handshaking with {address}...", IStatusProvider.StatusLevel.Notice);
                    Handshake(socket, remote);
                    SendConfigure(socket, remote);
                    LaserPlugDiscovery.SetConnectedDevice("showNET", address);
                    _lastConnectIp = address;
                    _isConnected = true;
                    SetStatus($"Connected to {address}", IStatusProvider.StatusLevel.Success);
                    await SendLoopAsync(socket, remote, token);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e) when (!token.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
                {
                    CloseQuietly(socket);
                    _isConnected = false;
                    if (attempt == 1)
                        Log.Warning($"showNET stream: connection to {address} failed - {e.Message}");
                }

                try
                {
                    await Task.Delay(ConnectRetryDelayMs, token);
                }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>The plug's address, or the first discovered laser when empty (zero-touch).</summary>
        private string ResolveAddress()
        {
            if (!string.IsNullOrWhiteSpace(_plug.Address))
                return _plug.Address;

            try
            {
                var lasers = LaserPlugDiscovery.ShowNetLasers;
                if (_autoSelectedIp == null
                    || lasers.All(laser => !string.Equals(laser.Ip, _autoSelectedIp, StringComparison.OrdinalIgnoreCase)))
                {
                    var first = lasers.FirstOrDefault();
                    var picked = first.Ip;
                    if (!string.IsNullOrWhiteSpace(picked) && picked != _autoSelectedIp)
                    {
                        _autoSelectedIp = picked;
                        Log.Debug($"showNET stream: auto-selected laser at {_autoSelectedIp}");
                    }
                }

                return _autoSelectedIp ?? string.Empty;
            }
            catch
            {
                // The discovery table mutates concurrently - a transient race just skips this poll.
                return _autoSelectedIp ?? string.Empty;
            }
        }

        /// <summary>A plug left empty streams to the first discovered laser; the plug card then
        /// writes that laser's address into the plug, which is the connection the stream already has.</summary>
        private bool IsAutoSelectedDevice(string address)
        {
            return _plugAddress.Length == 0
                   && address.Length > 0
                   && (address == _autoSelectedIp || address == _lastConnectIp);
        }

        /// <summary>
        /// A UDP socket on an ephemeral local port, bound to the plug's interface when one is
        /// pinned (falling back to any interface when the pin cannot bind — the NIC may be away;
        /// the retry loop keeps the pin for when it returns).
        /// </summary>
        private static Socket CreateSocket(string localAddress)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try { socket.SendBufferSize = 256 * 1024; }
            catch { /* default buffer size is fine */ }

            var bindAddress = IPAddress.Any;
            if (IPAddress.TryParse(localAddress, out var pinned) && !pinned.Equals(IPAddress.Any))
                bindAddress = pinned;

            try
            {
                socket.Bind(new IPEndPoint(bindAddress, 0));
            }
            catch (SocketException)
            {
                if (bindAddress.Equals(IPAddress.Any))
                    throw;

                bindAddress = IPAddress.Any;
                socket.Bind(new IPEndPoint(bindAddress, 0));
            }

            return socket;
        }

        private static void CloseQuietly(Socket? socket)
        {
            if (socket == null)
                return;

            try { socket.Close(); }
            catch { /* already dead */ }
        }

        /// <summary>
        /// The showNET session opener: send the handshake (command 0x04, keys marker + full
        /// obfuscation, scrambled+encrypted reply) up to HandshakeAttempts times, decode and parse
        /// the reply. Throws when the device never answers — the connection loop's catch reports.
        /// </summary>
        private void Handshake(Socket socket, IPEndPoint remote)
        {
            Span<byte> keyBytes = stackalloc byte[4];
            Random.Shared.NextBytes(keyBytes);
            var clientKey = BinaryPrimitives.ReadUInt32LittleEndian(keyBytes);

            // A fresh packet per attempt: PrepareSend obfuscates in place (the cipher is not an
            // involution), so a reused buffer would re-obfuscate ciphertext and every retry
            // after the first would be garbage to the device.
            for (var attempt = 1; attempt <= HandshakeAttempts; attempt++)
            {
                var packet = ShowNetProtocol.BuildHandshake(clientKey);
                var seq = NextSequenceId();
                ShowNetProtocol.PrepareSend(packet, packet.Length,
                                            ShowNetSendFlags.Keys
                                            | ShowNetSendFlags.ScrambleHeader
                                            | ShowNetSendFlags.CryptHeader
                                            | ShowNetSendFlags.ScramblePayload
                                            | ShowNetSendFlags.CryptPayload
                                            | ShowNetSendFlags.ScrambleResponse
                                            | ShowNetSendFlags.CryptResponse,
                                            seq);
                socket.SendTo(packet, remote);
                if (TryReceiveResponse(socket, seq, scrambled: true, crypted: true, out var rx, out var rxLength))
                {
                    if (!ShowNetProtocol.TryParseHandshakeResponse(rx, rxLength, out var parsed))
                        throw new IOException($"showNET device at {remote.Address} answered with an unexpected handshake reply ({rxLength} bytes)");

                    _session.Generation = parsed.Generation;
                    _session.ColorFeatures = parsed.ColorFeatures;
                    _session.ColorCount = parsed.ColorCount;
                    _session.FramePointSize = parsed.FramePointSize;
                    _session.TimeInterval = parsed.TimeInterval;
                    _session.Revision = parsed.Revision;
                    _session.Firmware = parsed.Firmware;
                    _session.MemoryAddress = parsed.MemoryAddress;
                    for (var i = 0; i < 3; i++)
                        _session.InterfaceId[i] = parsed.InterfaceId[i];
                    return;
                }
            }

            throw new SocketException((int)SocketError.TimedOut);
        }

        /// <summary>Session-local sequence counter — the reference's lock-guarded increment.</summary>
        private int NextSequenceId()
        {
            var seq = _sequenceId;
            _sequenceId = (_sequenceId + 1) & 0x7FFFFFFF;
            return seq;
        }

        /// <summary>
        /// Waits for the reply carrying the expected sequence id, decoding it in place per the
        /// request's response cipher. Stale replies (a reconnect's leftovers) cannot match the
        /// fresh sequence id and are discarded until the deadline.
        /// </summary>
        private static bool TryReceiveResponse(Socket socket, int seq, bool scrambled, bool crypted, out byte[] rx, out int rxLength)
        {
            var buffer = new byte[1600];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            var deadline = Environment.TickCount64 + HandshakeTimeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                socket.ReceiveTimeout = (int)Math.Max(1, deadline - Environment.TickCount64);
                try
                {
                    var length = socket.ReceiveFrom(buffer, ref remote);
                    if (length >= 24 && ShowNetProtocol.ResponseSeq(buffer.AsSpan(0, length)) == seq)
                    {
                        ShowNetProtocol.DecodeResponse(buffer, length, scrambled, crypted);
                        rx = buffer;
                        rxLength = length;
                        return true;
                    }

                    // A stale reply for an older sequence id - keep waiting within the deadline.
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
                {
                    break;
                }
            }

            rx = null;
            rxLength = 0;
            return false;
        }

        /// <summary>Sends the configure command (0x12) that sets up the device's frame memory
        /// layout; the reply is ignored, matching the reference client.</summary>
        private void SendConfigure(Socket socket, IPEndPoint remote)
        {
            var packet = ShowNetProtocol.BuildConfigure(_session.Generation2, FrameMemorySize, _session.MemoryAddress);
            ShowNetProtocol.PrepareSend(packet, packet.Length,
                                        ShowNetSendFlags.ScrambleHeader
                                        | ShowNetSendFlags.CryptHeader
                                        | ShowNetSendFlags.ScramblePayload
                                        | ShowNetSendFlags.CryptPayload
                                        | ShowNetSendFlags.ScrambleResponse
                                        | ShowNetSendFlags.CryptResponse
                                        | ShowNetSendFlags.IgnoreResponse,
                                        NextSequenceId());
            socket.SendTo(packet, remote);
        }

        /// <summary>
        /// Streams queued frames and their blanked/repeated fallbacks; keeps the session alive
        /// with NOPs while nothing flows. UDP is best-effort — the device loops its frame memory,
        /// so this loop's only state is what to encode next.
        /// </summary>
        private async Task SendLoopAsync(Socket socket, IPEndPoint remote, CancellationToken token)
        {
            var consecutiveErrors = 0;
            var lastRepeatTicks = 0L;
            var lastNopTicks = 0L;
            var exitX = 0;
            var exitY = 0;
            var blankScratch = Array.Empty<LaserPoint>();
            var approachScratch = Array.Empty<LaserPoint>();
            var masterOffReported = false;

            while (!token.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
            {
                var now = Environment.TickCount64;
                var masterOff = !LaserMasterEnable.Enabled;
                if (masterOff != masterOffReported)
                {
                    masterOffReported = masterOff;
                    if (masterOff)
                    {
                        SetStatus("Laser master off - output blanked, session kept warm", IStatusProvider.StatusLevel.Notice);
                        Log.Debug("showNET stream: master off - output blanked, session kept warm");
                    }
                }

                if (masterOff)
                {
                    // The master is the hard kill for a real-scanner protocol: drop everything
                    // queued and keep the session alive with blanked repeats below. Nothing lit
                    // can leave this stream while it is off.
                    _pointQueue.Clear();
                }

                LaserPoint[]? frame = null;
                if (!masterOff && _pointQueue.TryDequeue(out var queued) && queued.Length > 0)
                {
                    frame = queued;
                    lastRepeatTicks = 0;
                }
                else if (_lastFrame is { Length: > 0 } && now - lastRepeatTicks >= RepeatIntervalMs)
                {
                    var watchdogBlank = WatchdogElapsed(ref _lastEnqueueTimestamp);
                    if (watchdogBlank && !_watchdogBlanked)
                    {
                        _watchdogBlanked = true;
                        Log.Warning("showNET stream: frame watchdog - no new frames, blanking output");
                        SetStatus("Frame watchdog - no new frames, output blanked", IStatusProvider.StatusLevel.Warning);
                    }

                    frame = masterOff || watchdogBlank
                                ? BlankFrame(_lastFrame, ref blankScratch)
                                : _lastFrame;
                    lastRepeatTicks = now;
                }

                if (frame == null)
                {
                    // Idle: NOP keepalive while nothing flows.
                    if (now - lastNopTicks >= NopIntervalMs)
                    {
                        SendNop(socket, remote);
                        lastNopTicks = now;
                    }

                    try { await Task.Delay(2, token); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }

                // Galvo safety: every datagram replaces the device's memory image, so every
                // datagram leads with the dark ramp from the scanners' last position.
                var rawFrame = frame;
                frame = PrependDarkApproach(frame, exitX, exitY, ref approachScratch);

                try
                {
                    SendFrameDatagrams(socket, remote, frame);
                    consecutiveErrors = 0;
                    exitX = rawFrame[^1].X;
                    exitY = rawFrame[^1].Y;
                    if (rawFrame != blankScratch)
                        _lastFrame = rawFrame;
                    if (_status.Level != IStatusProvider.StatusLevel.Success && !_watchdogBlanked)
                        SetStatus("Streaming", IStatusProvider.StatusLevel.Success);
                }
                catch (Exception e) when (!token.IsCancellationRequested)
                {
                    consecutiveErrors++;
                    if (consecutiveErrors >= MaxConsecutiveSendErrors)
                        throw;

                    SetStatus($"Connection problems - retrying ({consecutiveErrors}/{MaxConsecutiveSendErrors})...",
                              IStatusProvider.StatusLevel.Warning);
                    await Task.Delay(50, token);
                }

                try { await Task.Delay(2, token); }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>The session keepalive while nothing else flows (command 0x1A without a frame).</summary>
        private void SendNop(Socket socket, IPEndPoint remote)
        {
            var packet = ShowNetProtocol.BuildNop(_packetId);
            ShowNetProtocol.PrepareSend(packet, packet.Length,
                                        ShowNetSendFlags.ScrambleHeader | ShowNetSendFlags.CryptHeader
                                        | ShowNetSendFlags.ScramblePayload | ShowNetSendFlags.ScrambleResponse
                                        | ShowNetSendFlags.IgnoreResponse,
                                        NextSequenceId());
            socket.SendTo(packet, remote);
        }

        /// <summary>Encodes the frame and sends it as fragment datagrams; UDP is fire-and-forget.</summary>
        private void SendFrameDatagrams(Socket socket, IPEndPoint remote, LaserPoint[] frame)
        {
            if (!ShowNetFrameEncoder.TryEncode(frame, frame.Length, in _session, _maxBrightness, _swapColorChannels,
                                               ref _bitstream, out var length, out var compressed))
            {
                // Over the 32-fragment wire budget. Dropping is the only safe move, but a
                // silently blank laser reads as a dead device - report once per incident.
                if (!_frameOverBudget)
                {
                    _frameOverBudget = true;
                    Log.Warning($"showNET stream: frame with {frame.Length} points exceeds the wire budget ({ShowNetProtocol.MaxFrameBytes} bytes) - dropped. Reduce the point count.");
                    SetStatus("Frame too large for showNET - output blanked, reduce point count", IStatusProvider.StatusLevel.Warning);
                }

                return;
            }

            // The reference's nextFrameId(): mask-increment then post-increment, so ids advance
            // 1, 3, 5, ... exactly as the vendor client produces them (replicated deliberately).
            _frameId = (_frameId + 1) & 0x7FFFFF;
            var frameId = _frameId++;
            _packetId = (_packetId + 1) & 0x7FF;
            var ticksPerPoint = Math.Clamp(_session.TimeInterval / _scanRate, 1, 65535);
            var datagrams = ShowNetProtocol.BuildFrame(_packetId, frameId, compressed, ticksPerPoint,
                                                       frame.Length, _bitstream, length);
            foreach (var datagram in datagrams)
            {
                ShowNetProtocol.PrepareSend(datagram, datagram.Length,
                                            ShowNetSendFlags.ScrambleHeader | ShowNetSendFlags.CryptHeader
                                            | ShowNetSendFlags.ScramblePayload | ShowNetSendFlags.ScrambleResponse
                                            | ShowNetSendFlags.IgnoreResponse,
                                            NextSequenceId());
                socket.SendTo(datagram, remote);
            }

            _frameOverBudget = false;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            TearDown();
        }

        private void SetStatus(string message, IStatusProvider.StatusLevel level)
        {
            // One immutable snapshot: the UI must never see the new message with the old level.
            _status = new StatusInfo(message, level);
        }
    }

    #endregion

    #region CITP stream - one feed of the machine's shared CITP source

    /// <summary>
    /// The device side of one laser plug on the shared CITP source (<see cref="CitpLaserHub"/>):
    /// registers the plug's output as a feed - "Physical Output" N of the one "TiXL" source - and
    /// feeds it queued frames. The hub owns the session, the discovery and the wire format (one
    /// source, N feeds, frames tagged with the feed index - the MadMapper shape of the protocol,
    /// which is what lets a receiver bind every feed separately); this adapter mirrors the plug's
    /// settings onto its feed and reports the session state.
    /// </summary>
    private sealed class CitpStream : IStream
    {
        public CitpStream(LaserPlug plug)
        {
            var device = plug.CitpSettings;
            _feedName = device.ResolveFeedName(plug.Name);
            _directHost = plug.Address ?? string.Empty;
            _directPort = plug.Port > 0 ? plug.Port : 0;
            _localAddress = plug.LocalAddress ?? string.Empty;

            _hub = CitpLaserHub.GetOrCreate(_directHost, _directPort, _localAddress);
            _feed = _hub.RegisterFeed(_feedName);
            ApplyLiveSettings(plug);
        }

        private readonly CitpLaserHub _hub;
        private readonly CitpLaserFeed _feed;
        private readonly string _feedName;
        private readonly string _directHost;
        private readonly int _directPort;
        private readonly string _localAddress;

        private int _disposed;

        public bool IsConnected => _hub.IsConnected;
        public IStatusProvider.StatusLevel StatusLevel => _hub.StatusLevel;

        public string StatusMessage
        {
            get
            {
                var session = _hub.StatusMessage;
                return _feed.Active
                           ? $"{session} - feed {_feed.Index + 1} '{_feed.Name}' at {_feed.RequiredFps} fps"
                           : $"{session} - feed {_feed.Index + 1} '{_feed.Name}' idle";
            }
        }

        public bool MatchesPlug(LaserPlug plug) => DescribeMismatch(plug) == null;

        /// <summary>Settings the running feed takes without a rebuild: brightness ceiling, channel
        /// order, keepalive. The feed name and the target need a new registration.</summary>
        public void ApplyLiveSettings(LaserPlug plug)
        {
            var device = plug.CitpSettings;
            _feed.MaxBrightness = Math.Clamp(device.MaxBrightness, 0f, 1f);
            _feed.SwapColorChannels = device.SwapColorChannels;
            _feed.Keepalive = device.Keepalive;
        }

        public string? DescribeMismatch(LaserPlug plug)
        {
            var device = plug.CitpSettings;
            var feedName = device.ResolveFeedName(plug.Name);
            var directPort = plug.Port > 0 ? plug.Port : 0;

            if (feedName != _feedName)
                return $"feed name '{_feedName}' -> '{feedName}'";

            // A plug re-pinned to the peer the source is already talking to is the same session.
            var address = plug.Address ?? string.Empty;
            if ((address != _directHost || directPort != _directPort) && !_hub.IsConnectedTo(address, directPort))
                return $"target '{_directHost}:{_directPort}' -> '{address}:{directPort}'";
            if ((plug.LocalAddress ?? string.Empty) != _localAddress)
                return $"local address '{_localAddress}' -> '{plug.LocalAddress}'";

            return null;
        }

        public void SubmitFrame(in LaserFrame frame)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            _hub.SubmitFrame(_feed, in frame);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _hub.UnregisterFeed(_feed);
        }
    }
    #endregion
}
