#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Streams the setup's laser outputs to their bound devices — the output-setup counterpart of
/// UpdatePresentation, and the replacement for the former graph-side output operators
/// ([EtherDreamOutput]/[PONKOutput]). Every pump interval each laser output's routed camera is
/// evaluated (or peeked when the camera already ran this frame), run through the output's
/// processing pipeline (optimizer + safety), and handed to the <see cref="LaserOutputStream"/>
/// of the laser plug the output is bound to on this machine.
///
/// Runs on the render thread, whatever view the editor shows — leaving the graph view must never
/// stop the show. The <see cref="LaserDriverEnabled"/> kill-switch silences the whole driver for
/// profiling.
/// </summary>
internal static class LaserDriver
{
    /// <summary>
    /// Pumps every laser output: camera frame in, pipeline through, device stream fed. Called from
    /// the frame loop (both the normal path and the minimized keep-alive path).
    /// </summary>
    public static void Update()
    {
        // The root instance, not the focused view: the setup's cameras live at the home level, and
        // navigating into a child composition must never stop the show from streaming.
        var composition = ProjectView.Focused?.RootInstance;
        var setup = ActiveSetup.Current;
        var machine = ActiveSetup.Machine;
        if (composition == null || setup == null || machine == null)
        {
            DisposeAllStreams();
            ClearOutputState();
            return;
        }

        // A pump that overran its budget skips the next editor frame. The pump runs every frame
        // while it stays cheap - the Board cards and output views read this cache, and a fixed
        // 30Hz gate against a 40-60Hz editor hands them a new frame every 1-2 frames, whose
        // alternating 24/48ms steps read as jerking motion. Skipping whole frames keeps the
        // heavy case evenly spaced instead of stretching to a 100ms interval, and the streams
        // loop their last frame at scan rate meanwhile, so the DAC never starves.
        if (_skipNextPump)
        {
            _skipNextPump = false;
            return;
        }

        _pumpGeneration++;
        var now = Stopwatch.GetTimestamp();

        // The pump runs inside the editor's frame loop: an exception from a broken project,
        // a half-disposed camera or a dying stream must degrade THIS interval, never take the
        // editor down. Every guard below logs throttled-once so a persistent failure stays
        // visible without flooding the log at pump rate.
        try
        {
            PumpAllOutputs(setup, machine, now);
        }
        catch (Exception e)
        {
            WarnThrottled($"laser pump failed - {e.Message}");
        }
    }

    private static void PumpAllOutputs(Setup setup, MachineConfig machine, long pullStart)
    {
        OutputContentResolver.PrepareContext(default);
        var context = OutputContentResolver.Context;

        lock (_diagnosticsLock)
        {
            PumpLaserOutputs.Clear();
            foreach (var output in setup.Outputs)
            {
                if (output.Kind == OutputDefinition.Kinds.Laser)
                    PumpLaserOutputs.Add(output);
            }
        }

        var livePlugIds = LivePlugIds;
        livePlugIds.Clear();

        // Every laser output's bound plugs are live for THIS pump, whether or not the output fits
        // the compute budget below. The stream reap at the end of the pump runs over this set: an
        // output that got deferred still owns its device streams, and reaping them there closed
        // the control session of every deferred output on every pump - the device saw "connected,
        // instantly disconnected" repeating ~10x a second. Bookkeeping only: dictionary lookups,
        // no evaluation, no device I/O.
        foreach (var output in PumpLaserOutputs)
        {
            foreach (var binding in machine.FindLaserBindings(output.Id))
            {
                if (machine.FindLaserPlug(binding.PlugId) is { } boundPlug)
                    livePlugIds.Add(boundPlug.Id);
            }
        }

        var budgetEnd = pullStart + PumpBudgetTicks;
        var count = PumpLaserOutputs.Count;

        _lastHeaviestComputeMs = 0;
        _lastComputeTotalMs = 0;
        _lastPipelineTotalMs = 0;
        _lastOutputsComputed = 0;
        _lastOutputsSkipped = 0;

        // Round-robin: each pump starts at the first output the previous pump had to defer, and the
        // expensive part (compute + submit) runs only while the frame budget holds. Whatever
        // doesn't fit keeps looping its last frame (the streams re-send it at scan rate) and
        // gets its turn on the next pump - a rig of N cameras can never block the editor frame
        // longer than the budget, however heavy its content; the show degrades in update rate
        // instead of the editor's frame rate.
        for (var i = 0; i < count; i++)
        {
            ProcessLaserOutput(machine, context, PumpLaserOutputs[(_pumpRotation + i) % count],
                               livePlugIds, budgetEnd, out var ranOutOfBudget);

            if (ranOutOfBudget)
            {
                // The deferred output leads the next pump. Starting after it would put it last in line
                // again, and a rig whose outputs together exceed the budget would starve that output
                // for good - its stream's frame watchdog then blanks the beam.
                _pumpRotation = (_pumpRotation + i) % count;
                break;
            }
        }

        _lastPumpTotalMs = (Stopwatch.GetTimestamp() - pullStart) * 1000.0 / Stopwatch.Frequency;

        // When a pump eats a noticeable slice of the editor frame, leave a breadcrumb in the log
        // with the per-stage split - the diagnostic that says WHICH stage to optimize. Time-based
        // throttle: the split changes every pump, so a per-message throttle would never fire.
        var nowTicks = Stopwatch.GetTimestamp();
        if (_lastPumpTotalMs > 12 && nowTicks - _lastSlowLog > Stopwatch.Frequency * 2)
        {
            _lastSlowLog = nowTicks;
            Log.Debug($"laser pump slow: {_lastPumpTotalMs:0.0}ms total, compute {_lastComputeTotalMs:0.0}ms"
                      + $" across {_lastOutputsComputed} outputs (pipeline {_lastPipelineTotalMs:0.0}ms)"
                      + $", {_lastOutputsSkipped} deferred");
        }

        // A stream whose plug (or its binding, or the output) vanished stops its threads — the
        // same lifetime the operator's Dispose had, just driven by the machine config instead of
        // the graph. Logged: a stream reaped while its plug still exists is a lifecycle bug that
        // shows up on the device as a connect/disconnect storm.
        DeadPlugIds.Clear();
        foreach (var plugId in _streams.Keys)
        {
            if (!livePlugIds.Contains(plugId))
                DeadPlugIds.Add(plugId);
        }

        foreach (var deadPlugId in DeadPlugIds)
        {
            if (_streams.TryRemove(deadPlugId, out var dead))
            {
                LogLifecycle($"laser plug {deadPlugId.ToString()[..8]} stream reaped - no output of this setup binds it any more");
                dead.Dispose();
            }

            _recreateDue.Remove(deadPlugId);
        }

        if (_pumpGeneration % PruneEveryPumps == 0)
            PruneOutputState();

        // The pump runs on the render thread and shares the frame budget with everything else.
        // A pump costlier than the capture budget halves its own cadence (one skipped frame)
        // rather than stretching the interval: the frames stay evenly spaced, which reads as
        // steady motion, while the editor gets every second frame back.
        var pullCost = Stopwatch.GetTimestamp() - pullStart;
        _skipNextPump = pullCost > ExpensivePullThreshold;
    }

