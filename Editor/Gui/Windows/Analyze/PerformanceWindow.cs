#nullable enable

namespace T3.Editor.Gui.Windows.Analyze;

/// <summary>
/// Toggleable window showing live frame-time, draw-duration, and GC-allocation graphs
/// plus the per-frame render stats. Toggled by clicking the mini graph in the app bar.
/// </summary>
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
        T3Metrics.DrawDetailedView();
    }

    internal override List<Window> GetInstances() => [];
}
