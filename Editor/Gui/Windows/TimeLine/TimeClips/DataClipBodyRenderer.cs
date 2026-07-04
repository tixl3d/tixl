#nullable enable

using System.Runtime.CompilerServices;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.Styling;
using T3.IoServices;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Renders per-channel tick / density-rect overlays inside <see cref="DataClip"/>-output
/// TimeClip bodies. Each channel gets its own horizontal track within the body, and
/// events are chunked into either individual ticks (sparse) or faded rects (dense)
/// based on inter-event pixel spacing.
/// </summary>
/// <remarks>
/// Reads the slot's cached value when populated; falls back to loading the file directly
/// via <see cref="DataClipFiles"/> when no downstream consumer pulls on the op; falls
/// back further to the in-progress recording's live <see cref="DataSet"/> when this clip
/// is the target of an active <see cref="RecordingSession"/>. All three paths feed the
/// same per-channel renderer.
/// </remarks>
internal static class DataClipBodyRenderer
{
    public static void TryDraw(Instance instance,
                               TimeClip timeClip,
                               Vector2 bodyMin,
                               Vector2 bodyMax,
                               float viewMinX,
                               float viewMaxX,
                               ImDrawListPtr drawList)
    {
        // Op-type gate: only proceed if this op publishes a Slot<DataClip?>. Quick reject
        // for the 99% case (any other TimeClip op).
        Slot<DataClip?>? dataSlot = null;
        foreach (var slot in instance.Outputs)
        {
            if (slot is Slot<DataClip?> ds)
            {
                dataSlot = ds;
                break;
            }
        }
        if (dataSlot == null)
            return;

        if (!TryGetDataSetAndMapping(instance, dataSlot, timeClip, out var dataSet, out var mapping)
            || dataSet == null)
            return;

        var bodyWidth = bodyMax.X - bodyMin.X;
        var bodyHeight = bodyMax.Y - bodyMin.Y;
        if (bodyWidth < 3 || bodyHeight < 4)
            return;

        // Filter to channels that have events. Empty channels don't get a track so the
        // layout stays compact.
        _visibleChannelsScratch.Clear();
        foreach (var channel in dataSet.Channels)
        {
            if (channel.Events.Count > 0)
                _visibleChannelsScratch.Add(channel);
        }
        if (_visibleChannelsScratch.Count == 0)
            return;

        // Sort by joined path so the per-track ordering matches the channel list in the
        // DataSet output canvas (DataSetViewCanvas iterates `OrderBy(c => string.Join(".",
        // c.Path))`). Without this the clip body shows tracks in DataSet.Channels insertion
        // order — which is recorder-arrival order, hardware-dependent, and unrelated to
        // what the user sees in the output window. Comparer cached statically to skip the
        // per-frame string concatenation cost.
        _visibleChannelsScratch.Sort(_channelByPathComparer);

        // Track layout: 2 px tracks with 1 px gap when the body is tall enough, 1 px
        // tracks with no gap when tight, fall back to a single overlay rect when even
        // 1 px per channel doesn't fit.
        const float topPadding = 2f;
        const float bottomPadding = 2f;
        var usableHeight = bodyHeight - topPadding - bottomPadding;
        var n = _visibleChannelsScratch.Count;

        float trackHeight;
        float gap;
        var visibleTrackCount = n;
        if (usableHeight >= n * 2f + (n - 1) * 1f)
        {
            trackHeight = 2f;
            gap = 1f;
        }
        else if (usableHeight >= n * 1f)
        {
            trackHeight = 1f;
            gap = 0f;
        }
        else
        {
            // More channels than fit even at 1 px each — render as many real tracks as
            // fit and clip the rest at the bottom. A truncated set of per-channel tracks
            // reads better than collapsing everything into one featureless block.
            trackHeight = 1f;
            gap = 0f;
            visibleTrackCount = Math.Max(1, (int)usableHeight);
        }

        // Each channel gets its own fair slice of the per-clip budget so a heavy channel
        // can't starve later channels. A channel that uses less than its share doesn't
        // donate it — keeps the allocation predictable across frames.
        const int perClipBudget = 1500;
        var perChannelBudget = Math.Max(40, perClipBudget / visibleTrackCount);
        var trackColor = UiColors.ForegroundFull.Fade(0.55f);

        for (var i = 0; i < visibleTrackCount; i++)
        {
            var trackTopY = bodyMin.Y + topPadding + i * (trackHeight + gap);
            var trackBottomY = trackTopY + trackHeight;
            DrawChannelTrack(_visibleChannelsScratch[i], mapping,
                             bodyMin.X, bodyMax.X,
                             viewMinX, viewMaxX,
                             trackTopY, trackBottomY,
                             perChannelBudget,
                             trackColor, drawList);
        }
    }