    /// <summary>Laser outputs of the active setup, in pump order - reused scratch, rebuilt per pump.</summary>
    private static readonly List<OutputDefinition> PumpLaserOutputs = [];

    /// <summary>Plugs some output of this pump binds; scratch, rebuilt per pump.</summary>
    private static readonly HashSet<Guid> LivePlugIds = [];

    /// <summary>Streams to reap at the end of a pump; scratch, rebuilt per pump.</summary>
    private static readonly List<Guid> DeadPlugIds = [];

    /// <summary>Pumps between sweeps of the per-output state of outputs that no longer exist.</summary>
    private const int PruneEveryPumps = 256;

    /// <summary>Guards the pump's diagnostic snapshot lists against the debug server's reader
    /// thread; the pump only holds it for list bookkeeping, never for camera evaluation.</summary>
    private static readonly object _diagnosticsLock = new();

    /// <summary>The output the next pump starts with (round-robin over the laser outputs).</summary>
    private static int _pumpRotation;

    /// <summary>How much of the editor's frame the laser pump may spend computing new frames.
    /// Outputs that don't fit keep looping their last frame (their streams re-send it at scan
    /// rate) and get their turn on the next pump.</summary>
    private static readonly long PumpBudgetTicks = Stopwatch.Frequency * 6 / 1000;

    /// <summary>Extra allowance for outputs after the first one in a pump: they reuse that
    /// pump's scene capture and only run their pipeline, so they must not be budgeted like the
    /// capture that produced it. Bounds a rig with distinct scenes to one extra capture.</summary>
    private static readonly long FollowUpBudgetTicks = Stopwatch.Frequency * 6 / 1000;

