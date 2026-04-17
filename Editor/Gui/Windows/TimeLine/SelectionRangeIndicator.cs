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
/// When nothing is selected, it falls back to the extent of all visible keyframes so the
/// user can stretch/translate everything without pre-selecting.
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
        // Keyframes: selection if it covers a positive range, else fall back to all keyframes so the
        // user can grab the SRI to move/stretch everything without pre-selecting.
        // Clips: only selected clips contribute — never all clips.
        var dopeSheet = _canvas.DopeSheetArea;
        var layers = _canvas.LayersArea;

        var keyframeRange = dopeSheet.GetSelectionTimeRange();
        _autoSelectKeyframesOnDrag = !keyframeRange.IsValid || keyframeRange.Duration <= 0;
        if (_autoSelectKeyframesOnDrag)
            keyframeRange = dopeSheet.GetAllKeyframesTimeRange();

        var clipRange = layers.GetSelectionTimeRange();

        _range = TimeRange.Undefined;
        if (keyframeRange.IsValid)
        {
            _range.Unite(keyframeRange.Start);
            _range.Unite(keyframeRange.End);
        }
        if (clipRange.IsValid)
        {
            _range.Unite(clipRange.Start);
            _range.Unite(clipRange.End);
        }
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

        var handleSize = new Vector2(5 * scale, 5 * scale);
        var compositionSymbolId = composition.Symbol.Id;
        var hitY = lineY - 2 * scale;
        var hitHeight = 8 * scale;

        // Emit the middle hit-target first so the edge handles (emitted after) win the hit test on overlap.
        var middleHovered = false;
        var middleActive = false;
        if (rightClamped - leftClamped > handleSize.X * 2)
        {
            var middleStart = xStart + handleSize.X;
            var middleEnd = xEnd - handleSize.X;
            ImGui.SetCursorScreenPos(new Vector2(middleStart, hitY));
            ImGui.InvisibleButton("##SriMiddle", new Vector2(middleEnd - middleStart, hitHeight));
            middleHovered = ImGui.IsItemHovered();
            middleActive = ImGui.IsItemActive();
            if (middleHovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            HandleMiddleDrag(compositionSymbolId);
        }

        // Start handle
        ImGui.SetCursorScreenPos(new Vector2(xStart - handleSize.X, hitY));
        ImGui.InvisibleButton("##SriStart", new Vector2(handleSize.X * 2, hitHeight));
        var startHovered = ImGui.IsItemHovered();
        var startActive = ImGui.IsItemActive();
        if (startHovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        HandleEdgeDrag(compositionSymbolId, _range.Start, _range.End);

        // End handle
        ImGui.SetCursorScreenPos(new Vector2(xEnd - handleSize.X, hitY));
        ImGui.InvisibleButton("##SriEnd", new Vector2(handleSize.X * 2, hitHeight));
        var endHovered = ImGui.IsItemHovered();
        var endActive = ImGui.IsItemActive();
        if (endHovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        HandleEdgeDrag(compositionSymbolId, _range.End, _range.Start);

        var lineOpacity = (middleHovered || middleActive) ? 1.0f : (_autoSelectKeyframesOnDrag ? 0.3f : 0.75f);
        var startOpacity = (startHovered || startActive) ? 1.0f : (_autoSelectKeyframesOnDrag ? 0.3f : 0.75f);
        var endOpacity = (endHovered || endActive) ? 1.0f : (_autoSelectKeyframesOnDrag ? 0.3f : 0.75f);

        if (rightClamped > leftClamped)
        {
            drawList.AddRectFilled(new Vector2(leftClamped, lineY),
                                   new Vector2(rightClamped, lineY + 1),
                                   UiColors.ForegroundFull.Fade(lineOpacity));
        }

        DrawHandle(drawList, new Vector2(xStart, lineY + 0.5f), handleSize, startOpacity);
        DrawHandle(drawList, new Vector2(xEnd, lineY + 0.5f), handleSize, endOpacity);
    }

    private static void DrawHandle(ImDrawListPtr drawList, Vector2 center, Vector2 size, float opacity)
    {
        var hx = size.X * 0.5f;
        var hy = size.Y * 0.5f;
        drawList.AddRectFilled(new Vector2(center.X - hx, center.Y - hy),
                               new Vector2(center.X + hx, center.Y + hy),
                               UiColors.ForegroundFull.Fade(opacity));
    }

    private void HandleEdgeDrag(in Guid compositionSymbolId, double originalU, double origin)
    {
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);

            if (!_isDragging)
            {
                if (_autoSelectKeyframesOnDrag)
                    _canvas.DopeSheetArea.SelectAllKeyframes();
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
                if (_autoSelectKeyframesOnDrag)
                    _canvas.DopeSheetArea.SelectAllKeyframes();
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
    private bool _autoSelectKeyframesOnDrag;
    private double _lastDragU;
    private TimeRange _range;

    private readonly TimeLineCanvas _canvas;
    private readonly ValueSnapHandler _snapHandler;
    private readonly IValueSnapAttractor[] _snapExclusions;
}
