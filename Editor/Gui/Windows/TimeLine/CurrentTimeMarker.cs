using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Windows.TimeLine.TimeClips;

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
        var fade = MarkerFade;
        drawList.AddRectFilled(p + new Vector2(-1,y), p + new Vector2(2, windowHeight), UiColors.BackgroundFull.Fade(0.2f * fade));
        drawList.AddRectFilled(p, p + new Vector2(1, y+ windowHeight), UiColors.StatusAnimated.Fade(fade));

        // Centre on the line using the glyph's real width: hi-dpi icon atlases make it wider than at 1x.
        Icons.GetGlyphDefinition(Icon.CurrentTimeMarkerHandle, out _, out var handleSize);
        var handleX = MathF.Floor(p.X + 0.5f - handleSize.X / 2);
        Icons.DrawIconAtScreenPosition(Icon.CurrentTimeMarkerHandle, new Vector2(handleX, y - 1), drawList, Color.White.Fade(fade));
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
        var fade = MarkerFade;
        drawList.AddRectFilled(new Vector2(x - 1, top), new Vector2(x + 2, bottom), UiColors.BackgroundFull.Fade(0.2f * fade));
        drawList.AddRectFilled(new Vector2(x, top), new Vector2(x + 1, bottom), UiColors.StatusAnimated.Fade(fade));
    }

    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        snapResult.TryToImproveWithAnchorValue(_currentTimeInBars);
    }
        
    /** Dimmed while the output previews a trimmed clip edge instead of the playhead time. */
    private static float MarkerFade => TrimEdgePreview.IsActive ? 0.35f : 1f;

    private double _currentTimeInBars;
}