#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// The timeline as a dockable window. Like the parameter window it is shared by all graph windows and
/// shows the timeline of the focused one. While it is open, graph windows don't draw their own timeline.
/// </summary>
internal sealed class TimelineWindow : Window
{
    internal TimelineWindow()
    {
        Config.Title = "Timeline";
        WindowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        WindowPaddingOverride = Vector2.Zero;
    }

    internal bool IsDetached => Config.Visible;

    internal override IReadOnlyList<Window> GetInstances() => [];

    protected override void DrawContent()
    {
        // View > Timeline and Toggle All UI only affect the timeline below the graph: this window is
        // shown or hidden as a whole.
        var projectView = ProjectView.Focused;
        var composition = projectView?.CompositionInstance;
        if (projectView == null || composition == null)
        {
            CustomComponents.EmptyWindowMessage("Open a project to see its timeline.");
            return;
        }

        TimelineToolbar.DrawAsWindowHeader(projectView, composition);

        ImGui.BeginChild("##timeline", Vector2.Zero, ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBackground);
        projectView.TimeLineCanvas.Draw(projectView, Playback.Current);
        ImGui.EndChild();
    }
}
