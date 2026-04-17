#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Thin horizontal indicator drawn inside the timeline ruler showing the time range
/// of currently selected keyframes / clips. Start and end handles stretch the selection;
/// the middle section translates it.
/// </summary>
internal sealed class SelectionRangeIndicator : IValueSnapAttractor
{
    public SelectionRangeIndicator(TimeLineCanvas canvas, ValueSnapHandler snapHandler)
    {
        _canvas = canvas;
        _snapHandler = snapHandler;
        _snapExclusions = [this];
    }

    public void Draw(Instance composition, ImDrawListPtr drawList)
    {
        _range = _canvas.GetSelectionTimeRange();
        if (!_range.IsValid || _range.Duration <= 0)
            return;

        var scale = T3Ui.UiScaleFactor;
        var rulerPos = ImGui.GetWindowPos();
        var rulerSize = ImGui.GetWindowSize();

        var xStart = _canvas.TransformX(_range.Start);
        var xEnd = _canvas.TransformX(_range.End);
        var lineY = rulerPos.Y + rulerSize.Y - 4 * scale;

        var leftClamped = MathF.Max(xStart, rulerPos.X);
        var rightClamped = MathF.Min(xEnd, rulerPos.X + rulerSize.X);

        if (rightClamped > leftClamped)
        {
            drawList.AddRectFilled(new Vector2(leftClamped, lineY),
                                   new Vector2(rightClamped, lineY + 1),
                                   UiColors.ForegroundFull.Fade(0.6f));
        }

        var handleSize = new Vector2(5 * scale, 5 * scale);
        DrawHandle(drawList, new Vector2(xStart, lineY + 0.5f), handleSize);
        DrawHandle(drawList, new Vector2(xEnd, lineY + 0.5f), handleSize);

        var compositionSymbolId = composition.Symbol.Id;
        var hitY = lineY - 2 * scale;
        var hitHeight = 8 * scale;

        // Emit the middle hit-target first so the edge handles (emitted after) win the hit test on overlap.
        if (rightClamped - leftClamped > handleSize.X * 2)
        {
            var middleStart = xStart + handleSize.X;
            var middleEnd = xEnd - handleSize.X;
            ImGui.SetCursorScreenPos(new Vector2(middleStart, hitY));
            ImGui.InvisibleButton("##SriMiddle", new Vector2(middleEnd - middleStart, hitHeight));
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            HandleMiddleDrag(compositionSymbolId);
        }

        // Start handle
        ImGui.SetCursorScreenPos(new Vector2(xStart - handleSize.X, hitY));
        ImGui.InvisibleButton("##SriStart", new Vector2(handleSize.X * 2, hitHeight));
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        HandleEdgeDrag(compositionSymbolId, _range.Start, _range.End);

        // End handle
        ImGui.SetCursorScreenPos(new Vector2(xEnd - handleSize.X, hitY));
        ImGui.InvisibleButton("##SriEnd", new Vector2(handleSize.X * 2, hitHeight));
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        HandleEdgeDrag(compositionSymbolId, _range.End, _range.Start);
    }

    private static void DrawHandle(ImDrawListPtr drawList, Vector2 center, Vector2 size)
    {
        var hx = size.X * 0.5f;
        var hy = size.Y * 0.5f;
        drawList.AddRectFilled(new Vector2(center.X - hx, center.Y - hy),
                               new Vector2(center.X + hx, center.Y + hy),
                               UiColors.ForegroundFull.Fade(0.9f));
    }

    private void HandleEdgeDrag(in Guid compositionSymbolId, double originalU, double origin)
    {
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);

            if (!_isDragging)
            {
                _canvas.StartDragCommand(compositionSymbolId);
                _lastDragU = originalU;
                _isDragging = true;
            }

            if (!ImGui.GetIO().KeyShift
                && _snapHandler.TryCheckForSnapping(u, out var snappedValue, _canvas.Scale.X, _snapExclusions))
            {
                u = (float)snappedValue;
            }

            var denom = _lastDragU - origin;
            if (Math.Abs(denom) < 1e-6)
                return;

            var dScale = (u - origin) / denom;
            _canvas.UpdateDragStretchCommand(scaleU: dScale, scaleV: 1, originU: origin, originV: 0);
            _lastDragU = u;
        }
        else if (ImGui.IsItemDeactivated() && _isDragging)
        {
            _isDragging = false;
            _canvas.CompleteDragCommand();
        }
    }

    private void HandleMiddleDrag(in Guid compositionSymbolId)
    {
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);

            if (!_isDragging)
            {
                _canvas.StartDragCommand(compositionSymbolId);
                _lastDragU = u;
                _isDragging = true;
                return;
            }

            var du = u - _lastDragU;
            if (du == 0)
                return;

            _canvas.UpdateDragCommand(du, 0);
            _lastDragU = u;
        }
        else if (ImGui.IsItemDeactivated() && _isDragging)
        {
            _isDragging = false;
            _canvas.CompleteDragCommand();
        }
    }

    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        if (!_range.IsValid || _range.Duration <= 0)
            return;
        snapResult.TryToImproveWithAnchorValue(_range.Start);
        snapResult.TryToImproveWithAnchorValue(_range.End);
    }

    private bool _isDragging;
    private double _lastDragU;
    private TimeRange _range;

    private readonly TimeLineCanvas _canvas;
    private readonly ValueSnapHandler _snapHandler;
    private readonly IValueSnapAttractor[] _snapExclusions;
}
