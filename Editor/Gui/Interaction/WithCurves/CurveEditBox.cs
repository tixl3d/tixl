using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;

namespace T3.Editor.Gui.Interaction.WithCurves;

/// <summary>
/// Note: This component is currently not used because most interactions can be completed by dragging keyframes with
/// snapping or using the TimeRangeSelection's scale functionality. 
/// </summary>
internal class CurveEditBox : IValueSnapAttractor
{
    public CurveEditBox(ScalableCanvas canvas, ValueSnapHandler snapHandler)
    {
        _canvas = canvas;
        _snapHandler = snapHandler;
        //_selectionHandler = selectionHandler;
    }

    private HashSet<VDefinition> _selectedKeyframes;
        
    public void Draw(Guid compositionSymbolId, HashSet<VDefinition> selectedKeyframes, Instance compositionOp)
    {
        if (selectedKeyframes.Count <= 1)
            return;

        if (!ImGui.GetIO().KeyAlt)
        {
            if (_changeKeyframesCommand != null)
            {
                CompleteDragCommand();
            }
            return;
        }
            
        _compositionOp = compositionOp;
        _selectedKeyframes = selectedKeyframes;
        var drawlist = ImGui.GetWindowDrawList();

        _bounds = GetBoundingBox();
        var minU = _bounds.Min.X;
        var maxU = _bounds.Max.X;
        var minV = _bounds.Min.Y;
        var maxV = _bounds.Max.Y;

        var boundsOnScreen = _canvas.TransformRect(_bounds);
        drawlist.AddRect(boundsOnScreen.Min, boundsOnScreen.Max, SelectBoxBorderColor);
        drawlist.AddRectFilled(boundsOnScreen.Min, boundsOnScreen.Max, SelectBoxBorderFill);

        var deltaOnCanvas = _canvas.InverseTransformDirection(ImGui.GetIO().MouseDelta);

        /*
        ScaleHandle(
            id: "##top",
            screenPos: boundsOnScreen.Min - VerticalHandleOffset,
            size: new Vector2(boundsOnScreen.GetWidth(), DragHandleSize),
            Direction.Vertical,
            scale: (maxV + deltaOnCanvas.Y - minV) / (maxV - minV),
            (scale, ep) => { ep.ManipulateV(minV + (ep.PosOnCanvas.Y - minV) * scale); }
            //(scale, ep) => { ManipulateKeyframeValue(ep, minV + (ep.PosOnCanvas.Y - minV) * scale); }
            );

        ScaleHandle(
            id: "##bottom",
            screenPos: new Vector2(boundsOnScreen.Min.X, boundsOnScreen.Max.Y),
            size: new Vector2(boundsOnScreen.GetWidth(), DragHandleSize),
            Direction.Vertical,
            scale: (maxV - deltaOnCanvas.Y - minV) / (maxV - minV),
            (scale, ep) => { ep.ManipulateV(maxV - (maxV - ep.PosOnCanvas.Y) * scale); }
            );

        ScaleHandle(
            id: "##right",
            screenPos: new Vector2(boundsOnScreen.Max.X, boundsOnScreen.Min.Y),
            size: new Vector2(DragHandleSize, boundsOnScreen.GetHeight()),
            Direction.Horizontal,
            scale: (maxU + deltaOnCanvas.X - minU) / (maxU - minU),
            (scale, ep) => { ep.ManipulateU(minU + (ep.PosOnCanvas.X - minU) * scale); }
            );

        ScaleHandle(
            id: "##left",
            screenPos: boundsOnScreen.Min - HorizontalHandleOffset,
            size: new Vector2(DragHandleSize, boundsOnScreen.GetHeight()),
            Direction.Horizontal,
            scale: (maxU - deltaOnCanvas.X - minU) / (maxU - minU),
            (scale, ep) => { ep.ManipulateU(maxU - (maxU - ep.PosOnCanvas.X) * scale); }
            );
            */

        var combined = (MoveRingInnerRadius + MoveRingOuterRadius);
        var center = _canvas.TransformPosition(_bounds.GetCenter());

        MoveHandle(compositionSymbolId,
                   "<##moveLeft", Direction.Horizontal,
                   screenPos: center + new Vector2(-MoveRingOuterRadius, -0.5f * combined),
                   size: new Vector2(MoveRingOuterRadius - MoveRingInnerRadius, combined));


        MoveHandle(compositionSymbolId,
                   "^##moveUp", Direction.Vertical,
                   screenPos: center + new Vector2(-0.5f * combined, -MoveRingOuterRadius),
                   size: new Vector2(combined, MoveRingOuterRadius - MoveRingInnerRadius));

        MoveHandle(compositionSymbolId,
                   ">##moveRight", Direction.Horizontal,
                   screenPos: center + new Vector2(MoveRingInnerRadius, -0.5f * combined),
                   size: new Vector2(MoveRingOuterRadius - MoveRingInnerRadius, combined));

        MoveHandle(compositionSymbolId,
                   "v##moveDown", Direction.Vertical,
                   screenPos: center + new Vector2(-0.5f * combined, MoveRingInnerRadius),
                   size: new Vector2(combined, MoveRingOuterRadius - MoveRingInnerRadius));

        MoveHandle(compositionSymbolId,
                   "+##both", Direction.Both,
                   screenPos: center - new Vector2(1, 1) * MoveRingInnerRadius,
                   size: Vector2.One * MoveRingInnerRadius * 2f);
    }
        
