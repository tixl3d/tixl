using ImGuiNET;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.TimeLine;

internal sealed class CurrentTimeMarker: IValueSnapAttractor
{
    public void Draw(double timeInBars, TimeLineCanvas timelineCanvas)
    {
        _currentTimeInBars = timeInBars;

        var p = new Vector2(timelineCanvas.TransformX((float)timeInBars), 0);
        var drawList = ImGui.GetWindowDrawList();
        var y = ImGui.GetWindowPos().Y;
        var windowHeight = ImGui.GetWindowHeight() +1;
        drawList.AddRectFilled(p + new Vector2(-1,y), p + new Vector2(2, windowHeight), UiColors.BackgroundFull.Fade(0.2f));
        drawList.AddRectFilled(p, p + new Vector2(1, y+ windowHeight), UiColors.StatusAnimated);
        Icons.DrawIconAtScreenPosition(Icon.CurrentTimeMarkerHandle, p+ new Vector2(-4,y-1));
    }

    /// <summary>
    /// Draws just the vertical line into the current (child) window's draw list. Called between the clip
    /// area and the dope sheet so the playhead renders above timeline clips but below keyframes.
    /// </summary>
    public void DrawLineInCurrentWindow(double timeInBars, TimeLineCanvas timelineCanvas)
    {
        var x = timelineCanvas.TransformX((float)timeInBars);
        var drawList = ImGui.GetWindowDrawList();
        var top = ImGui.GetWindowPos().Y;
        var bottom = top + ImGui.GetWindowHeight() + 1;
        drawList.AddRectFilled(new Vector2(x - 1, top), new Vector2(x + 2, bottom), UiColors.BackgroundFull.Fade(0.2f));
        drawList.AddRectFilled(new Vector2(x, top), new Vector2(x + 1, bottom), UiColors.StatusAnimated);
    }

    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        snapResult.TryToImproveWithAnchorValue(_currentTimeInBars);
    }
        
    private double _currentTimeInBars;
}