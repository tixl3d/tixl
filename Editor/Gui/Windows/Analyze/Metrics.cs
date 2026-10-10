#nullable enable
using System.Diagnostics;
using System.Globalization;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Animation;
using T3.Core.Stats;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;

namespace T3.Editor.Gui.Windows.Analyze;

internal static class T3Metrics
{
    /// <summary>
    /// Records the interval since the previous frame. Call once per frame after ImGui's delta time was set, independent
    /// of whether any metrics UI is drawn.
    /// </summary>
    public static void RecordFrameInterval()
    {
        PerformanceMetrics.RecordFrame(ImGui.GetIO().DeltaTime * 1000);
        FrameTiming.RecordFrameStart(Playback.RunTimeInSecs, T3Ui.UseVSync);
    }

    /// <summary>Starts timing the main thread's work for this frame.</summary>
    public static void CpuFrameStarted()
    {
        _cpuFrameStopwatch.Restart();
    }

    /// <summary>Stops timing the main thread's work; call just before presenting.</summary>
    public static void CpuFrameCompleted()
    {
        _cpuFrameStopwatch.Stop();
        _cpuFrameDurationMs = (float)((double)_cpuFrameStopwatch.ElapsedTicks / Stopwatch.Frequency * 1000.0);
        PerformanceMetrics.RecordCpuFrame(_cpuFrameDurationMs);
    }

    public static void DrawRenderPerformanceGraph()
    {
        float barHeight = 4;
        var offsetFromAppMenu = new Vector2(AppMenuBar.AppBarSpacingX,
                                            (int)((ImGui.GetFrameHeight() - barHeight) * 0.5f));
        var screenPosition = ImGui.GetCursorScreenPos() + offsetFromAppMenu;

        float barWidth = 100;
        float paddedBarWidth = barWidth + 30;
        ImGui.SameLine(0, offsetFromAppMenu.X);
        if (ImGui.InvisibleButton("performanceGraph", new Vector2(barWidth, ImGui.GetFrameHeight())))
        {
            WindowManager.ToggleInstanceVisibility<PerformanceWindow>();
        }

        // Glance tooltip — only when the full Performance window isn't already visible,
        // so you don't get a redundant overlay on top of the window.
        if (ImGui.IsItemHovered() && !WindowManager.IsAnyInstanceVisible<PerformanceWindow>())
        {
            CustomComponents.BeginTooltip(450);
            {
                ImGui.Dummy(new Vector2(250 * T3Ui.UiScaleFactor, 1));
                DrawDetailedView();
                ImGui.Spacing();
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextUnformatted("Click to open Performance window");
                ImGui.PopStyleColor();
                ImGui.PopFont();
            }
            CustomComponents.EndTooltip();
        }

        float normalFramerateLevelAt = 0.5f;
        var refreshPeriodMs = (float)(FrameTiming.RefreshPeriodSec * 1000);
        float frameTimingScaleFactor = barWidth / normalFramerateLevelAt / (float)FrameTiming.RefreshRate;

        var peakDecay = MathUtils.BlendForFrame(0.05f, FrameTiming.VisualDeltaSec);
        _peakCpuFrameDurationMs = _peakCpuFrameDurationMs > _cpuFrameDurationMs
                                      ? MathUtils.Lerp(_peakCpuFrameDurationMs, _cpuFrameDurationMs, peakDecay)
                                      : _cpuFrameDurationMs;

        var deltaTimeMs = ImGui.GetIO().DeltaTime * 1000;

        _peakDeltaTimeMs = _peakDeltaTimeMs > deltaTimeMs
                               ? MathUtils.Lerp(_peakDeltaTimeMs, deltaTimeMs, peakDecay)
                               : deltaTimeMs;

        var drawList = ImGui.GetWindowDrawList();

        // CPU time of the frame
        var cpuTimeWidth = (float)Math.Ceiling(_cpuFrameDurationMs * frameTimingScaleFactor).Clamp(0, paddedBarWidth);
        drawList.AddRectFilled(screenPosition, screenPosition + new Vector2(cpuTimeWidth, barHeight), ColorForCpuBar);

        // Rest of the frame interval
        var deltaTimeWidth = (deltaTimeMs * frameTimingScaleFactor - cpuTimeWidth).Clamp(0, paddedBarWidth);
        var renderBarPos = screenPosition + new Vector2(cpuTimeWidth, 0);
        drawList.AddRectFilled(renderBarPos, renderBarPos + new Vector2(deltaTimeWidth, barHeight), ColorForFramerateBar);

        // Peak CPU time
        var peakCpuTimePos = screenPosition + new Vector2((int)(_peakCpuFrameDurationMs * frameTimingScaleFactor).Clamp(0, paddedBarWidth), 0);
        drawList.AddRectFilled(peakCpuTimePos, peakCpuTimePos + new Vector2(2, barHeight), ColorForCpuBar);

        // Draw Peak Render Duration
        var peakDeltaTimePos = screenPosition + new Vector2((int)(_peakDeltaTimeMs * frameTimingScaleFactor).Clamp(0, paddedBarWidth), 0);
        drawList.AddRectFilled(peakDeltaTimePos, peakDeltaTimePos + new Vector2(2, barHeight), ColorForFramerateBar);

        // Mark one refresh period of the display
        var normalFramerateMarkerPos = screenPosition + new Vector2(refreshPeriodMs * frameTimingScaleFactor, 0);
        drawList.AddRectFilled(normalFramerateMarkerPos + new Vector2(0, -1), normalFramerateMarkerPos + new Vector2(1, barHeight + 1), ColorForCpuBar);
    }

