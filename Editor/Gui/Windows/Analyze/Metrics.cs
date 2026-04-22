#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Stats;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

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
            T3Ui.UseVSync = !T3Ui.UseVSync;
        }

        if (ImGui.IsItemHovered())
        {
            CustomComponents.BeginTooltip(450*T3Ui.UiScaleFactor);
            {
                ImGui.Dummy(new Vector2( 250 * T3Ui.UiScaleFactor,1));
                DrawLabeledGraph(PerformanceMetrics.FrameDuration, "Frame", "ms", 32f, FormatMs);
                ImGui.Separator();
                DrawLabeledGraph(PerformanceMetrics.UiRenderDuration, "Draw", "ms", 32f, FormatMs);
                ImGui.Separator();
                DrawLabeledGraph(PerformanceMetrics.GcAllocationsKb, "Mem-Alloc", "", 10_000f, FormatKb);

                ImGui.TextUnformatted($"""
                            Render: {_peakDeltaTimeMs:0.0}ms
                            VSync: {(T3Ui.UseVSync ? "On" : "Off")} (Click to toggle)
                            """);

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

        // Axis ticks describe the histogram's fixed x-domain — not the plot line's Y-scale.
        var axisLeft = axisFormat(slots[0].ValueRangeMin);
        var axisMid = axisFormat(slots[slots.Length / 2].ValueRangeMin);
        var axisRight = axisFormat(domainMax);

        // Plot line Y-scale tracks the window's current max so the graph always uses its full vertical range.
        var plotMaxY = metric.Max;

        var drawList = ImGui.GetWindowDrawList();
        MetricGraphView.DrawGraph(drawList, rect, metric, _floatGraphBuffer,
                                  BarColor, FlashColor, LineColor,
                                  PerformanceMetrics.Now, domainMax,
                                  axisLeft, axisMid, axisRight);
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

    // Scratch buffer for plot-line copy-out. Sized to match PerformanceMetrics.WindowSize.
    private static readonly float[] _floatGraphBuffer = new float[PerformanceMetrics.WindowSize];
}