    /// <summary>
    /// Single horizontal track for one channel. Dispatches between tick (point-in-time)
    /// rendering and interval rendering based on the channel's
    /// <see cref="DataChannel.DurationType"/>; both honour the per-channel budget.
    /// </summary>
    private static int DrawChannelTrack(DataChannel channel,
                                        TimeRangeMapping mapping,
                                        float bodyMinX,
                                        float bodyMaxX,
                                        float viewMinX,
                                        float viewMaxX,
                                        float trackTopY,
                                        float trackBottomY,
                                        int budget,
                                        Color tickColor,
                                        ImDrawListPtr drawList)
    {
        if (channel.DurationType == ChannelDurationTypes.Interval)
        {
            return DrawIntervalChannelTrack(channel, mapping,
                                            bodyMinX, bodyMaxX,
                                            trackTopY, trackBottomY,
                                            budget, tickColor, drawList);
        }

        return DrawTickChannelTrack(channel, mapping,
                                    bodyMinX, bodyMaxX,
                                    viewMinX, viewMaxX,
                                    trackTopY, trackBottomY,
                                    budget, tickColor, drawList);
    }

    /// <summary>
    /// Tick rendering for point-in-time channels (MIDI CC, OSC, telemetry). Walks events
    /// in order, groups consecutive ones within <see cref="MinTickGapPx"/> into runs, and
    /// renders each run as either individual ticks (small / sparse runs) or a single
    /// density-rect (larger / denser runs). Returns the number of draw commands emitted.
    /// </summary>
    private static int DrawTickChannelTrack(DataChannel channel,
                                            TimeRangeMapping mapping,
                                            float bodyMinX,
                                            float bodyMaxX,
                                            float viewMinX,
                                            float viewMaxX,
                                            float trackTopY,
                                            float trackBottomY,
                                            int budget,
                                            Color tickColor,
                                            ImDrawListPtr drawList)
    {
        // Channel.Events may be appended on a background thread during recording. Snapshot
        // the count once; any later additions appear on the next frame.
        var eventCount = channel.Events.Count;
        if (eventCount == 0 || budget <= 0)
            return 0;

        var rangeStart = mapping.TimeRange.Start;
        var rangeEnd = mapping.TimeRange.End;
        var rangeSpan = rangeEnd - rangeStart;
        if (rangeSpan < 0.0001)
            return 0;
        var bodySpan = bodyMaxX - bodyMinX;
        if (bodySpan < 0.0001)
            return 0;

        // Restrict the walk to events whose source time maps into the visible slice of the
        // body. For a long clip zoomed in (the "thousands of CC events" case), the bulk of
        // the list sits off-screen; events are stored in ascending source-time order, so a
        // binary search on the visible window's edges bounds the loop instead of stepping
        // across the whole channel every frame. Both window edges are converted to source
        // time and min/max'd so a time-reversed clip (SourceRange running backwards) still
        // searches a valid ascending range.
        var visMinX = MathF.Max(bodyMinX, viewMinX);
        var visMaxX = MathF.Min(bodyMaxX, viewMaxX);
        if (visMaxX < visMinX)
            return 0;

        var winStartBars = rangeStart + (visMinX - bodyMinX) / bodySpan * rangeSpan;
        var winEndBars = rangeStart + (visMaxX - bodyMinX) / bodySpan * rangeSpan;
        var secsA = mapping.LocalBarsToSourceSecs(winStartBars);
        var secsB = mapping.LocalBarsToSourceSecs(winEndBars);
        var minSecs = Math.Min(secsA, secsB);
        var maxSecs = Math.Max(secsA, secsB);

        // Widen by one event each side so a run beginning just outside the window still
        // contributes its leading / trailing tick.
        var firstIndex = Math.Max(0, channel.FindIndexForTime(minSecs) - 1);
        var lastIndex = Math.Min(eventCount - 1, channel.FindIndexForTime(maxSecs) + 1);
        if (lastIndex < firstIndex)
            return 0;
        var visibleCount = lastIndex - firstIndex + 1;

        // Even-decimation step when the worst case (every visible event its own tick) would
        // blow the budget. Better than front-loading: spreading the omissions evenly
        // preserves the visual rhythm of activity across the track instead of fading after
        // the first N events.
        var step = Math.Max(1, visibleCount / Math.Max(1, budget));

        var commands = 0;

        // Run state. We walk events and either:
        //   - extend the current run if the new event is within MinTickGapPx of the previous
        //   - flush the run (as ticks or rect, depending on count) and start a new one
        var runStartX = float.NaN;
        var runEndX = float.NaN;
        var runEventCount = 0;
        var prevX = float.NegativeInfinity;

        for (var i = firstIndex; i <= lastIndex; i += step)
        {
            if (commands >= budget)
                break;

            DataEvent? ev;
            try { ev = channel.Events[i]; }
            catch (ArgumentOutOfRangeException) { break; }  // list shrank under us

            if (ev == null)
                continue;

            var localBars = mapping.SourceSecsToLocalBars(ev.Time);
            if (localBars < rangeStart || localBars > rangeEnd)
                continue;

            var t = (localBars - rangeStart) / rangeSpan;
            // Snap to integer pixels — sub-pixel float positions get rasterised with
            // anti-aliasing, and a fractional change frame-to-frame (e.g. body width
            // edging from 132.4 → 132.5 px while dragging an edge) shows up as a 1 px
            // tick jitter even though the absolute timeline position barely moved.
            var x = MathF.Floor((float)(bodyMinX + t * bodySpan));

            if (float.IsNaN(runStartX))
            {
                runStartX = x;
                runEndX = x;
                runEventCount = 1;
                prevX = x;
                continue;
            }

            if (x - prevX <= MinTickGapPx)
            {
                runEndX = x;
                runEventCount++;
                prevX = x;
            }
            else
            {
                commands += FlushRun(runStartX, runEndX, runEventCount,
                                     trackTopY, trackBottomY, tickColor, drawList);
                runStartX = x;
                runEndX = x;
                runEventCount = 1;
                prevX = x;
            }
        }

        if (!float.IsNaN(runStartX) && commands < budget)
        {
            commands += FlushRun(runStartX, runEndX, runEventCount,
                                 trackTopY, trackBottomY, tickColor, drawList);
        }

        return commands;
    }