    /// <summary>
    /// Body of the Performance window: labelled metric graphs, display and presentation info, and per-frame render stats.
    /// </summary>
    internal static void DrawDetailedView()
    {
        DrawCopyCsvButton();

        DrawLabeledGraph(PerformanceMetrics.FrameDuration, "Frame", FrameTooltip, "ms", 32f, FormatMs);
        ImGui.Separator();
        DrawLabeledGraph(PerformanceMetrics.CpuFrameDuration, "CPU", CpuTooltip, "ms", 32f, FormatMs);
        ImGui.Separator();
        DrawLabeledGraph(PerformanceMetrics.PresentDuration, "Present", PresentTooltip, "ms", 32f, FormatMs);
        ImGui.Separator();
        DrawLabeledGraph(PerformanceMetrics.GpuFrameDuration, "GPU", GpuTooltip, "ms", 32f, FormatMs);
        ImGui.Separator();
        DrawLabeledGraph(PerformanceMetrics.GcAllocationsKb, "Mem-Alloc", MemAllocTooltip, "", 10_000f, FormatKb);

        ImGui.TextUnformatted($"Render: {_peakDeltaTimeMs:0.0}ms");
        ImGui.TextUnformatted($"Display: {FrameTiming.RefreshRate:0.00} Hz measured");
        ImGui.TextUnformatted($"Presentation: {PresentationDiagnostics.Describe(PresentationDiagnostics.CompositionMode)}, "
                              + $"{PresentationDiagnostics.QueuedFrames} queued");

        ImGui.Spacing();

        ImGui.PushFont(Fonts.FontSmall);
        foreach (var (key, number) in RenderStatsCollector.ResultsForLastFrame)
        {
            var formattedNumber = number switch
                                      {
                                          > 1000000 => $"{number / 1000000.0:0.0}M",
                                          > 1000    => $"{number / 1000.0:0.0}K",
                                          _         => number.ToString()
                                      };
            ImGui.Text($"{formattedNumber} {key}");
        }
        ImGui.PopFont();
    }