    /// <summary>
    /// One output's pump slice: keep its bound plugs' streams warm (cheap bookkeeping, every
    /// pump), then - only while the frame budget holds - compute its camera's frame and submit.
    /// At least one output always computes per pump, so the rotation always advances.
    /// </summary>
    private static void ProcessLaserOutput(MachineConfig machine, EvaluationContext context, OutputDefinition output,
                                           HashSet<Guid> livePlugIds, long budgetEnd, out bool ranOutOfBudget)
    {
        ranOutOfBudget = false;

        // One warm stream per bound plug - created even while the output is muted so the
        // connection stays up. A laser output may bind several plugs: the same frame fans
        // out to each device (two scanners, one show).
        _submitStreams.Clear();
        foreach (var binding in machine.FindLaserBindings(output.Id))
        {
            var plug = machine.FindLaserPlug(binding.PlugId);
            if (plug == null)
                continue;

            livePlugIds.Add(plug.Id);
            if (!_streams.TryGetValue(plug.Id, out var stream))
            {
                var created = LaserOutputStream.Create(plug);
                if (created == null)
                    continue;

                _streams[plug.Id] = created;
                stream = created;
            }
            else if (!MatchesAfterLiveUpdate(stream, plug))
            {
                // Device settings (brightness ceiling, scan rate, channel order, keepalive,
                // address) are applied when a stream is built. A drag on the plug card fires
                // every frame, so the rebuild waits for the edit to settle - one reconnect
                // instead of one per frame - and then applies the new values live.
                var rebuildNow = Stopwatch.GetTimestamp();
                if (!_recreateDue.TryGetValue(plug.Id, out var due))
                {
                    // First mismatch starts the settle window; do not reconnect on the very
                    // frame the edit began, or a drag rebuilds the stream once per frame.
                    _recreateDue[plug.Id] = rebuildNow + RecreateSettleTicks;
                    continue;
                }

                if (rebuildNow < due)
                    continue;

                _recreateDue.Remove(plug.Id);
                if (_streams.TryRemove(plug.Id, out var stale))
                {
                    // Name the field that changed: a persistent mismatch (something rewriting the
                    // plug every frame) would otherwise look like "connected, then instantly
                    // disconnected" repeating forever, with no hint why.
                    LogLifecycle($"laser plug '{plug.Name}' rebuilding stream - {stale.DescribeMismatch(plug)}");
                    stale.Dispose();
                }

                var recreated = LaserOutputStream.Create(plug);
                if (recreated == null)
                    continue;

                _streams[plug.Id] = recreated;
                stream = recreated;
            }
            else
            {
                _recreateDue.Remove(plug.Id);
            }

            _submitStreams.Add(stream);
        }

        // Compute once per output - also when it is muted or has no bindings. The Board cards and
        // the parameter previews read this cache, so a preview never has to pull the camera on
        // the UI thread while the pump is running (a data race on the pipeline scratch).
        var sending = output.IsSending;
        var now = Stopwatch.GetTimestamp();

        // Desired-FPS pacing: between recomputes the streams loop the last submitted frame at
        // scan rate, so skipping computes only lowers the content refresh rate. Gate on sending -
        // a muted output must keep blanking every pump, not loop its last lit frame. The frame
        // cache's timestamp doubles as the last-compute clock; no extra state per output.
        var desiredFps = Math.Clamp(output.LaserProcessing?.Safety.DesiredFps ?? 60, 1, 120);
        var minInterval = Stopwatch.Frequency / desiredFps;
        if (sending && _frameCache.TryGetValue(output.Id, out var paced)
                    && now - paced.Timestamp < minInterval)
            return;

        // The first compute of a pump always runs - even a camera heavier than the whole budget
        // must get its turn instead of starving (checked against the hard wall only, which is
        // false at the start of a pump). Every later output reuses that pump's scene capture
        // and only runs its pipeline, so it is bounded by the follow-up allowance instead of
        // the capture's estimate.
        var isFirstCompute = _lastOutputsComputed == 0;
        var limit = isFirstCompute ? budgetEnd : budgetEnd + FollowUpBudgetTicks;
        if (now > limit)
        {
            // A deferred output keeps looping its last frame - unless it was just muted.
            if (!sending)
            {
                foreach (var stream in _submitStreams)
                    stream.SubmitBlank();
            }

            ranOutOfBudget = true;
            _lastOutputsSkipped++;
            return;
        }

        var computeStart = Stopwatch.GetTimestamp();
        var frame = ComputeFrame(output, context);
        var computeTicks = Stopwatch.GetTimestamp() - computeStart;
        var computeMs = computeTicks * 1000.0 / Stopwatch.Frequency;
        lock (_diagnosticsLock)
        {
            _computeEstimateTicks[output.Id] = estimateNext(_computeEstimateTicks.GetValueOrDefault(output.Id), computeTicks);
            _lastComputeMsByOutput[output.Id] = computeMs;
        }

        _lastComputeTotalMs += computeMs;
        if (computeMs > _lastHeaviestComputeMs)
            _lastHeaviestComputeMs = computeMs;

        _lastOutputsComputed++;
        _frameCache[output.Id] = (Stopwatch.GetTimestamp(), frame);

        if (frame.IsEmpty || !sending)
        {
            // Muted, or nothing to draw: dark now. Returning without a submission would keep the
            // streams looping the last lit frame until their watchdog fires a second later.
            foreach (var stream in _submitStreams)
                stream.SubmitBlank();
            return;
        }

        // A calibrated frame already went through this exact safety config in the pipeline session,
        // so the door stage would be an identical pass plus two array copies - skip it. The test
        // screen and the uncalibrated alignment shapes bypass the pipeline's safety stage and
        // must still pass the door.
        var processing = output.LaserProcessing ?? LaserProcessingSettings.Default;
        var config = processing.Safety.ToConfig();
        foreach (var stream in _submitStreams)
            stream.SubmitFrame(frame, config, alreadySafetyApplied: frame.SafetyApplied);
    }

    private static bool MatchesAfterLiveUpdate(LaserOutputStream stream, LaserPlug plug)
    {
        stream.ApplyLiveSettings(plug);
        return stream.MatchesPlug(plug);
    }

