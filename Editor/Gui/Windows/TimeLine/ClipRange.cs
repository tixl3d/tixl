using ImGuiNET;
using T3.Core.Animation;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Visualizes the mapped time area within a <see cref="TimeClip"/> content by shading everything
/// outside the entered clip instance's SourceRange. Pure display — the range is edited from the
/// parent timeline (slip drag in the ruler) or via the symbol's authored source extent
/// (<see cref="SourceExtentEditor"/>); the former in-place Alt-drag manipulation had no undo.
/// The range boundaries stay registered as snap anchors.
/// </summary>
internal sealed class ClipRange : IValueSnapAttractor
{
    public void Draw(TimeLineCanvas canvas, TimeClip timeClip, ImDrawListPtr drawlist)
    {
        if (timeClip == null)
            return;

        _timeClip = timeClip;

        // Range start
        {
            var xRangeStart = canvas.TransformX(timeClip.SourceRange.Start);
            var rangeStartPos = new Vector2(xRangeStart, 0);

            // Shade outside
            drawlist.AddRectFilled(
                                   new Vector2(0, 0),
                                   new Vector2(xRangeStart, _timeRangeShadowSize.Y),
                                   _timeRangeOutsideColor);

            // Shadow
            drawlist.AddRectFilled(
                                   rangeStartPos - new Vector2(_timeRangeShadowSize.X - 1, 0),
                                   rangeStartPos + new Vector2(0, _timeRangeShadowSize.Y),
                                   _timeRangeShadowColor);

            // Line
            drawlist.AddRectFilled(rangeStartPos, rangeStartPos + new Vector2(1, 9999), _timeRangeShadowColor);
        }

        // Range end
        {
            var rangeEndX = canvas.TransformX(timeClip.SourceRange.End);
            var rangeEndPos = new Vector2(rangeEndX, 0);

            // Shade outside
            var windowMaxX = ImGui.GetContentRegionAvail().X + canvas.WindowPos.X;
            if (rangeEndX < windowMaxX)
                drawlist.AddRectFilled(
                                       rangeEndPos,
                                       rangeEndPos + new Vector2(windowMaxX - rangeEndX, _timeRangeShadowSize.Y),
                                       _timeRangeOutsideColor);

            // Shadow
            drawlist.AddRectFilled(
                                   rangeEndPos + new Vector2(1, 0),
                                   rangeEndPos + _timeRangeShadowSize,
                                   _timeRangeShadowColor);

            // Line
            drawlist.AddRectFilled(rangeEndPos, rangeEndPos + new Vector2(1, 9999), _timeRangeShadowColor);
        }
    }

    private static readonly Vector2 _timeRangeShadowSize = new(1, 9999);
    private static readonly Color _timeRangeShadowColor = UiColors.StatusAnimated.Fade(0.2f);
    private static readonly Color _timeRangeOutsideColor = UiColors.StatusAnimated.Fade(0.1f);

    private static TimeClip _timeClip;

    #region implement snapping interface -----------------------------------
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        if (_timeClip == null)
            return;

        snapResult.TryToImproveWithAnchorValue(_timeClip.SourceRange.Start);
        snapResult.TryToImproveWithAnchorValue(_timeClip.SourceRange.End);
    }
    #endregion
}
