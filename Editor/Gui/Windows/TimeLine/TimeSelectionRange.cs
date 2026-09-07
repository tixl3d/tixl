#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Snapping;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// A graphic representation that allows to move and scale multiple selected timeline elements
/// </summary>
internal sealed class TimeSelectionRange : IValueSnapAttractor
{
    public TimeSelectionRange(TimeLineCanvas timeLineCanvas, ValueSnapHandler snapHandler)
    {
        _timeLineCanvas = timeLineCanvas;
        _snapHandler = snapHandler;
    }

    public void Draw(Instance composition, ImDrawListPtr drawlist)
    {
        if (!_isDragging && !ImGui.GetIO().KeyAlt)
            return;
            
        _selectionTimeRange = _timeLineCanvas.GetSelectionTimeRange();
        if (!_selectionTimeRange.IsValid || _selectionTimeRange.Duration <= 0)
            return;

        var compositionSymbolId = composition.Symbol.Id;
        var contentRegionMin = ImGui.GetWindowPos();
        var contentRegionMax = contentRegionMin + ImGui.GetWindowSize();
        ImGui.PushStyleColor(ImGuiCol.Button, _timeRangeMarkerColor.Rgba);
        // Range start
        {
            var xRangeStartOnScreen = _timeLineCanvas.TransformX(_selectionTimeRange.Start);
            var rangeStartPos = new Vector2(xRangeStartOnScreen, contentRegionMin.Y);
            // Shade outside
            drawlist.AddRectFilled(
                                   new Vector2(0, 0),
                                   new Vector2(xRangeStartOnScreen, _timeRangeShadowSize.Y),
                                   _timeRangeOutsideColor);

            // Shadow
            drawlist.AddRectFilled(
                                   rangeStartPos - new Vector2(_timeRangeShadowSize.X - 1, 0),
                                   rangeStartPos + new Vector2(0, _timeRangeShadowSize.Y),
                                   _timeRangeShadowColor);

            // Line
            drawlist.AddRectFilled(rangeStartPos, rangeStartPos + new Vector2(1, 9999), _timeRangeShadowColor);
                
            ImGui.SetCursorScreenPos(rangeStartPos 
                                     + new Vector2(-_timeRangeHandleSize.X, 
                                                   (contentRegionMax-contentRegionMin).Y - _timeRangeHandleSize.Y));
            ImGui.Button("##SelectionStartPos", _timeRangeHandleSize);

            HandleDrag(compositionSymbolId, _selectionTimeRange.Start, _selectionTimeRange.End);
        }

        // Range end
        {
            var xRangeEndOnScreen = _timeLineCanvas.TransformX(_selectionTimeRange.End);
            var rangeEndPos = new Vector2(xRangeEndOnScreen, contentRegionMin.Y);

            // Shade outside
            //var windowMaxX =  ImGui.GetContentRegionAvail().X + _timeLineCanvas.WindowPos.X;
            if (xRangeEndOnScreen < contentRegionMax.X)
                drawlist.AddRectFilled(
                                       rangeEndPos,
                                       new Vector2(contentRegionMax.X, _timeRangeShadowSize.Y),
                                       _timeRangeOutsideColor);

            // Shadow
            drawlist.AddRectFilled(
                                   rangeEndPos,
                                   rangeEndPos + _timeRangeShadowSize,
                                   _timeRangeShadowColor);

            // Line
            drawlist.AddRectFilled(rangeEndPos, rangeEndPos + new Vector2(1, 9999), _timeRangeShadowColor);

            ImGui.SetCursorScreenPos(rangeEndPos 
                                     + new Vector2(0, (contentRegionMax-contentRegionMin).Y - _timeRangeHandleSize.Y));
                
            ImGui.Button("##SelectionEndPos", _timeRangeHandleSize);
            HandleDrag(compositionSymbolId, _selectionTimeRange.End, _selectionTimeRange.Start);
        }

        ImGui.PopStyleColor();
    }

    private void HandleDrag(in Guid compositionSymbolId, double originalU, double origin)
    {
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var u = _timeLineCanvas.InverseTransformX(ImGui.GetIO().MousePos.X);

            if (!_isDragging)
            {
                _timeLineCanvas.StartDragCommand(compositionSymbolId);
                _lastDragU = originalU;
                _isDragging = true;
            }

            if (!ImGui.GetIO().KeyShift && _snapHandler.TryCheckForSnapping( u, out var snappedValue, _timeLineCanvas.Scale.X, [this]))
            {
                u = (float)snappedValue;
            }
                
            var dScale = (u - origin) / (_lastDragU - origin);
            _timeLineCanvas.UpdateDragStretchCommand(scaleU: dScale, scaleV: 1, originU: origin, originV: 0);
            _lastDragU = u;
        }
        else if (ImGui.IsItemDeactivated() && _isDragging)
        {
            _isDragging = false;
            _timeLineCanvas.CompleteDragCommand();
        }
    }

    private static void SetCursorToBottom(float xInScreen, float paddingFromBottom)
    {
        var maxY = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y;
        ImGui.SetCursorScreenPos(new Vector2(xInScreen, maxY - paddingFromBottom));
    }

    #region implement snapping interface -----------------------------------
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        snapResult.TryToImproveWithAnchorValue( _selectionTimeRange.Start);
        snapResult.TryToImproveWithAnchorValue( _selectionTimeRange.End);
    }
    #endregion

    private bool _isDragging;
    private double _lastDragU;

    private static readonly Vector2 _timeRangeHandleSize = new(10, 20);
    private static readonly Vector2 _timeRangeShadowSize = new(5, 9999);
    private static readonly Color _timeRangeShadowColor = new(0, 0, 0, 0.4f);
    private static readonly Color _timeRangeOutsideColor = new(0.0f, 0.0f, 0.0f, 0.2f);
    private static readonly Color _timeRangeMarkerColor = new(1f, 1, 1f, 0.3f);
    private readonly TimeLineCanvas _timeLineCanvas;
    private readonly ValueSnapHandler _snapHandler;
    private TimeRange _selectionTimeRange;
}