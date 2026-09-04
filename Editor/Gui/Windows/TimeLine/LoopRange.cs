#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Editor.Gui.Interaction.Snapping;

namespace T3.Editor.Gui.Windows.TimeLine;

internal sealed class LoopRange : IValueSnapAttractor
{
    public void Draw(TimeLineCanvas canvas, Playback playback, ImDrawListPtr drawlist, ValueSnapHandler snapHandler)
    {
        _playback = playback;
        

        ImGui.PushStyleColor(ImGuiCol.Button, _timeRangeMarkerColor.Rgba);

        // Range start
        {
            var xRangeStart = canvas.TransformX(playback.LoopRange.Start);
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

            SetCursorToBottom(
                              xRangeStart - _timeRangeHandleSize.X,
                              _timeRangeHandleSize.Y);

            ImGui.Button("##StartPos", _timeRangeHandleSize);

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var newTime = canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
                if(snapHandler.TryCheckForSnapping( newTime, out var snappedValue, canvas.Scale.X, [this]))
                {
                    newTime = (float)snappedValue;
                }
                playback.LoopRange.Start = newTime;
            }
        }

        // Range end
        {
            var rangeEndX = canvas.TransformX(playback.LoopRange.End);
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
                                   rangeEndPos,
                                   rangeEndPos + _timeRangeShadowSize,
                                   _timeRangeShadowColor);

            // Line
            drawlist.AddRectFilled(rangeEndPos, rangeEndPos + new Vector2(1, 9999), _timeRangeShadowColor);

            SetCursorToBottom(
                              rangeEndX,
                              _timeRangeHandleSize.Y);

            ImGui.Button("##EndPos", _timeRangeHandleSize);

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var newTime = canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
                if (snapHandler.TryCheckForSnapping(newTime, out var snappedValue, canvas.Scale.X, [this]))
                {
                    newTime = (float)snappedValue;
                }
                playback.LoopRange.End = newTime;
            }
        }

        ImGui.PopStyleColor();
    }

    private static void SetCursorToBottom(float xInScreen, float paddingFromBottom)
    {
        var maxY = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y;
        ImGui.SetCursorScreenPos(new Vector2(xInScreen, maxY - paddingFromBottom));
    }

    private static readonly Vector2 _timeRangeHandleSize = new(10, 20);
    private static readonly Vector2 _timeRangeShadowSize = new(5, 9999);
    private static readonly Color _timeRangeShadowColor = new(0, 0, 0, 0.5f);
    private static readonly Color _timeRangeOutsideColor = new(0.0f, 0.0f, 0.0f, 0.3f);
    private static readonly Color _timeRangeMarkerColor = new(1f, 1, 1f, 0.3f);
    private Playback? _playback;
        
    #region implement snapping interface -----------------------------------
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        if (_playback == null)
            return;
            
        snapResult.TryToImproveWithAnchorValue(_playback.LoopRange.Start);
        snapResult.TryToImproveWithAnchorValue(_playback.LoopRange.End);
    }
    #endregion
}