    /// <summary>
    /// Small icon button at the top of the detailed view that copies the current window's samples
    /// to the clipboard as CSV. Intended for quickly sharing performance snapshots (e.g. pasting
    /// into a bug report or chat).
    /// </summary>
    private static void DrawCopyCsvButton()
    {
        var contentWidth = ImGui.GetContentRegionAvail().X;
        var btnSize = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + contentWidth - btnSize);
        if (CustomComponents.TransparentIconButton(Icon.CopyToClipboard, new Vector2(btnSize)))
        {
            ImGui.SetClipboardText(BuildCsvExport());
        }
        CustomComponents.TooltipForLastItem("Copy current performance window to clipboard as CSV.");
    }

    /// <summary>Builds a CSV snapshot of the current performance window across all metrics.</summary>
    private static string BuildCsvExport()
    {
        var frameScratch = new float[PerformanceMetrics.WindowSize];
        var cpuScratch = new float[PerformanceMetrics.WindowSize];
        var presentScratch = new float[PerformanceMetrics.WindowSize];
        var gpuScratch = new float[PerformanceMetrics.WindowSize];
        var allocScratch = new float[PerformanceMetrics.WindowSize];

        var frames = PerformanceMetrics.FrameDuration.AsOrderedSpan(frameScratch);
        var cpus = PerformanceMetrics.CpuFrameDuration.AsOrderedSpan(cpuScratch);
        var presents = PerformanceMetrics.PresentDuration.AsOrderedSpan(presentScratch);
        var gpus = PerformanceMetrics.GpuFrameDuration.AsOrderedSpan(gpuScratch);
        var allocs = PerformanceMetrics.GcAllocationsKb.AsOrderedSpan(allocScratch);

        var count = Math.Min(Math.Min(frames.Length, cpus.Length), Math.Min(presents.Length, allocs.Length));

        var sb = new System.Text.StringBuilder(count * 48 + 256);
        sb.Append("# TiXL Performance Export  ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.Append("# Window: ").Append(PerformanceMetrics.WindowSize).Append(" samples  Total frames: ").AppendLine(PerformanceMetrics.TotalFrameCount.ToString());
        sb.AppendLine("# Columns: frame_index, frame_ms, cpu_ms, present_ms, gpu_ms, alloc_kB");
        sb.AppendLine("# gpu_ms is its own sequence (reported a few frames late, dropped results skipped), not the GPU time of the frame on the same row.");
        sb.AppendLine("frame_index,frame_ms,cpu_ms,present_ms,gpu_ms,alloc_kB");

        for (var i = 0; i < count; i++)
        {
            sb.Append(i).Append(',')
              .Append(frames[i].ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
              .Append(cpus[i].ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
              .Append(presents[i].ToString("0.##", CultureInfo.InvariantCulture)).Append(',');
            if (i < gpus.Length)
                sb.Append(gpus[i].ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append(',').AppendLine(allocs[i].ToString("0.#", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Renders a labelled header line plus a histogram + plot-line graph for the given metric.
    /// Header has three columns: label (left) | ~average (centre) | max (right, muted).
    /// The plot line auto-scales its Y-axis to the current window's max; <paramref name="domainMax"/>
    /// is only used as the rightmost histogram axis tick (the fixed domain upper bound).
    /// </summary>
    private static void DrawLabeledGraph(RollingMetric metric, string label, string tooltip, string unit, float domainMax, Func<float, string> axisFormat)
    {
        if (metric.Count < 1)
            return;

        var contentWidth = ImGui.GetContentRegionAvail().X;
        var startPos = ImGui.GetCursorScreenPos();

        // Three-column header.
        var centerText = $"~{axisFormat(metric.Average)}{unit}";
        var rightText = $"{axisFormat(metric.Max)}{unit} max";

        ImGui.TextUnformatted(label);
        CustomComponents.TooltipForLastItem(tooltip);

        var centerSize = ImGui.CalcTextSize(centerText);
        ImGui.SameLine(0, 0);
        ImGui.SetCursorScreenPos(new Vector2(startPos.X + (contentWidth - centerSize.X) * 0.5f, startPos.Y));
        ImGui.TextUnformatted(centerText);

        var rightSize = ImGui.CalcTextSize(rightText);
        ImGui.SameLine(0, 0);
        ImGui.SetCursorScreenPos(new Vector2(startPos.X + contentWidth - rightSize.X, startPos.Y));
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(rightText);
        ImGui.PopStyleColor();

        // Graph area.
        var size = new Vector2(contentWidth, 60 * T3Ui.UiScaleFactor);
        var cursor = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);
        ImGui.Dummy(new Vector2(1, 6 * T3Ui.UiScaleFactor));

        var rect = new ImRect(cursor, cursor + size);
        var slots = metric.Slots;

        // Axis ticks describe the histogram's fixed x-domain — same value also drives the plot Y-scale
        // so the graph stays readable (a jumping auto-scaled max is harder to parse than a fixed one).
        var axisLeft = axisFormat(slots[0].ValueRangeMin);
        var axisMid = axisFormat(slots[slots.Length / 2].ValueRangeMin);
        var axisRight = axisFormat(domainMax);

        var drawList = ImGui.GetWindowDrawList();
        var hoveredBucket = MetricGraphView.DrawGraph(drawList, rect, metric, _floatGraphBuffer,
                                                      BarColor, FlashColor, LineColor,
                                                      PerformanceMetrics.Now, domainMax,
                                                      axisLeft, axisMid, axisRight);
        if (hoveredBucket >= 0)
            DrawBucketTooltip(label, unit, axisFormat, domainMax, metric, hoveredBucket);
    }

    /// <summary>
    /// Tooltip shown while hovering a histogram bucket. Shows count + share + estimated periodicity
    /// for the current window and all-time. Holding <c>Shift</c> switches to cumulative
    /// ("this bucket and everything higher") — useful for "how often do we see at least X".
    /// </summary>
    private static void DrawBucketTooltip(string label, string unit, Func<float, string> axisFormat,
                                          float domainMax, RollingMetric metric, int bucketIndex)
    {
        var slots = metric.Slots;
        var cumulative = ImGui.GetIO().KeyShift;

        int countRecent;
        long countTotal;
        if (cumulative)
        {
            countRecent = 0;
            countTotal = 0;
            for (var i = bucketIndex; i < slots.Length; i++)
            {
                countRecent += slots[i].CountRecent;
                countTotal += slots[i].CountTotal;
            }
        }
        else
        {
            countRecent = slots[bucketIndex].CountRecent;
            countTotal = slots[bucketIndex].CountTotal;
        }

        string rangeLabel;
        if (cumulative)
        {
            rangeLabel = $"≥ {axisFormat(slots[bucketIndex].ValueRangeMin)}{unit}";
        }
        else
        {
            var upperEdge = bucketIndex + 1 < slots.Length
                                ? slots[bucketIndex + 1].ValueRangeMin
                                : domainMax;
            rangeLabel = $"{axisFormat(slots[bucketIndex].ValueRangeMin)} .. {axisFormat(upperEdge)}{unit}";
        }

        ImGui.BeginTooltip();
        {
            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextUnformatted(label);
            ImGui.PopFont();

            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted($"({rangeLabel})");
            ImGui.PopStyleColor();

            ImGui.Separator();

            // Recent window.
            ImGui.TextUnformatted($"Last {PerformanceMetrics.WindowSize} Frames");
            var pctRecent = countRecent / (float)PerformanceMetrics.WindowSize * 100f;
            ImGui.TextUnformatted($"  {countRecent}×   {pctRecent:0.#}%");

            // Periodicity from inner span (first..last visible occurrence) — stays steady as ticks
            // scroll in and out of the window, unlike WindowSize/count which jumps in steps.
            if (TryComputeInnerSpanPeriod(metric, bucketIndex, cumulative, domainMax,
                                          out var periodFramesF, out var periodSeconds))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextUnformatted($"  ~ every {periodFramesF:0.#}F / {periodSeconds:0.00}s");
                ImGui.PopStyleColor();
            }

            // Per-frame occurrence strip: shows *when* in the window each hit happened.
            DrawOccurrenceBar(metric, bucketIndex, cumulative, domainMax);

            ImGui.Spacing();

            // All-time.
            var total = PerformanceMetrics.TotalFrameCount;
            ImGui.TextUnformatted($"Total ({total:N0} Frames)");
            if (total > 0)
            {
                var pctTotal = countTotal / (float)total * 100f;
                ImGui.TextUnformatted($"  {countTotal}×   {pctTotal:0.##}%");
            }

            ImGui.Spacing();
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted(cumulative ? "(release Shift for single bucket)" : "(hold Shift for cumulative)");
            ImGui.PopStyleColor();
            ImGui.PopFont();
        }
        ImGui.EndTooltip();
    }

    /// <summary>
    /// Estimates the typical period between hits in the hovered bucket(s) using the *inner span* —
    /// distance between the first and last visible occurrence — instead of <c>WindowSize / count</c>.
    /// This is stable as occurrences scroll in and out at the buffer edges; the naive formula jumps
    /// in coarse steps and visibly flickers for short periods.
    ///
    /// Returns false when fewer than two occurrences are present (no meaningful period available).
    /// </summary>
    private static bool TryComputeInnerSpanPeriod(RollingMetric metric, int bucketIndex, bool cumulative,
                                                  float domainMax, out float periodFrames, out float periodSeconds)
    {
        var slots = metric.Slots;
        var lowerEdge = slots[bucketIndex].ValueRangeMin;
        float upperEdge;
        if (cumulative)
        {
            upperEdge = float.PositiveInfinity;
        }
        else
        {
            upperEdge = bucketIndex + 1 < slots.Length
                            ? slots[bucketIndex + 1].ValueRangeMin
                            : domainMax;
        }

        var samples = metric.AsOrderedSpan(_floatGraphBuffer);
        var firstIdx = -1;
        var lastIdx = -1;
        var matchCount = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var v = samples[i];
            if (v < lowerEdge || v >= upperEdge)
                continue;
            if (firstIdx < 0)
                firstIdx = i;
            lastIdx = i;
            matchCount++;
        }

        if (matchCount < 2)
        {
            periodFrames = 0f;
            periodSeconds = 0f;
            return false;
        }

        periodFrames = (lastIdx - firstIdx) / (float)(matchCount - 1);
        var avgFrameMs = PerformanceMetrics.FrameDuration.Average;
        periodSeconds = periodFrames * avgFrameMs / 1000f;
        return true;
    }

    /// <summary>
    /// Draws a compact horizontal strip inside the tooltip where every tick marks a frame in the
    /// current window whose sample fell into the hovered bucket (or any higher bucket when cumulative).
    /// Makes the temporal pattern of allocations (periodic / clustered / one-off) visible at a glance.
    /// Scans the value buffer directly — matching against bucket value-range bounds — so the
    /// hot Metrics API stays lean.
    /// </summary>
    private static void DrawOccurrenceBar(RollingMetric metric, int bucketIndex, bool cumulative, float domainMax)
    {
        var barWidth = 220f * T3Ui.UiScaleFactor;
        var barHeight = 10f * T3Ui.UiScaleFactor;

        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(barWidth, barHeight));
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(pos, pos + new Vector2(barWidth, barHeight), UiColors.BackgroundFull.Fade(0.8f));

        var samples = metric.AsOrderedSpan(_floatGraphBuffer);
        var count = samples.Length;
        if (count < 2)
            return;

        var slots = metric.Slots;
        var lowerEdge = slots[bucketIndex].ValueRangeMin;
        float upperEdge;
        if (cumulative)
        {
            upperEdge = float.PositiveInfinity;
        }
        else
        {
            upperEdge = bucketIndex + 1 < slots.Length
                            ? slots[bucketIndex + 1].ValueRangeMin
                            : domainMax;
        }

        var step = barWidth / (count - 1);
        uint tickColor = UiColors.ForegroundFull;
        var yTop = pos.Y + 1f;
        var yBottom = pos.Y + barHeight - 1f;
        for (var i = 0; i < count; i++)
        {
            var v = samples[i];
            if (v < lowerEdge || v >= upperEdge)
                continue;
            var x = pos.X + i * step;
            drawList.AddLine(new Vector2(x, yTop), new Vector2(x, yBottom), tickColor, 1f);
        }
    }

    private static string FormatMs(float ms) => $"{ms:0.#}";

    /// <summary>Formats a kilobyte count using K / MB / GB suffixes (e.g. 100 → "100K", 1500 → "1.5MB").</summary>
    private static string FormatKb(float kb)
    {
        if (kb < 1000f)
            return $"{kb:0}K";
        if (kb < 1_000_000f)
            return $"{kb / 1000f:0.#}MB";
        return $"{kb / 1_000_000f:0.#}GB";
    }

    private static uint ColorForCpuBar => UiColors.ForegroundFull.Fade(0.4f);
    private static uint ColorForFramerateBar => UiColors.ForegroundFull.Fade(0.1f);

    private static  Color BarColor => UiColors.ForegroundFull.Fade(0.1f);
    private static  Color FlashColor => UiColors.ForegroundFull.Fade(0.5f);
    private static  Color LineColor => UiColors.ForegroundFull.Fade(0.5f);

    private const string FrameTooltip = "Time between the starts of two frames. With vsync it settles at the display's refresh period.";

    private const string CpuTooltip =
        "Main-thread time per frame: evaluating the graph, building the UI and issuing GPU commands. "
        + "Doesn't include the GPU executing them, unless the CPU had to wait for the GPU (e.g. reading back a texture). "
        + "Close to the frame budget means CPU-bound.";

    private const string PresentTooltip =
        "Time spent handing the frame to the displays: waiting for a vblank or a free back buffer. "
        + "With vsync and a light scene, the frame's spare time ends up here.";

    private const string GpuTooltip =
        "GPU time per frame, from timestamps around the frame's commands. Reported a few frames late; "
        + "includes GPU idle gaps while the CPU is still submitting. Close to the frame budget means GPU-bound.";

    private const string MemAllocTooltip = "Managed memory allocated per frame. Steady allocations lead to garbage-collection pauses.";

    private static float _peakCpuFrameDurationMs;
    private static float _peakDeltaTimeMs;
    private static float _cpuFrameDurationMs;
    private static readonly Stopwatch _cpuFrameStopwatch = new();

    // Scratch buffer for plot-line copy-out (and reused by the tooltip's occurrence strip).
    // Sized to match PerformanceMetrics.WindowSize.
    private static readonly float[] _floatGraphBuffer = new float[PerformanceMetrics.WindowSize];
}
