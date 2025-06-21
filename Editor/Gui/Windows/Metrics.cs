#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows;

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
        _uiRenderDurations.Enqueue(_uiRenderDurationMs);

        // Collect GC
        var currentGCCount = GC.GetTotalAllocatedBytes(); // Gen 0 collections
        _gcAllocationsLastFrame = currentGCCount - _totalGCAllocations;
        _gcAllocationsInKb.Enqueue((float)(_gcAllocationsLastFrame / 1024.0));

        _totalGCAllocations = currentGCCount;
    }

    public static void DrawRenderPerformanceGraph()
    {
        const float barHeight = 4;
        var offsetFromAppMenu = new Vector2(AppMenuBar.AppBarSpacingX, 
                                            (int)((ImGui.GetFrameHeight() - barHeight)*0.5f));
        var screenPosition = ImGui.GetCursorScreenPos() + offsetFromAppMenu;

        const float barWidth = 100;
        float paddedBarWidth = barWidth + 30;
        ImGui.SameLine(0, offsetFromAppMenu.X);
        if (ImGui.InvisibleButton("performanceGraph", new Vector2(barWidth, ImGui.GetFrameHeight())))
        {
            T3Ui.UseVSync = !T3Ui.UseVSync;
        }

        if (ImGui.IsItemHovered())
        {
            CustomComponents.BeginTooltip();
            {
                DrawLabeledGraph(_frameDurations, "Frame","ms", 30);
                DrawLabeledGraph(_uiRenderDurations, "UI","ms", 30);                
                DrawLabeledGraph(_gcAllocationsInKb, "GC","kB", 400);
                
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

        const float normalFramerateLevelAt = 0.5f;
        const float frameTimingScaleFactor = barWidth / normalFramerateLevelAt / ExpectedFramerate;

        _uiSmoothedRenderDurationMs = MathUtils.Lerp(_uiSmoothedRenderDurationMs, _uiRenderDurationMs, 0.05f);

        _peakUiRenderDurationMs = _peakUiRenderDurationMs > _uiRenderDurationMs
                                      ? MathUtils.Lerp(_peakUiRenderDurationMs, _uiRenderDurationMs, 0.05f)
                                      : _uiRenderDurationMs;

        var deltaTimeMs = ImGui.GetIO().DeltaTime * 1000;
        _frameDurations.Enqueue(deltaTimeMs);
        
        // avoid jittering
        // if (deltaTimeMs > ExpectedFrameDurationMs * 0.8f && deltaTimeMs < ExpectedFrameDurationMs * 1.25f)
        // {
        //     deltaTimeMs = ExpectedFrameDurationMs;
        // }
        

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

        // ImGui.PushFont(Fonts.FontSmall);
        // drawList.AddText(screenPosition + new Vector2(0, 4), ColorForFramerateBar, $"{deltaTimeMs:0.0}ms");
        // ImGui.PopFont();
    }

    private static void DrawLabeledGraph(CircularBuffer<float> history, string label, string unit, float max)
    {
        if (history.Count < 1)
            return;
        
        history.CopyTo(_floatGraphBuffer);
        var average = _floatGraphBuffer.Average();
        var min = _floatGraphBuffer.Min();
        var jitter = _floatGraphBuffer.Max() - min;
        var last = _floatGraphBuffer[^1];
        ImGui.TextUnformatted($"{label}: {last:0.0}{unit} (~{average:0.0}  [{min:0.0} ..  +{jitter:0.0}])");
        ImGui.PlotLines("##uiDurations", ref _floatGraphBuffer[0], _uiRenderDurations.Count, 0,
                        null,
                        0.00f, max
                       );
    }

    private static uint ColorForUiBar => UiColors.ForegroundFull.Fade(0.4f);
    private static uint ColorForFramerateBar => UiColors.ForegroundFull.Fade(0.1f);
    private const float ExpectedFramerate = 60;
    private const float ExpectedFrameDurationMs = 1 / ExpectedFramerate * 1000;

    private static float _peakUiRenderDurationMs;
    private static float _peakDeltaTimeMs;
    private static long _totalGCAllocations = 0;
    private static long _gcAllocationsLastFrame;

    private static float _uiRenderDurationMs;
    private static float _uiSmoothedRenderDurationMs;
    private static readonly Stopwatch _watchImgRenderTime = new();
    private const int BufferSize = 100;
    private static readonly float[] _floatGraphBuffer = new float[BufferSize]; // reusable to avoid allocations
    private static readonly CircularBuffer<float> _uiRenderDurations = new(BufferSize);
    private static readonly CircularBuffer<float> _frameDurations = new(BufferSize);
    private static readonly CircularBuffer<float> _gcAllocationsInKb = new(BufferSize);
}