    /// <summary>
    /// Interval rendering for span-in-time channels (MIDI notes today; subtitles /
    /// regions later). Draws each event as a filled bar from its <see cref="DataEvent.Time"/>
    /// to its <see cref="DataIntervalEvent.EndTime"/>, clipped to the body bounds.
    /// Unfinished intervals (live-recording note-ons before the note-off arrives) stretch
    /// to the right edge of the body so the user sees the held note grow.
    /// </summary>
    /// <remarks>
    /// Run-collapsing isn't applied here — every interval is a distinct event with its own
    /// duration, so the tick-density heuristic that works for point streams would smear
    /// the timing information. The per-channel budget still caps draw commands; once it's
    /// reached the loop bails out, leaving the leading interval(s) visible.
    /// </remarks>
    private static int DrawIntervalChannelTrack(DataChannel channel,
                                                TimeRangeMapping mapping,
                                                float bodyMinX,
                                                float bodyMaxX,
                                                float trackTopY,
                                                float trackBottomY,
                                                int budget,
                                                Color tickColor,
                                                ImDrawListPtr drawList)
    {
        var eventCount = channel.Events.Count;
        if (eventCount == 0 || budget <= 0)
            return 0;

        var rangeStart = mapping.TimeRange.Start;
        var rangeEnd = mapping.TimeRange.End;
        var rangeSpan = rangeEnd - rangeStart;
        if (rangeSpan < 0.0001)
            return 0;
        var bodySpan = bodyMaxX - bodyMinX;

        var step = Math.Max(1, eventCount / Math.Max(1, budget));
        var commands = 0;

        for (var i = 0; i < eventCount; i += step)
        {
            if (commands >= budget)
                break;

            DataEvent? ev;
            try { ev = channel.Events[i]; }
            catch (ArgumentOutOfRangeException) { break; }

            if (ev is not DataIntervalEvent interval)
                continue;

            var startLocalBars = mapping.SourceSecsToLocalBars(interval.Time);
            // Unfinished interval (recorder hasn't seen note-off yet) — visualise it
            // running to the end of the clip so the user sees it grow as they hold the
            // note rather than vanishing because EndTime is +∞.
            var endLocalBars = double.IsInfinity(interval.EndTime)
                                   ? rangeEnd
                                   : mapping.SourceSecsToLocalBars(interval.EndTime);

            // Cull intervals fully outside the clip's TimeRange.
            if (endLocalBars < rangeStart || startLocalBars > rangeEnd)
                continue;

            // Clip the visible span to the body so a long interval that overlaps the
            // edge stops at the edge instead of drawing off-clip.
            var clippedStart = Math.Max(startLocalBars, rangeStart);
            var clippedEnd = Math.Min(endLocalBars, rangeEnd);

            var t1 = (clippedStart - rangeStart) / rangeSpan;
            var t2 = (clippedEnd - rangeStart) / rangeSpan;
            var x1 = MathF.Floor((float)(bodyMinX + t1 * bodySpan));
            var x2 = MathF.Floor((float)(bodyMinX + t2 * bodySpan));
            // Even a zero-duration interval should be visible — same TickWidthPx the
            // point-event branch uses for parity at high zoom.
            if (x2 < x1 + TickWidthPx)
                x2 = x1 + TickWidthPx;

            drawList.AddRectFilled(new Vector2(x1, trackTopY),
                                   new Vector2(x2, trackBottomY),
                                   tickColor);
            commands++;
        }

        return commands;
    }