    /// <summary>
    /// The frame this output streams right now: the pipeline result of the camera routed to it
    /// by OutputRef — or, while uncalibrated, the alignment rectangle (input ignored). Empty when
    /// no camera is routed and safety is calibrated. Backs the Board card and parameter previews:
    /// the pump refreshes the cache every interval with the frame it just submitted, so previews
    /// show the streamed truth without ever re-evaluating the camera. Only when the driver is not
    /// pumping (kill-switch off) does the preview compute on demand.
    /// The returned buffer belongs to the output's pipeline session and is rewritten by the next
    /// compute — draw it within the frame, never keep it.
    /// </summary>
    public static LaserFrame GetFrame(OutputDefinition output)
    {
        var now = Stopwatch.GetTimestamp();
        if (_frameCache.TryGetValue(output.Id, out var cached))
        {
            // While the driver is pumping, its cache is the streamed truth even when it is a
            // pump or two old: recomputing here would pull the camera a second time on the UI
            // thread (a full scene capture and readback) and race the pump on the pipeline
            // scratch. The pump now computes muted/no-binding outputs too, so this cache always
            // exists once it has run. The on-demand pull remains only for the driver disabled.
            if (LaserDriverEnabled.Enabled || now - cached.Timestamp < PreviewStaleAfter)
                return cached.Frame;
        }

        if (LaserDriverEnabled.Enabled)
            return LaserFrame.Empty;

        _frameCache[output.Id] = cached = (now, ComputeFrame(output, null));
        return cached.Frame;
    }

    /// <summary>The most recent raw frame the routed camera produced (pre-pipeline), for
    /// diagnostics - where the optimizer's input came from. Empty when no pull happened yet.</summary>
    public static LaserPoint[] GetRawFrame(OutputDefinition output)
    {
        foreach (var camera in LaserContent.Cameras)
        {
            if (camera is not Instance instance || LaserContent.OutputRefOf(instance) != output.Id)
                continue;

            if (!_cameraPulls.TryGetValue(instance, out var pulled))
                break;

            if (pulled.List is StructuredList<LaserPoint> typed && typed.TypedElements != null)
                return typed.TypedElements;
            break;
        }

        return Array.Empty<LaserPoint>();
    }

    /// <summary>Scanning readout for the setup UI: the last computed frame's point count, the
    /// bound plug's scan rate (0 = the shared 30 kpps default), and the ILDA frame rate the
    /// scanner actually achieves (PPS / points — 30k / 500 = 60). Zeros when nothing was
    /// computed or no plug is bound. UI-thread only: the pump swaps the cache atomically, so a
    /// concurrent read never tears.</summary>
    public static (int PointCount, int ScanRate, int EffectiveFps) GetScanStats(OutputDefinition output)
    {
        var pointCount = _frameCache.TryGetValue(output.Id, out var cached) ? cached.Frame.Count : 0;

        var machine = ActiveSetup.Machine;
        var binding = machine?.FindBinding(output.Id);
        var plug = binding is { IsLaser: true } ? machine!.FindLaserPlug(binding.PlugId) : null;
        if (plug == null)
            return (pointCount, 0, 0);

        var scanRate = ResolvePlugScanRate(plug);
        return (pointCount, scanRate, LaserProcessingSettings.SafetySettings.EffectiveFrameRate(scanRate, pointCount));
    }

    /// <summary>The plug's configured scan rate; 0 for the transports without a rate setting
    /// (PONK streams at the receiver's rate) resolves to the shared 30 kpps default.</summary>
    public static int ResolvePlugScanRate(LaserPlug plug)
    {
        var rate = plug.Kind switch
                       {
                           LaserPlug.Kinds.EtherDream => plug.EtherDream?.ScanRate ?? 0,
                           LaserPlug.Kinds.ShowNet => plug.ShowNet?.ScanRate ?? 0,
                           _ => 0,
                       };
        return rate == 0 ? 30000 : rate;
    }

    /// <summary>The live status of the stream serving this output — connection, buffer, watchdog.</summary>
    public static bool TryGetStreamStatus(Guid outputId, out string message, out IStatusProvider.StatusLevel level)
    {
        message = string.Empty;
        level = IStatusProvider.StatusLevel.Undefined;

        var machine = ActiveSetup.Machine;
        if (machine == null)
            return false;

        var binding = machine.FindBinding(outputId);
        if (binding is not { IsLaser: true })
            return false;

        if (!_streams.TryGetValue(binding.PlugId, out var stream))
            return false;

        // Per-stage pump timings, right on the plug card: total, the heaviest single camera
        // (scene evaluation + GPU readback + conversion for the output that paid them), the
        // pipeline stage (optimizer + safety, what the Calibrated gate switches on), and how
        // many of the rig's outputs fit this frame's budget.
        message = $"{stream.StatusMessage} · pump {LastPumpTotalMs:0}ms, heaviest camera {LastHeaviestComputeMs:0}ms"
                  + $", pipeline {LastPipelineTotalMs:0}ms"
                  + $", {LastOutputsComputed}/{LastOutputsComputed + LastOutputsSkipped} cameras this frame";
        level = stream.StatusLevel;
        return true;
    }

