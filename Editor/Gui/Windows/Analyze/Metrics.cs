#nullable enable
using System.Diagnostics;
using System.Globalization;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Stats;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;

namespace T3.Editor.Gui.Windows.Analyze;

internal static class T3Metrics
{
    public static void UiRenderingStarted()
    {
        _watchImgRenderTime.Restart();
        _watchImgRenderTime.Start();
    }

    public static void UiRenderingCompleted()
    {
        _watchImgRenderTime.Stop();
        _uiRenderDurationMs = (float)((double)_watchImgRenderTime.ElapsedTicks / Stopwatch.Frequency * 1000.0);
        PerformanceMetrics.RecordUiRender(_uiRenderDurationMs);
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
            CustomComponents.BeginTooltip(450 * T3Ui.UiScaleFactor);
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
        float frameTimingScaleFactor = barWidth / normalFramerateLevelAt / ExpectedFramerate;

        _uiSmoothedRenderDurationMs = MathUtils.Lerp(_uiSmoothedRenderDurationMs, _uiRenderDurationMs, 0.05f);

        _peakUiRenderDurationMs = _peakUiRenderDurationMs > _uiRenderDurationMs
                                      ? MathUtils.Lerp(_peakUiRenderDurationMs, _uiRenderDurationMs, 0.05f)
                                      : _uiRenderDurationMs;

        var deltaTimeMs = ImGui.GetIO().DeltaTime * 1000;
        PerformanceMetrics.RecordFrame(deltaTimeMs);

        _peakDeltaTimeMs = _peakDeltaTimeMs > deltaTimeMs
                               ? MathUtils.Lerp(_peakDeltaTimeMs, deltaTimeMs, 0.05f)
                               : deltaTimeMs;

        var drawList = ImGui.GetWindowDrawList();

        // Draw Ui Render Duration
        var uiTimeWidth = (float)Math.Ceiling(_uiRenderDurationMs * frameTimingScaleFactor).Clamp(0, paddedBarWidth);
        drawList.AddRectFilled(screenPosition, screenPosition + new Vector2(uiTimeWidth, barHeight), ColorForUiBar);

        // Draw Frame Render Duration
        var deltaTimeWidth = (deltaTimeMs * frameTimingScaleFactor - uiTimeWidth).Clamp(0, paddedBarWidth);
        var renderBarPos = screenPosition + new Vector2(uiTimeWidth, 0);
        drawList.AddRectFilled(renderBarPos, renderBarPos + new Vector2(deltaTimeWidth, barHeight), ColorForFramerateBar);

        // Draw Peak UI Duration
        var peakUiTimePos = screenPosition + new Vector2((int)(_peakUiRenderDurationMs * frameTimingScaleFactor).Clamp(0, paddedBarWidth), 0);
        drawList.AddRectFilled(peakUiTimePos, peakUiTimePos + new Vector2(2, barHeight), ColorForUiBar);

        // Draw Peak Render Duration
        var peakDeltaTimePos = screenPosition + new Vector2((int)(_peakDeltaTimeMs * frameTimingScaleFactor).Clamp(0, paddedBarWidth), 0);
        drawList.AddRectFilled(peakDeltaTimePos, peakDeltaTimePos + new Vector2(2, barHeight), ColorForFramerateBar);

        // Draw 60fps mark
        var normalFramerateMarkerPos = screenPosition + new Vector2(ExpectedFrameDurationMs * frameTimingScaleFactor, 0);
        drawList.AddRectFilled(normalFramerateMarkerPos + new Vector2(0, -1), normalFramerateMarkerPos + new Vector2(1, barHeight + 1), ColorForUiBar);
    }