    /// <summary>
    /// Emits draw commands for one run of consecutive close-together events. Small runs
    /// (≤ <see cref="RunSizeForTicks"/>) render as individual ticks; larger runs collapse
    /// into a single density-rect with alpha scaled by events-per-pixel.
    /// </summary>
    private static int FlushRun(float startX, float endX, int eventCount,
                                float trackTopY, float trackBottomY,
                                Color tickColor, ImDrawListPtr drawList)
    {
        if (eventCount == 1)
        {
            drawList.AddRectFilled(new Vector2(startX, trackTopY),
                                   new Vector2(startX + TickWidthPx, trackBottomY),
                                   tickColor);
            return 1;
        }

        if (eventCount <= RunSizeForTicks)
        {
            // Sparse run — N ticks across the run, evenly spaced. (Intermediate event
            // positions weren't recorded; interpolation matches the original spacing at
            // sub-pixel precision.)
            for (var i = 0; i < eventCount; i++)
            {
                var t = eventCount == 1 ? 0f : i / (float)(eventCount - 1);
                // Floor the interpolated tick position too — startX/endX are already
                // pixel-aligned at this point, but t * (endX - startX) reintroduces
                // sub-pixel offsets that cause the same jitter the outer loop avoids.
                var x = MathF.Floor(startX + t * (endX - startX));
                drawList.AddRectFilled(new Vector2(x, trackTopY),
                                       new Vector2(x + TickWidthPx, trackBottomY),
                                       tickColor);
            }
            return eventCount;
        }

        // Dense run — single rect with alpha scaled by density.
        var width = MathF.Max(1f, endX - startX);
        var density = eventCount / width;
        var alpha = MathF.Min(0.7f, 0.25f + density * 0.4f);
        drawList.AddRectFilled(new Vector2(startX, trackTopY),
                               new Vector2(endX + TickWidthPx, trackBottomY),
                               tickColor.Fade(alpha));
        return 1;
    }