    /// <summary>Last pump's total time on the render thread, in milliseconds.</summary>
    public static double LastPumpTotalMs => _lastPumpTotalMs;

    /// <summary>Last pump's most expensive single camera pull (scene capture + readback + conversion).</summary>
    public static double LastHeaviestComputeMs => _lastHeaviestComputeMs;

    /// <summary>Last pump's total time in the output pipeline (optimizer + safety) - the stage the
    /// Calibrated gate switches on.</summary>
    public static double LastPipelineTotalMs => _lastPipelineTotalMs;

    private static double _lastPumpTotalMs;
    private static double _lastHeaviestComputeMs;
    private static double _lastComputeTotalMs;
    private static double _lastPipelineTotalMs;
    private static int _lastOutputsComputed;
    private static int _lastOutputsSkipped;
    private static long _lastSlowLog;

    /// <summary>Time-throttled lifecycle log for stream rebuilds and reaps. A per-message throttle
    /// would print the first occurrence and then hide a repeating loop - and a repeating lifecycle
    /// line IS the diagnostic (a device sees it as "connected, instantly disconnected").</summary>
    private static void LogLifecycle(string message)
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _lastLifecycleLog < Stopwatch.Frequency * 2)
            return;

        _lastLifecycleLog = now;
        Log.Debug(message);
    }

    private static long _lastLifecycleLog;

    /// <summary>Each output's smoothed compute duration, in ticks - the pump uses it to not start
    /// a compute that cannot finish inside the frame's remaining budget. Render-thread only.</summary>
    private static readonly Dictionary<Guid, long> _computeEstimateTicks = [];

    /// <summary>Last measured compute duration per output, in ms - diagnostics only.</summary>
    private static readonly Dictionary<Guid, double> _lastComputeMsByOutput = [];

    /// <summary>Exponential blend toward the latest measurement (spikes fade over ~4 pumps).</summary>
    private static long estimateNext(long previous, long latest)
        => previous == 0 ? latest : (previous * 3 + latest) / 4;

    private static double LastOutputsComputed => _lastOutputsComputed;
    private static double LastOutputsSkipped => _lastOutputsSkipped;

    /// <summary>
    /// The camera's frame through the output's pipeline. With a context supplied (the driver's
    /// pump) the routed camera is evaluated into it; with null (a preview outside the pump -
    /// driver kill-switch off) a fresh context is prepared so time-animated content keeps
    /// moving. One evaluation per camera per pump interval: two outputs routed to the same
    /// camera must not run the scene (and its GPU readbacks) twice.
    /// </summary>
    private static LaserFrame ComputeFrame(OutputDefinition output, EvaluationContext? pumpContext)
    {
        // Transport budget: a CITP LaserFeedFrame travels as ONE datagram capped at 12,000 points
        // and the encode drops everything beyond it - converting more per camera is work whose
        // result is thrown away. Bound the capture when every bound plug is CITP; other transports
        // (or a CITP + non-CITP mix, where the full frame fans out to all) stream uncapped.
        var pointBudget = 0;
        var machine = ActiveSetup.Machine;
        if (machine != null)
        {
            var anyPlug = false;
            var allCitp = true;
            foreach (var binding in machine.FindLaserBindings(output.Id))
            {
                var plug = machine.FindLaserPlug(binding.PlugId);
                if (plug == null)
                    continue;

                anyPlug = true;
                if (plug.Kind != LaserPlug.Kinds.Citp)
                    allCitp = false;
            }

            if (anyPlug && allCitp)
                pointBudget = CitpMaxFramePoints;
        }

        // Test screen: the alignment rectangle with the output's name, regardless of the Calibrated
        // gate - it is the aiming aid used BEFORE calibration. The stream door still applies the
        // safety config, so the safe zone clamps it like anything else.
        if (_testScreenOutputs.ContainsKey(output.Id))
        {
            var testSettings = output.LaserProcessing ?? LaserProcessingSettings.Default;
            LaserPipeline.RectFromSafety(testSettings.Safety, out var cx, out var cy, out var hw, out var hh);
            var testFrame = GetTestFrame(output, cx, cy, hw, hh);
            return new LaserFrame(testFrame, testFrame.Length);
        }

        StructuredList? list = null;
        foreach (var camera in LaserContent.Cameras)
        {
            if (camera is not Instance instance || LaserContent.OutputRefOf(instance) != output.Id)
                continue;

            // Keyed per live instance, not per symbol child: the same op child can be instantiated
            // several times (a composition placed twice), and those are separate cameras - one must
            // never receive the other's cached pull. An on-demand preview (no pump context) always
            // pulls: the pump generation does not advance while the driver is off.
            if (pumpContext != null
                && _cameraPulls.TryGetValue(instance, out var pulled)
                && pulled.Generation == _pumpGeneration)
            {
                // Already evaluated in this pump - another output shares the camera.
                list = pulled.List;
            }
            else
            {
                // The pull evaluates the routed camera's FULL scene subtree — user project code.
                // A broken graph must cost this interval's frame, not the editor.
                LaserContentRegistry.PointBudget = pointBudget;
                try
                {
                    if (pumpContext == null)
                    {
                        // The shared evaluation context, reset for this pull: in setup mode with no
                        // video composite rendering, nobody else prepares it, and a stale context freezes
                        // time-animated content (the camera's roll animator, oscillators).
                        OutputContentResolver.PrepareContext(default);
                        list = ((ILaserContentSupplier)instance).GetLaserPoints(OutputContentResolver.Context);
                    }
                    else
                    {
                        list = ((ILaserContentSupplier)instance).GetLaserPoints(pumpContext);
                    }
                }
                catch (Exception e)
                {
                    WarnThrottled($"laser camera evaluation failed - {e.Message}");
                }
                finally
                {
                    LaserContentRegistry.PointBudget = 0;
                }

                _cameraPulls[instance] = (_pumpGeneration, list);
            }

            break;
        }

        LaserPoint[]? input = null;
        var count = 0;
        if (list is StructuredList<LaserPoint> typed && typed.TypedElements != null && typed.NumElements > 0)
        {
            input = typed.TypedElements;
            count = typed.NumElements;
        }

        if (!_pipelineSessions.TryGetValue(output.Id, out var session))
            _pipelineSessions[output.Id] = session = new LaserPipeline.Session();

        var settings = output.LaserProcessing ?? LaserProcessingSettings.Default;

        // Frame mapping: in calibrated mode the incoming frame's actual extent is rescaled into
        // the aimed zone (the setup rect) - Stretch fills both axes, Fit preserves aspect. With
        // frame warp on, the frame lands on the four keystone corners instead (a homography, so
        // it can be trapezed onto a skewed screen; an invalid corner set yields no frame, never the
// unwarped full field). Before the pipeline, so the safety transforms
        // clamp the mapped result. The input is the camera's live backing array; mapping writes
        // into the session's reused buffer (a per-pump allocation here lands in the large-object
        // heap at pump rate and turns into GC stalls across the rig).
        if (settings.Safety.FrameWarp)
            input = LaserPipeline.WarpFrameIntoQuad(input, count, settings.Safety, session.GetMappedBuffer(count));
        else if (settings.Safety.Calibrated)
            input = LaserPipeline.MapFrameIntoZone(input, count, settings.Safety, output.FrameMapping, session.GetMappedBuffer(count));

        var pipelineStart = Stopwatch.GetTimestamp();

        // The µs motion budgets (drawing speed, motion response, corner settle, transitions)
        // convert to point counts at the bound plug's scan rate - the same resolution the scan
        // readout uses. No plug bound: the shared 30 kpps default.
        var scanRatePps = 30000;
        var motionBinding = machine?.FindBinding(output.Id);
        if (motionBinding is { IsLaser: true })
        {
            var motionPlug = machine!.FindLaserPlug(motionBinding.PlugId);
            if (motionPlug != null)
                scanRatePps = ResolvePlugScanRate(motionPlug);
        }

        var result = session.Apply(settings, input, count, out var frameCount, scanRatePps);
        if (settings.Safety.Calibrated && settings.Safety.RenderSafetyShapes)
            result = session.AppendSafetyShapes(settings.Safety, result, frameCount, out frameCount);
        _lastPipelineTotalMs += (Stopwatch.GetTimestamp() - pipelineStart) * 1000.0 / Stopwatch.Frequency;
        return new LaserFrame(result, frameCount, settings.Safety.Calibrated);
    }

    /// <summary>A pump costlier than the capture budget skips the next editor frame, keeping the
    /// remaining pumps evenly spaced instead of stretching the interval. Below it the pump runs
    /// every editor frame - that is what keeps the previews moving smoothly.</summary>
    private static readonly long ExpensivePullThreshold = PumpBudgetTicks;

    /// <summary>Set by an over-budget pump; the next Update() consumes it and yields one frame.</summary>
    private static bool _skipNextPump;

    /// <summary>Increments once per pump - the camera-pull cache dedupes within a pump, not
    /// within a fixed time window, so the pump may run at the editor's frame rate.</summary>
    private static long _pumpGeneration;

    /// <summary>Device streams per laser plug id; created lazily, disposed when the plug's binding
    /// goes away. Concurrent: the debug server reads it from its TCP thread while the pump writes.</summary>
    private static readonly ConcurrentDictionary<Guid, LaserOutputStream> _streams = [];

    /// <summary>Pipeline scratch per output; render-thread only, like everything that reads it.</summary>
    private static readonly Dictionary<Guid, LaserPipeline.Session> _pipelineSessions = [];

    /// <summary>The frame the pump just computed and submitted per output - what the previews show.
    /// Concurrent for the debug server's diagnostics reader.</summary>
    private static readonly ConcurrentDictionary<Guid, (long Timestamp, LaserFrame Frame)> _frameCache = [];

    /// <summary>How long a pump-cached frame backs the previews before an on-demand pull (only while the driver is off).</summary>
    private static readonly long PreviewStaleAfter = Stopwatch.Frequency / 4; // 250 ms

    /// <summary>One camera evaluation per pump interval, keyed by the live camera instance - shared by
    /// outputs routed to the same camera instance, separate for separate cameras. Entries of cameras
    /// that stopped being pulled are swept by <see cref="PruneOutputState"/>, so a deleted camera is
    /// not kept alive.</summary>
    private static readonly Dictionary<Instance, (long Generation, StructuredList? List)> _cameraPulls = [];

    /// <summary>The most points a CITP LaserFeedFrame datagram carries.</summary>
    private const int CitpMaxFramePoints = 12000;

    /// <summary>Drops the state of outputs and cameras that left the setup: pipeline buffers are
    /// large, and a deleted output would otherwise keep its share for the rest of the session.</summary>
    private static void PruneOutputState()
    {
        StaleIds.Clear();
        foreach (var outputId in _pipelineSessions.Keys)
        {
            if (!IsPumped(outputId))
                StaleIds.Add(outputId);
        }

        lock (_diagnosticsLock)
        {
            foreach (var outputId in StaleIds)
            {
                _pipelineSessions.Remove(outputId);
                _computeEstimateTicks.Remove(outputId);
                _lastComputeMsByOutput.Remove(outputId);
                _testFrames.Remove(outputId);
                _frameCache.TryRemove(outputId, out _);
            }
        }

        StaleCameras.Clear();
        foreach (var (camera, pull) in _cameraPulls)
        {
            if (pull.Generation < _pumpGeneration - 1)
                StaleCameras.Add(camera);
        }

        foreach (var camera in StaleCameras)
        {
            _cameraPulls.Remove(camera);
        }
    }

    private static bool IsPumped(Guid outputId)
    {
        for (var i = 0; i < PumpLaserOutputs.Count; i++)
        {
            if (PumpLaserOutputs[i].Id == outputId)
                return true;
        }

        return false;
    }

    /// <summary>Everything cached per output and camera, for when the setup goes away altogether.</summary>
    private static void ClearOutputState()
    {
        lock (_diagnosticsLock)
        {
            _pipelineSessions.Clear();
            _cameraPulls.Clear();
            _frameCache.Clear();
            _computeEstimateTicks.Clear();
            _lastComputeMsByOutput.Clear();
            _testFrames.Clear();
            _recreateDue.Clear();
        }
    }

    private static readonly List<Guid> StaleIds = [];
    private static readonly List<Instance> StaleCameras = [];

    /// <summary>Plugs whose device settings changed, and the timestamp their stream may rebuild at.
    /// Render-thread only, like the pump that fills it.</summary>
    private static readonly Dictionary<Guid, long> _recreateDue = [];

    /// <summary>How long a changed device setting must hold still before its stream rebuilds: a
    /// drag on a plug card fires every frame and must reconnect once, not once per frame.</summary>
    private static readonly long RecreateSettleTicks = Stopwatch.Frequency / 4;

    /// <summary>The test screen only changes with the output's name, its aimed rectangle or the
    /// selected pattern, so it is built once and replayed - it is pumped every interval while shown.</summary>
    private static LaserPoint[] GetTestFrame(OutputDefinition output, int cx, int cy, int halfWidth, int halfHeight)
    {
        var key = (output.Name, cx, cy, halfWidth, halfHeight, TestScreenPattern);
        if (_testFrames.TryGetValue(output.Id, out var cached) && cached.Key == key)
            return cached.Frame;

        var frame = TestScreenPattern == LaserTestFrame.Pattern.NameFrame
                        ? LaserTestFrame.Build(output.Name, cx, cy, halfWidth, halfHeight)
                        : LaserTestFrame.BuildPattern(TestScreenPattern, cx, cy, halfWidth, halfHeight);
        _testFrames[output.Id] = (key, frame);
        return frame;
    }

    private static readonly Dictionary<Guid, ((string Name, int Cx, int Cy, int HalfWidth, int HalfHeight, LaserTestFrame.Pattern Pattern) Key, LaserPoint[] Frame)> _testFrames = [];

    /// <summary>Outputs currently streaming the test screen (rectangle + output name); transient
    /// show state, never saved. Concurrent: toggled from the UI, read by the debug server.</summary>
    private static readonly ConcurrentDictionary<Guid, byte> _testScreenOutputs = [];

    /// <summary>The pattern every shown test screen streams (one shared picker: calibration is a
    /// one-person activity, and a per-output picker would desync the wizard flow).</summary>
    public static LaserTestFrame.Pattern TestScreenPattern { get; set; } = LaserTestFrame.Pattern.NameFrame;

    /// <summary>Streams to fan this pump's frame out to - scratch, rebuilt per output. Compute
    /// happens once per output, after the (cheap) stream bookkeeping, so an output with no
    /// bindings still produces a frame for the previews.</summary>
    private static readonly List<LaserOutputStream> _submitStreams = [];

    public static bool IsTestScreen(Guid outputId) => _testScreenOutputs.ContainsKey(outputId);

    public static void SetTestScreen(Guid outputId, bool on)
    {
        if (on)
            _testScreenOutputs[outputId] = 0;
        else
            _testScreenOutputs.TryRemove(outputId, out _);
    }

    /// <summary>Logs a warning once per message per few seconds - visible failures without
    /// log flooding when the pump hits the same problem every interval.</summary>
    private static void WarnThrottled(string message)
    {
        var now = Stopwatch.GetTimestamp();
        var last = _lastWarnTimestamp;
        if (last != 0 && now - last < Stopwatch.Frequency && _lastWarnMessage == message)
            return;

        _lastWarnTimestamp = now;
        _lastWarnMessage = message;
        Log.Warning($"Laser driver: {message}");
    }

    private static long _lastWarnTimestamp;
    private static string? _lastWarnMessage;

    /// <summary>Live per-output state for the debug protocol: what the pump is doing, what the
    /// streams report, and how big the last frame was - the remote view of the show.</summary>
    public static IEnumerable<object> GetDiagnostics(Setup setup, MachineConfig machine)
    {
        foreach (var output in setup.Outputs)
        {
            if (output.Kind != OutputDefinition.Kinds.Laser)
                continue;

            var bindings = new List<object>();
            foreach (var binding in machine.FindLaserBindings(output.Id))
            {
                var plug = machine.FindLaserPlug(binding.PlugId);
                if (plug == null)
                    continue;

                _streams.TryGetValue(plug.Id, out var stream);
                bindings.Add(new
                                 {
                                     plug = plug.Name,
                                     kind = plug.Kind,
                                     connected = stream is { IsConnected: true },
                                     status = stream?.StatusMessage ?? "no stream",
                                 });
            }

            _frameCache.TryGetValue(output.Id, out var cached);
            var diagPlug = machine.FindLaserBindings(output.Id)
                                .Select(b => machine.FindLaserPlug(b.PlugId))
                                .FirstOrDefault(p => p != null);
            var diagScanRate = diagPlug == null ? 0 : ResolvePlugScanRate(diagPlug);
            yield return new
                             {
                                 name = output.Name,
                                 sending = output.IsSending,
                                 testScreen = _testScreenOutputs.ContainsKey(output.Id),
                                 calibrated = output.LaserProcessing?.Safety.Calibrated ?? false,
                                 framePoints = cached.Frame.Count,
                                 desiredFps = Math.Clamp(output.LaserProcessing?.Safety.DesiredFps ?? 60, 1, 120),
                                 scanRatePps = diagScanRate,
                                 effectiveFps = diagPlug == null ? 0
                                                    : LaserProcessingSettings.SafetySettings.EffectiveFrameRate(diagScanRate, cached.Frame.Count),
                                 bindings,
                             };
        }
    }

    /// <summary>Debug-protocol view of the pump: what one interval costs, how many outputs fit in
    /// it, and each output's learned compute estimate - answers "why do the previews lag".</summary>
    public static object GetPumpStats()
    {
        OutputDefinition[] snapshot;
        Dictionary<Guid, long> estimates;
        Dictionary<Guid, double> lastCompute;
        lock (_diagnosticsLock)
        {
            snapshot = PumpLaserOutputs.ToArray();
            estimates = new Dictionary<Guid, long>(_computeEstimateTicks);
            lastCompute = new Dictionary<Guid, double>(_lastComputeMsByOutput);
        }

        var outputs = new List<object>();
        foreach (var output in snapshot)
        {
            var estimateTicks = estimates.GetValueOrDefault(output.Id);
            _frameCache.TryGetValue(output.Id, out var cached);
            outputs.Add(new
                            {
                                name = output.Name,
                                estimateMs = Math.Round(estimateTicks * 1000.0 / Stopwatch.Frequency, 2),
                                lastComputeMs = Math.Round(lastCompute.GetValueOrDefault(output.Id), 2),
                                cachedFramePoints = cached.Frame.Count,
                                cachedAgeMs = cached.Frame.Points != null
                                                  ? Math.Round((Stopwatch.GetTimestamp() - cached.Timestamp) * 1000.0 / Stopwatch.Frequency, 1)
                                                  : -1.0,
                            });
        }

        return new
                   {
                       pumpTotalMs = Math.Round(_lastPumpTotalMs, 2),
                       computeTotalMs = Math.Round(_lastComputeTotalMs, 2),
                       pipelineTotalMs = Math.Round(_lastPipelineTotalMs, 2),
                       outputsComputed = _lastOutputsComputed,
                       outputsSkipped = _lastOutputsSkipped,
                       skipNextPump = _skipNextPump,
                       pumpGeneration = _pumpGeneration,
                       budgetMs = Math.Round(PumpBudgetTicks * 1000.0 / Stopwatch.Frequency, 1),
                       outputs,
                   };
    }

    private static void DisposeAllStreams()
    {
        if (_streams.IsEmpty)
            return;

        foreach (var key in _streams.Keys)
        {
            if (_streams.TryRemove(key, out var stream))
                stream.Dispose();
        }
    }
}