    /// <summary>
    /// Body of the Performance window: three labelled metric graphs, render summary, and per-frame render stats.
    /// </summary>
    internal static void DrawDetailedView()
    {
        DrawCopyCsvButton();

        DrawLabeledGraph(PerformanceMetrics.FrameDuration, "Frame", "ms", 32f, FormatMs);
        ImGui.Separator();
        DrawLabeledGraph(PerformanceMetrics.UiRenderDuration, "Draw", "ms", 32f, FormatMs);
        ImGui.Separator();
        DrawLabeledGraph(PerformanceMetrics.GcAllocationsKb, "Mem-Alloc", "", 10_000f, FormatKb);

        ImGui.TextUnformatted($"Render: {_peakDeltaTimeMs:0.0}ms");

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

    /// <summary>Builds a CSV snapshot of the current performance window across all three metrics.</summary>
    private static string BuildCsvExport()
    {
        var frameScratch = new float[PerformanceMetrics.WindowSize];
        var drawScratch = new float[PerformanceMetrics.WindowSize];
        var allocScratch = new float[PerformanceMetrics.WindowSize];

        var frames = PerformanceMetrics.FrameDuration.AsOrderedSpan(frameScratch);
        var draws = PerformanceMetrics.UiRenderDuration.AsOrderedSpan(drawScratch);
        var allocs = PerformanceMetrics.GcAllocationsKb.AsOrderedSpan(allocScratch);

        var count = Math.Min(Math.Min(frames.Length, draws.Length), allocs.Length);

        var sb = new System.Text.StringBuilder(count * 32 + 256);
        sb.Append("# TiXL Performance Export  ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.Append("# Window: ").Append(PerformanceMetrics.WindowSize).Append(" samples  Total frames: ").AppendLine(PerformanceMetrics.TotalFrameCount.ToString());
        sb.AppendLine("# Columns: frame_index, frame_ms, draw_ms, alloc_kB");
        sb.AppendLine("frame_index,frame_ms,draw_ms,alloc_kB");

        for (var i = 0; i < count; i++)
        {
            sb.Append(i).Append(',')
              .Append(frames[i].ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
              .Append(draws[i].ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
              .AppendLine(allocs[i].ToString("0.#", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Renders a labelled header line plus a histogram + plot-line graph for the given metric.
    /// Header has three columns: label (left) | ~average (centre) | max (right, muted).
    /// The plot line auto-scales its Y-axis to the current window's max; <paramref name="domainMax"/>
    /// is only used as the rightmost histogram axis tick (the fixed domain upper bound).
    /// </summary>
    private static void DrawLabeledGraph(RollingMetric metric, string label, string unit, float domainMax, Func<float, string> axisFormat)
    {
        if (metric.Count < 1)
            return;

        var contentWidth = ImGui.GetContentRegionAvail().X;
        var startPos = ImGui.GetCursorScreenPos();

        // Three-column header.
        var centerText = $"~{axisFormat(metric.Average)}{unit}";
        var rightText = $"{axisFormat(metric.Max)}{unit} max";

        ImGui.TextUnformatted(label);

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

    private static uint ColorForUiBar => UiColors.ForegroundFull.Fade(0.4f);
    private static uint ColorForFramerateBar => UiColors.ForegroundFull.Fade(0.1f);

    private static  Color BarColor => UiColors.ForegroundFull.Fade(0.1f);
    private static  Color FlashColor => UiColors.ForegroundFull.Fade(0.5f);
    private static  Color LineColor => UiColors.ForegroundFull.Fade(0.5f);

    private const float ExpectedFramerate = 60;
    private const float ExpectedFrameDurationMs = 1 / ExpectedFramerate * 1000;

    private static float _peakUiRenderDurationMs;
    private static float _peakDeltaTimeMs;
    private static float _uiRenderDurationMs;
    private static float _uiSmoothedRenderDurationMs;
    private static readonly Stopwatch _watchImgRenderTime = new();

    // Scratch buffer for plot-line copy-out (and reused by the tooltip's occurrence strip).
    // Sized to match PerformanceMetrics.WindowSize.
    private static readonly float[] _floatGraphBuffer = new float[PerformanceMetrics.WindowSize];
}