    /// <summary>
    /// Resolves the <see cref="DataSet"/> + <see cref="TimeRangeMapping"/> for the
    /// current clip. <see cref="DataSet"/> source priority order:
    /// (1) the op's output slot value when it's been evaluated,
    /// (2) the live <see cref="DataSet"/> from an active <see cref="RecordingSession"/>
    ///     when this clip is the recording target — lets the timeline show events
    ///     streaming in during capture,
    /// (3) the file on disk via the shared <see cref="DataClipFiles"/> resolver when the op is
    ///     unwired and not being recorded into.
    /// </summary>
    /// <remarks>
    /// The mapping is always rebuilt from the live <see cref="TimeClip"/> via
    /// <see cref="TryBuildMapping"/>, never read from <c>slot.Value.Mapping</c>. The slot
    /// has <c>DirtyFlagTrigger.Animated</c>, so its cached value is refreshed only when
    /// playback time advances — drag-mutating <see cref="TimeClip.TimeRange"/> or
    /// <see cref="TimeClip.SourceRange"/> doesn't fire <c>Update</c>, and the cached
    /// mapping would otherwise drift one or more frames behind the body bounds until the
    /// user scrubbed. Reading from the live <see cref="TimeClip"/> is allocation-free and
    /// keeps the visualisation in lockstep with the drag.
    /// </remarks>
    private static bool TryGetDataSetAndMapping(Instance instance,
                                                Slot<DataClip?> dataSlot,
                                                TimeClip timeClip,
                                                out DataSet? dataSet,
                                                out TimeRangeMapping mapping)
    {
        mapping = default;

        var clipValue = dataSlot.Value;
        if (clipValue?.Set != null)
        {
            dataSet = clipValue.Set;
            return TryBuildMapping(timeClip, out mapping);
        }

        // Live-recording fallback: if this clip is the active session's target, use the
        // in-flight DataSet.
        if (RecordingSession.TryGetLiveDataSet(instance.SymbolChildId, out var liveSet) && liveSet != null)
        {
            dataSet = liveSet;
            return TryBuildMapping(timeClip, out mapping);
        }

        // File fallback: read the path from the op's IDescriptiveFilename interface and
        // look up the parsed DataSet from the shared cache.
        dataSet = null;
        if (instance is not IDescriptiveFilename descriptive)
            return false;

        var path = descriptive.SourcePathSlot.TypedInputValue.Value;
        if (string.IsNullOrEmpty(path))
            return false;
        if (!AssetRegistry.TryResolveAddress(path, instance, out var absolutePath, out _))
            return false;
        if (!DataClipFiles.TryGetDataSetForFile(path, absolutePath, out dataSet, out _))
            return false;

        return TryBuildMapping(timeClip, out mapping);
    }

    /// <summary>
    /// Builds the source-content-relative <see cref="TimeRangeMapping"/> the same way
    /// <c>LoadDataClip.Update</c> does — direct identity from <see cref="TimeClip.TimeRange"/>
    /// and <see cref="TimeClip.SourceRange"/>. SourceRange is in file-time (recording
    /// anchored at 0); cut / start-handle trim adjust it in that space. Keeps the
    /// renderer's culling and tick placement consistent with what <c>SimulateIoData</c>
    /// uses for dispatch.
    /// </summary>
    private static bool TryBuildMapping(TimeClip timeClip, out TimeRangeMapping mapping)
    {
        mapping = default;
        var playback = Playback.Current;
        if (playback == null)
            return false;

        mapping = new TimeRangeMapping(timeClip.TimeRange, timeClip.SourceRange, playback.Bpm);
        return true;
    }

    /// <summary>Width of a single tick in pixels — 2 keeps marks visible at all zooms.</summary>
    private const float TickWidthPx = 2f;

    /// <summary>
    /// Min gap in pixels between consecutive events for them to remain visually distinct
    /// ticks rather than collapse into a density-rect. Sub-pixel resolution events become
    /// rects automatically.
    /// </summary>
    private const float MinTickGapPx = 2.5f;

    /// <summary>
    /// Threshold for ticks-vs-rect within a run: runs with this many events or fewer
    /// render as individual ticks, larger runs collapse to a single density-rect.
    /// </summary>
    private const int RunSizeForTicks = 5;

    // Scratch buffer reused across frames to avoid per-frame allocation when filtering
    // channels by event count.
    private static readonly List<DataChannel> _visibleChannelsScratch = new();

    // Joined-path comparer for the per-clip track order. Joins on '.' so the sort matches
    // the DataSet output canvas's `OrderBy(c => string.Join(".", c.Path))`. The joined key
    // is stable (Path is init-only), so it's cached per channel — the sort runs once per
    // clip per frame and the naive `string.Join` in the comparer otherwise allocated two
    // strings per comparison, churning the GC across every visible DataClip. The weak table
    // lets the keys collect with their channels across project reloads.
    private static readonly ConditionalWeakTable<DataChannel, string> _channelPathKeys = new();

    private static string GetChannelPathKey(DataChannel channel)
        => _channelPathKeys.GetValue(channel, static c => string.Join('.', c.Path));

    private static readonly Comparison<DataChannel> _channelByPathComparer =
        (a, b) => string.CompareOrdinal(GetChannelPathKey(a), GetChannelPathKey(b));
}
