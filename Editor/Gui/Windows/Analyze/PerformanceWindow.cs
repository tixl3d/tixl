#nullable enable

using T3.Core.Stats;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.Analyze;

/// <summary>
/// Toggleable window showing live frame-time, draw-duration, and GC-allocation graphs
/// plus the per-frame render stats. Toggled by clicking the mini graph in the app bar.
/// </summary>
[HelpUiID("PerformanceMonitor")]
internal sealed class PerformanceWindow : Window
{
    public PerformanceWindow()
    {
        Config.Title = "Performance";
        MenuTitle = "Performance";
        WindowPaddingOverride = new Vector2(7);
    }

    protected override void DrawContent()
    {
        DrawPresentationSettings();
        T3Metrics.DrawDetailedView();
    }

    /// <summary>The vsync and frame-rate settings, shared with the settings window.</summary>
    internal static bool DrawPresentationSettings()
    {
        var changed = FormInputs.AddCheckBox("Use VSync",
                                             ref UserSettings.Config.UseVSync,
                                             "Synchronise frame presentation to the display refresh rate. Turn off to uncap the frame rate (useful for benchmarking; can cause tearing).",
                                             UserSettings.Defaults.UseVSync);
        FormInputs.SetIndentToParameters();
        changed |= FormInputs.AddDropdown(ref UserSettings.Config.TargetFrameRate,
                                          TargetFrameRates,
                                          "Frame Rate",
                                          FormatTargetFrameRate,
                                          """
                                          Renders every refresh of the pacing display, or holds each frame for whole refreshes
                                          to approach a lower rate: 60 fps on a 120 Hz display gives a 16.7 ms frame budget.
                                          Rates the display can't divide into round to the nearest one it can.
                                          Needs vsync; the pacing display is the first bound output, otherwise the editor window.
                                          """);
        changed |= FormInputs.AddDropdown(ref UserSettings.Config.FrameLatency,
                                          FrameLatencies,
                                          "Frame Latency",
                                          FormatFrameLatency,
                                          """
                                          Low latency lets CPU and GPU work on one frame at a time: the shortest delay between
                                          input and picture, but when a frame misses its refresh (screen capture, streaming,
                                          a heavy scene) the frame rate drops to half.
                                          Smooth keeps one more frame in flight, like the Vulkan backend: one refresh more delay,
                                          but a late frame no longer costs a whole refresh.
                                          """);
        return changed;
    }

    private static string FormatTargetFrameRate(int framesPerSecond)
    {
        return framesPerSecond switch
                   {
                       0   => GetMatchDisplayLabel(),
                       120 => "120 fps",
                       60  => "60 fps",
                       50  => "50 fps",
                       30  => "30 fps",
                       _   => "Custom"
                   };
    }

    /** Rebuilt only when the rounded rate changes, so drawing the dropdown doesn't allocate every frame. */
    private static string GetMatchDisplayLabel()
    {
        var refreshRate = (int)Math.Round(FrameTiming.RefreshRate);
        if (refreshRate != _matchDisplayLabelRate)
        {
            _matchDisplayLabelRate = refreshRate;
            _matchDisplayLabel = $"Match Display ({refreshRate} Hz)";
        }

        return _matchDisplayLabel;
    }

    private static string FormatFrameLatency(int frames) => frames <= 1 ? "Low latency (1 frame)" : "Smooth (2 frames)";

    private static readonly int[] TargetFrameRates = [0, 120, 60, 50, 30];
    private static readonly int[] FrameLatencies = [1, 2];
    private static int _matchDisplayLabelRate = -1;
    private static string _matchDisplayLabel = "Match Display";

    internal override List<Window> GetInstances() => [];
}