    #region implement snapping interface -----------------------------------
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
    }
    #endregion

    private const float MoveRingOuterRadius = 25;
    private const float MoveRingInnerRadius = 3;

    private enum Direction
    {
        Horizontal,
        Vertical,
        Both,
    }

    private void ScaleHandle(string id, Vector2 screenPos, Vector2 size, Direction direction, float scale, Action<float, VDefinition> scaleFunction)
    {
        if ((direction == Direction.Vertical && _bounds.GetHeight() <= 0.001f)
            || (direction == Direction.Horizontal && _bounds.GetWidth() <= 0.001f))
            return;

        ImGui.SetCursorScreenPos(screenPos);

        ImGui.Button(id, size);

        if (ImGui.IsItemActive() || ImGui.IsItemHovered())
            ImGui.SetMouseCursor(
                                 direction == Direction.Horizontal
                                     ? ImGuiMouseCursor.ResizeEW
                                     : ImGuiMouseCursor.ResizeNS);

        if (!ImGui.IsItemActive() || !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            return;

        if (Double.IsNaN(scale) || Math.Abs(scale) > 10000)
            return;

        if (scale < 0)
            scale = 0;

    }
        
    private double _uDragStarted;
    private double _boundUMaxDragStarted;
    private double _boundUMinDragStarted;
    private double _lastU;
        
    private void MoveHandle(in Guid compositionSymbolId, string labelAndId, Direction direction, Vector2 screenPos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(screenPos);

        ImGui.PushStyleColor(ImGuiCol.Button, new Color(0.2f).Rgba);
        ImGui.Button(labelAndId, size);
        ImGui.PopStyleColor();

        var isMouseDragging = ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left);
        if (isMouseDragging)
        {
            var u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
            if (_changeKeyframesCommand == null)
            {
                StartDragCommand(compositionSymbolId);
                _uDragStarted = u;
                _lastU = u;
                _boundUMaxDragStarted = _bounds.Max.X;
                _boundUMinDragStarted = _bounds.Min.X;
                //_lastBoundsMin = _boundUMinDragStarted;
            }

            var totalDeltaU = u - _uDragStarted;
            if (!ImGui.GetIO().KeyShift)
            {
                var newBoundsMax = totalDeltaU + _boundUMaxDragStarted;
                var newBoundsMin = totalDeltaU + _boundUMinDragStarted;
                
                if (_snapHandler.TryCheckForSnapping(newBoundsMin, out var snappedMinValue, _canvas.Scale.X, new List<IValueSnapAttractor> { this }))
                {
                    totalDeltaU = snappedMinValue - _boundUMinDragStarted;
                }
                else if (_snapHandler.TryCheckForSnapping(newBoundsMax, out var snappedMaxValue, _canvas.Scale.X, new List<IValueSnapAttractor> { this }))
                {
                    totalDeltaU =  snappedMaxValue - _boundUMaxDragStarted;
                }
            }

            var newU = totalDeltaU + _uDragStarted;
            var deltaU =  newU - _lastU;
            _lastU = newU;
                
            foreach (var ep in _selectedKeyframes)
            {
                ep.U += deltaU;
            }
        }
        else if (ImGui.IsItemDeactivated())
        {
            CompleteDragCommand();
        }
    }

    public void StartDragCommand(Guid compositionSymbolId)
    {
        // FIXME: getting current curves is complicated here.
        var mockNotWorking = new List<Curve>();
        _changeKeyframesCommand = new ChangeKeyframesCommand(_selectedKeyframes, mockNotWorking);
    }
        
        
    public void CompleteDragCommand()
    {
        if (_changeKeyframesCommand == null)
            return;
            
        // Update reference in macro command
        _changeKeyframesCommand.StoreCurrentValues();
        UndoRedoStack.Add(_changeKeyframesCommand);
        _changeKeyframesCommand = null;
    }
        
    private static ChangeKeyframesCommand _changeKeyframesCommand;
        
    private ImRect GetBoundingBox()
    {
        var minU = float.PositiveInfinity;
        var minV = float.PositiveInfinity;
        var maxU = float.NegativeInfinity;
        var maxV = float.NegativeInfinity;

        if (_selectedKeyframes.Count > 1)
        {
            foreach (var selected in _selectedKeyframes)
            {
                minU = Math.Min(minU, (float)selected.U);
                maxU = Math.Max(maxU, (float)selected.U);
                minV = Math.Min(minV, (float)selected.Value);
                maxV = Math.Max(maxV, (float)selected.Value);
            }
            return new ImRect(minU, minV, maxU, maxV);
        }

        if (_selectedKeyframes.Count == 1)
        {
            var vDef = _selectedKeyframes.First();
            minU = maxU = (float)vDef.U;
            minV = maxV = (float)vDef.Value;

            return new ImRect(minU, minV, maxU, maxV);
        }
        return new ImRect(100, 100, 200, 200);
    }
        
    private ImRect _bounds;
    private readonly ScalableCanvas _canvas;

    // Styling
    private const float DragHandleSize = 10;
    private static readonly Vector2 VerticalHandleOffset = new(0, DragHandleSize);
    private static readonly Vector2 HorizontalHandleOffset = new(DragHandleSize, 0);

    private static readonly Color SelectBoxBorderColor = new(1, 1, 1, 0.2f);
    private static readonly Color SelectBoxBorderFill = new(1, 1, 1, 0.05f);
    private Instance _compositionOp;
    private readonly ValueSnapHandler _snapHandler;
}