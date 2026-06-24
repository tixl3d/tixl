#nullable enable

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
        FormInputs.AddCheckBox("Use VSync",ref UserSettings.Config.UseVSync,
                               "Synchronise frame presentation to the display refresh rate. Turn off to uncap the frame rate (useful for benchmarking; can cause tearing).");
        T3Metrics.DrawDetailedView();
    }

    internal override List<Window> GetInstances() => [];
}
