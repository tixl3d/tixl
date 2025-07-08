using ImGuiNET;
using SharpDX;
using T3.Core.Animation;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.TimeLine;

internal sealed class ClipRange : IValueSnapAttractor
{
    /// <summary>
    /// Visualizes the mapped time area within a <see cref="TimeClip"/> content  
    /// </summary>
    public void Draw(TimeLineCanvas canvas, TimeClip timeClip, ImDrawListPtr drawlist, ValueSnapHandler snapHandler)
    {
        if (timeClip == null)
            return;

        _timeClip = timeClip;

        ImGui.PushStyleColor(ImGuiCol.Button, _timeRangeMarkerColor.Rgba);
        var manipulationEnabled = ImGui.GetIO().KeyAlt;

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

            if (manipulationEnabled)
            {
                SetCursorToBottom(
                                  xRangeStart - _timeRangeHandleSize.X,
                                  _timeRangeHandleSize.Y);

                ImGui.Button("##StartPos", _timeRangeHandleSize);

                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    var newTime = canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
                    if (snapHandler.TryCheckForSnapping(newTime, out var snappedTime, canvas.Scale.X, 
                                                            [(IValueSnapAttractor)this]))
                    {
                        newTime = (float)snappedTime;
                    }
                    var delta = newTime - timeClip.SourceRange.Start;
                    var speed = timeClip.TimeRange.Duration / timeClip.SourceRange.Duration;
                    timeClip.SourceRange.Start = newTime;
                    timeClip.TimeRange.Start += delta * speed;
                }
            }
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
                                   rangeEndPos + new Vector2(1,0),
                                   rangeEndPos + _timeRangeShadowSize,
                                   _timeRangeShadowColor);

            // Line
            drawlist.AddRectFilled(rangeEndPos, rangeEndPos + new Vector2(1, 9999), _timeRangeShadowColor);

            if (manipulationEnabled)
            {
                SetCursorToBottom(
                                  rangeEndX,
                                  _timeRangeHandleSize.Y);

                ImGui.Button("##EndPos", _timeRangeHandleSize);

                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    var newTime = canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
                    if (snapHandler.TryCheckForSnapping(newTime, out var snappedTime, canvas.Scale.X, [this]))
                    {
                        newTime = (float)snappedTime;
                    }
                    
                    var delta = newTime - timeClip.SourceRange.End;
                    var speed = timeClip.TimeRange.Duration / timeClip.SourceRange.Duration;
                    timeClip.SourceRange.End = newTime;
                    timeClip.TimeRange.End += delta * speed;
                }
            }
        }

        ImGui.PopStyleColor();
    }

    private static void SetCursorToBottom(float xInScreen, float paddingFromBottom)
    {
        var max = ImGui.GetWindowContentRegionMax() + ImGui.GetWindowPos();
        var p = new Vector2(xInScreen, max.Y - paddingFromBottom);
        ImGui.SetCursorScreenPos(p);
    }

    private static readonly Vector2 _timeRangeHandleSize = new(10, 20);
    private static readonly Vector2 _timeRangeShadowSize = new(1, 9999);
    private static readonly Color _timeRangeShadowColor = UiColors.StatusAnimated.Fade(0.2f);
    private static readonly Color _timeRangeOutsideColor = UiColors.StatusAnimated.Fade(0.1f);
    private static readonly Color _timeRangeMarkerColor = UiColors.StatusAnimated.Fade(0.5f);

    //private static Playback _playback;
    private static TimeClip _timeClip;
    
    #region implement snapping interface -----------------------------------
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        if (_timeClip == null)
            return;

        snapResult.TryToImproveWithAnchorValue( _timeClip.SourceRange.Start);
        snapResult.TryToImproveWithAnchorValue( _timeClip.SourceRange.End);
    }
    #endregion
}