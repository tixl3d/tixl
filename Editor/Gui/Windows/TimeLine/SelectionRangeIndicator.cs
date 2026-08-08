#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Thin horizontal indicator drawn inside the timeline ruler showing the time range of
/// the current keyframe / clip selection. Start and end handles stretch the selection;
/// the middle section translates it. When nothing is selected, it falls back to the extent
/// of all visible keyframes so the user can stretch/translate everything without pre-selecting.
///
/// TimeWarp handles (Alt+click on the bar): small circular markers that subdivide the
/// selection range into segments. Dragging a handle retimes the two adjacent segments
/// piecewise-linearly around it. If inner handles exist, dragging the SRI start or end
/// retimes only the outer segment rather than stretching the whole selection; translating
/// via middle-drag carries the handles along.
///
/// The handle list and the piecewise-linear drag math live in
/// <see cref="TimeWarpHandleSet"/> and <see cref="TimeWarpDrag"/>; this class orchestrates
/// drawing, hit-testing, and mode dispatch.
/// </summary>
internal sealed class SelectionRangeIndicator : IValueSnapAttractor
{
    public SelectionRangeIndicator(TimeLineCanvas canvas, ValueSnapHandler snapHandler)
    {
        _canvas = canvas;
        _snapHandler = snapHandler;
        _snapExclusions = [this];
        _handles = new TimeWarpHandleSet();
        _warpDrag = new TimeWarpDrag(canvas, _handles);
        _sourceRegion = new SourceRegionIndicator(canvas);
    }

    public void Draw(Instance composition, ImDrawListPtr drawList)
    {
        _lastComposition = composition;
        var hasRange = ComputeRange(out var rulerPos, out var rulerSize, out var scale);

        var xStart = hasRange ? _canvas.TransformX(_range.Start) : 0;
        var xEnd = hasRange ? _canvas.TransformX(_range.End) : 0;
        // 5px instead of 4: 1px of breathing room between the bar and the source region behind it.
        var lineY = rulerPos.Y + rulerSize.Y - 5 * scale;

        // The media-clip source region draws (and slip-drags) even without a selection range.
        _sourceRegion.Draw(composition, drawList, hasRange, xStart, xEnd, lineY, rulerPos, rulerSize, scale);

        if (!hasRange)
            return;
        var leftClamped = MathF.Max(xStart, rulerPos.X);
        var rightClamped = MathF.Min(xEnd, rulerPos.X + rulerSize.X);

        var handleSize = new Vector2(5 * scale, 5 * scale);
        var hitY = lineY - 2 * scale;
        var hitHeight = 8 * scale;
        var altHeld = ImGui.GetIO().KeyAlt;

        _handles.RebuildVisible(_range);

        // Middle — emitted first; edges + warp handles emitted later steal overlap.
        EmitMiddleButton(composition.Symbol.Id, xStart, xEnd, hitY, hitHeight, scale, handleSize.X,
                         altHeld, out var middleHovered, out var middleActive, out var middlePressed);

        EmitEdgeButton(composition.Symbol.Id, xStart, hitY, hitHeight, handleSize.X, isStart: true,
                       out var startHovered, out var startActive);
        EmitEdgeButton(composition.Symbol.Id, xEnd, hitY, hitHeight, handleSize.X, isStart: false,
                       out var endHovered, out var endActive);

        var warpHandleRadius = 4 * scale;
        var warpHandleHoveredIndex = EmitWarpHandleButtons(composition, hitY, hitHeight, warpHandleRadius);

        HandleAltClickInsert(altHeld, middlePressed, middleActive, warpHandleHoveredIndex, scale);

        DrawBar(drawList, leftClamped, rightClamped, lineY, handleSize,
                new Vector2(xStart, lineY + 0.5f), new Vector2(xEnd, lineY + 0.5f),
                altHeld, middleHovered, middleActive,
                startHovered, startActive, endHovered, endActive);

        DrawWarpHandles(drawList, lineY, warpHandleRadius, warpHandleHoveredIndex);
        DrawAltHoverPreview(drawList, altHeld, middleHovered, warpHandleHoveredIndex, lineY, warpHandleRadius);

        ApplyPendingRemoval();
    }

    public void ClearTimeWarpHandles() => _handles.Clear();

    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        if (!_range.IsValid || _range.Duration <= 0)
            return;
        snapResult.TryToImproveWithAnchorValue(_range.Start);
        snapResult.TryToImproveWithAnchorValue(_range.End);
    }

    //
    // Range computation ------------------------------------------------------
    //

    private bool ComputeRange(out Vector2 rulerPos, out Vector2 rulerSize, out float scale)
    {
        rulerPos = ImGui.GetWindowPos();
        rulerSize = ImGui.GetWindowSize();
        scale = T3Ui.UiScaleFactor;

        var editors = _canvas.KeyframeEditors;
        var layers = _canvas.ClipArea;

        // Keyframes: aggregated across all active editors — selection if it covers a positive range,
        // else fall back to all keyframes. Clips: only selected clips contribute.
        var keyframeRange = editors.GetSelectionTimeRange();
        _autoSelectKeyframesOnDrag = !keyframeRange.IsValid || keyframeRange.Duration <= 0;
        if (_autoSelectKeyframesOnDrag)
            keyframeRange = editors.GetAllKeyframesTimeRange();

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

        // Detect selection-just-became-empty → drop handles.
        var hasKeyframeSelectionNow = !_autoSelectKeyframesOnDrag;
        if (_lastHadKeyframeSelection && !hasKeyframeSelectionNow)
            _handles.Clear();
        _lastHadKeyframeSelection = hasKeyframeSelectionNow;

        return _range.IsValid && _range.Duration > 0;
    }

    //
    // Hit-test emission ------------------------------------------------------
    //

    private void EmitMiddleButton(in Guid compositionSymbolId,
                                  float xStart, float xEnd, float hitY, float hitHeight,
                                  float scale, float edgeHalfWidth, bool altHeld,
                                  out bool hovered, out bool active, out bool pressed)
    {
        hovered = false;
        active = false;
        pressed = false;

        if (xEnd - xStart <= edgeHalfWidth * 2)
            return;

        var middleStart = xStart + edgeHalfWidth;
        var middleEnd = xEnd - edgeHalfWidth;
        ImGui.SetCursorScreenPos(new Vector2(middleStart, hitY));
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton("##SriMiddle", new Vector2(middleEnd - middleStart, hitHeight));
        hovered = ImGui.IsItemHovered();
        active = ImGui.IsItemActive();
        pressed = ImGui.IsItemActivated();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        // Translate drag only when Alt is NOT held (Alt reserved for marker insert);
        // continue driving an in-flight drag even if Alt state changes mid-drag.
        if (!altHeld || _currentDragMode == DragMode.Middle || _currentDragMode == DragMode.MiddleCustom)
            HandleMiddleDrag(compositionSymbolId);
    }

    private void EmitEdgeButton(in Guid compositionSymbolId, float edgeX, float hitY, float hitHeight,
                                float edgeHalfWidth, bool isStart,
                                out bool hovered, out bool active)
    {
        ImGui.SetCursorScreenPos(new Vector2(edgeX - edgeHalfWidth, hitY));
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton(isStart ? "##SriStart" : "##SriEnd", new Vector2(edgeHalfWidth * 2, hitHeight));
        hovered = ImGui.IsItemHovered();
        active = ImGui.IsItemActive();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        HandleEdgeDrag(compositionSymbolId, isStart);
    }

    private int EmitWarpHandleButtons(Instance composition, float hitY, float hitHeight, float handleRadius)
    {
        var warpHitSize = new Vector2(handleRadius * 2 + 2, hitHeight);
        var hoveredIdx = -1;
        for (var vi = 0; vi < _handles.VisibleIndices.Count; vi++)
        {
            var handleIdx = _handles.VisibleIndices[vi];
            var hx = _canvas.TransformX((float)_handles[handleIdx]);
            ImGui.SetCursorScreenPos(new Vector2(hx - warpHitSize.X * 0.5f, hitY));
            ImGui.InvisibleButton($"##SriWarp{handleIdx}", warpHitSize);

            if (ImGui.IsItemHovered())
            {
                hoveredIdx = handleIdx;
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            HandleWarpHandleDrag(composition, handleIdx);
        }
        return hoveredIdx;
    }

    private void HandleAltClickInsert(bool altHeld, bool middlePressed, bool middleActive,
                                      int warpHandleHoveredIndex, float scale)
    {
        // Alt+click on middle (no existing handle under cursor) inserts a handle.
        // Alt+click on an existing handle is handled inside HandleWarpHandleDrag (toggle).
        if (altHeld && middlePressed && warpHandleHoveredIndex < 0 && _currentDragMode == DragMode.None)
        {
            _pendingAltInsertU = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
            _pendingAltInsertActive = true;
        }

        if (_pendingAltInsertActive && middleActive && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f * scale))
        {
            // Alt+drag is reserved for another interaction — cancel the pending insert.
            _pendingAltInsertActive = false;
        }

        if (_pendingAltInsertActive && !middleActive)
        {
            _handles.Insert(_pendingAltInsertU, _range);
            _pendingAltInsertActive = false;
        }
    }

    //
    // Rendering --------------------------------------------------------------
    //

    private void DrawBar(ImDrawListPtr drawList, float leftClamped, float rightClamped, float lineY,
                         Vector2 handleSize, Vector2 startCenter, Vector2 endCenter,
                         bool altHeld, bool middleHovered, bool middleActive,
                         bool startHovered, bool startActive, bool endHovered, bool endActive)
    {
        var baseOpacity = _autoSelectKeyframesOnDrag ? 0.3f : 0.75f;
        var lineColor = (altHeld && (middleHovered || middleActive))
                            ? UiColors.StatusAutomated
                            : UiColors.ForegroundFull.Fade((middleHovered || middleActive) ? 1.0f : baseOpacity);
        var startColor = UiColors.ForegroundFull.Fade((startHovered || startActive) ? 1.0f : baseOpacity);
        var endColor = UiColors.ForegroundFull.Fade((endHovered || endActive) ? 1.0f : baseOpacity);

        if (rightClamped > leftClamped)
        {
            drawList.AddRectFilled(new Vector2(leftClamped, lineY),
                                   new Vector2(rightClamped, lineY + 1),
                                   lineColor);
        }

        DrawEdgeHandle(drawList, startCenter, handleSize, startColor);
        DrawEdgeHandle(drawList, endCenter, handleSize, endColor);
    }

    private static void DrawEdgeHandle(ImDrawListPtr drawList, Vector2 center, Vector2 size, Color color)
    {
        var hx = size.X * 0.5f;
        var hy = size.Y * 0.5f;
        drawList.AddRectFilled(new Vector2(center.X - hx, center.Y - hy),
                               new Vector2(center.X + hx, center.Y + hy),
                               color);
    }

    private void DrawWarpHandles(ImDrawListPtr drawList, float lineY, float handleRadius, int hoveredIdx)
    {
        for (var vi = 0; vi < _handles.VisibleIndices.Count; vi++)
        {
            var handleIdx = _handles.VisibleIndices[vi];
            if (handleIdx == _pendingRemoveHandleIndex || handleIdx >= _handles.Count)
                continue;
            var hx = _canvas.TransformX((float)_handles[handleIdx]);
            var center = new Vector2(hx, lineY + 0.5f);
            var hovered = handleIdx == hoveredIdx || handleIdx == _draggedWarpHandleIndex;
            var color = hovered ? UiColors.StatusAutomated : UiColors.ForegroundFull;
            drawList.AddCircleFilled(center, handleRadius, color);
        }
    }

    private void DrawAltHoverPreview(ImDrawListPtr drawList, bool altHeld, bool middleHovered,
                                     int warpHandleHoveredIndex, float lineY, float handleRadius)
    {
        if (!altHeld || !middleHovered || _currentDragMode != DragMode.None)
            return;

        float previewX;
        if (warpHandleHoveredIndex >= 0 && warpHandleHoveredIndex < _handles.Count
            && warpHandleHoveredIndex != _pendingRemoveHandleIndex)
        {
            previewX = _canvas.TransformX((float)_handles[warpHandleHoveredIndex]);
        }
        else
        {
            previewX = ImGui.GetIO().MousePos.X;
        }

        drawList.AddCircle(new Vector2(previewX, lineY + 0.5f), handleRadius + 1,
                           UiColors.StatusAutomated, 12, 1.2f);
    }

    private void ApplyPendingRemoval()
    {
        if (_pendingRemoveHandleIndex >= 0)
            _handles.RemoveAt(_pendingRemoveHandleIndex);
        _pendingRemoveHandleIndex = -1;
    }

    //
    // Drag handlers ----------------------------------------------------------
    //

    private void HandleEdgeDrag(in Guid compositionSymbolId, bool isStart)
    {
        var originalU = isStart ? _range.Start : _range.End;
        var origin = isStart ? _range.End : _range.Start;

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);

            if (_currentDragMode == DragMode.None)
                StartEdgeDrag(compositionSymbolId, isStart, originalU);

            if (_currentDragMode is DragMode.EdgeStartCustom or DragMode.EdgeEndCustom)
            {
                _warpDrag.Update(u);
                _lastDragU = u;
                return;
            }

            if (_currentDragMode != (isStart ? DragMode.EdgeStart : DragMode.EdgeEnd))
                return;

            if (!ImGui.GetIO().KeyShift
                && _snapHandler.TryCheckForSnapping(u, out var snappedValue, _canvas.Scale.X, _snapExclusions))
            {
                u = (float)snappedValue;
            }

            var denom = _lastDragU - origin;
            if (Math.Abs(denom) < 1e-6)
                return;

            var dScale = (u - origin) / denom;
            // Keyframes stretch about the fixed end; time clips are trimmed at the dragged boundary instead.
            _canvas.UpdateSelectionRangeEdgeDrag(dScale, origin, u, _edgeDragOrigU, isStart);
            _lastDragU = u;
        }
        else if (ImGui.IsItemDeactivated())
        {
            var wantsStart = isStart ? DragMode.EdgeStart : DragMode.EdgeEnd;
            var wantsStartCustom = isStart ? DragMode.EdgeStartCustom : DragMode.EdgeEndCustom;
            if (_currentDragMode == wantsStart)
            {
                _canvas.CompleteDragCommand();
                _currentDragMode = DragMode.None;
            }
            else if (_currentDragMode == wantsStartCustom)
            {
                _warpDrag.Complete();
                _currentDragMode = DragMode.None;
            }
        }
    }

    private void StartEdgeDrag(in Guid compositionSymbolId, bool isStart, double originalU)
    {
        if (_autoSelectKeyframesOnDrag)
            _canvas.KeyframeEditors.SelectAllKeyframes();

        if (_handles.HasVisible)
        {
            // Handles present → retime only the outer segment (start to first handle, or last to end).
            var origEdgeU = isStart ? (double)_range.Start : _range.End;
            var boundary = isStart ? _handles.GetFirstVisibleU() : _handles.GetLastVisibleU();
            _warpDrag.Begin(_lastComposition!, origHandleU: origEdgeU,
                            prevBoundary: isStart ? double.NegativeInfinity : boundary,
                            nextBoundary: isStart ? boundary : double.PositiveInfinity,
                            singleSegment: true,
                            segmentLeftU: isStart ? Math.Min(origEdgeU, boundary) : boundary,
                            segmentRightU: isStart ? boundary : Math.Max(origEdgeU, boundary),
                            pureTranslation: false,
                            trackHandlePositions: false,
                            useAllKeyframes: _autoSelectKeyframesOnDrag);
            _currentDragMode = isStart ? DragMode.EdgeStartCustom : DragMode.EdgeEndCustom;
        }
        else
        {
            _canvas.StartDragCommand(compositionSymbolId);
            _currentDragMode = isStart ? DragMode.EdgeStart : DragMode.EdgeEnd;
        }

        _lastDragU = originalU;
        _edgeDragOrigU = originalU;
    }

    private void HandleMiddleDrag(in Guid compositionSymbolId)
    {
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);

            if (_currentDragMode == DragMode.None)
            {
                StartMiddleDrag(compositionSymbolId, u);
                return;
            }

            if (_currentDragMode != DragMode.Middle && _currentDragMode != DragMode.MiddleCustom)
                return;

            // Snap the SRI's start or end to nearby anchors — whichever gets a stronger snap wins.
            var rawDu = u - _middleOrigPressU;
            var correctedDu = SnapMiddleTranslation(rawDu);

            if (_currentDragMode == DragMode.MiddleCustom)
            {
                _warpDrag.Update(_middleOrigPressU + correctedDu);
                _middleLastAppliedDu = correctedDu;
                _lastDragU = u;
                return;
            }

            var frameDu = correctedDu - _middleLastAppliedDu;
            if (frameDu == 0)
                return;
            _canvas.UpdateDragCommand(frameDu, 0);
            _middleLastAppliedDu = correctedDu;
            _lastDragU = u;
        }
        else if (ImGui.IsItemDeactivated())
        {
            if (_currentDragMode == DragMode.Middle)
            {
                _canvas.CompleteDragCommand();
                _currentDragMode = DragMode.None;
            }
            else if (_currentDragMode == DragMode.MiddleCustom)
            {
                _warpDrag.Complete();
                _currentDragMode = DragMode.None;
            }
        }
    }

    private void StartMiddleDrag(in Guid compositionSymbolId, double pressU)
    {
        if (_autoSelectKeyframesOnDrag)
            _canvas.KeyframeEditors.SelectAllKeyframes();

        _middleOrigPressU = pressU;
        _middleOrigStart = _range.Start;
        _middleOrigEnd = _range.End;
        _middleLastAppliedDu = 0;

        if (_handles.Count > 0)
        {
            _warpDrag.Begin(_lastComposition!, origHandleU: pressU,
                            prevBoundary: 0, nextBoundary: 0,
                            singleSegment: false,
                            segmentLeftU: 0, segmentRightU: 0,
                            pureTranslation: true,
                            trackHandlePositions: true,
                            useAllKeyframes: _autoSelectKeyframesOnDrag);
            _currentDragMode = DragMode.MiddleCustom;
        }
        else
        {
            _canvas.StartDragCommand(compositionSymbolId);
            _currentDragMode = DragMode.Middle;
        }
        _lastDragU = pressU;
    }

    /// <summary>
    /// Adjust the requested translation so that the SRI's start or end snaps to a nearby anchor.
    /// Whichever edge has the stronger snap (smaller delta) wins; Shift bypasses snapping.
    /// </summary>
    private double SnapMiddleTranslation(double rawDu)
    {
        if (ImGui.GetIO().KeyShift)
            return rawDu;

        var candidateStart = _middleOrigStart + rawDu;
        var candidateEnd = _middleOrigEnd + rawDu;

        var bestDelta = 0.0;
        var hasSnap = false;

        if (_snapHandler.TryCheckForSnapping(candidateStart, out var snappedStart, _canvas.Scale.X, _snapExclusions))
        {
            bestDelta = snappedStart - candidateStart;
            hasSnap = true;
        }

        if (_snapHandler.TryCheckForSnapping(candidateEnd, out var snappedEnd, _canvas.Scale.X, _snapExclusions))
        {
            var d = snappedEnd - candidateEnd;
            if (!hasSnap || Math.Abs(d) < Math.Abs(bestDelta))
            {
                bestDelta = d;
                hasSnap = true;
            }
        }

        return hasSnap ? rawDu + bestDelta : rawDu;
    }

    private void HandleWarpHandleDrag(Instance composition, int handleIdx)
    {
        _lastComposition = composition;
        var altHeld = ImGui.GetIO().KeyAlt;

        if (ImGui.IsItemActivated())
        {
            if (altHeld)
            {
                // Alt+press on a handle: candidate for toggle-removal on clean release.
                _pendingAltToggleIndex = handleIdx;
                return;
            }
            StartWarpHandleDrag(composition, handleIdx);
        }

        // Alt+press followed by drag → cancel the toggle and promote to a warp drag.
        if (_pendingAltToggleIndex == handleIdx && ImGui.IsItemActive()
            && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f))
        {
            _pendingAltToggleIndex = -1;
            StartWarpHandleDrag(composition, handleIdx);
        }

        if (_currentDragMode == DragMode.WarpHandle && _draggedWarpHandleIndex == handleIdx
            && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var rawU = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
            var (prev, next) = _handles.GetNeighbors(handleIdx, _range);
            const double minGap = 1e-4;
            var clamped = Math.Clamp(rawU, prev + minGap, next - minGap);
            _handles.SetValueUnsorted(handleIdx, clamped);
            _warpDrag.Update(clamped);
        }

        if (ImGui.IsItemDeactivated())
        {
            if (_pendingAltToggleIndex == handleIdx)
            {
                // Toggle on release-without-drag; defer the removal until after all per-frame
                // iterations so indices stay valid for the rest of this frame.
                _pendingRemoveHandleIndex = handleIdx;
                _pendingAltToggleIndex = -1;
                return;
            }

            if (_currentDragMode == DragMode.WarpHandle && _draggedWarpHandleIndex == handleIdx)
            {
                _warpDrag.Complete();
                _draggedWarpHandleIndex = -1;
                _currentDragMode = DragMode.None;
            }
        }
    }

    private void StartWarpHandleDrag(Instance composition, int handleIdx)
    {
        var (prev, next) = _handles.GetNeighbors(handleIdx, _range);
        _warpDrag.Begin(composition, origHandleU: _handles[handleIdx],
                        prevBoundary: prev, nextBoundary: next,
                        singleSegment: false,
                        segmentLeftU: 0, segmentRightU: 0,
                        pureTranslation: false,
                        trackHandlePositions: true,
                        useAllKeyframes: _autoSelectKeyframesOnDrag);
        _draggedWarpHandleIndex = handleIdx;
        _currentDragMode = DragMode.WarpHandle;
    }

    //
    // State ------------------------------------------------------------------
    //

    private enum DragMode
    {
        None,
        Middle,
        MiddleCustom,    // middle-translate when inner handles exist (tracks handle positions for undo)
        EdgeStart,
        EdgeEnd,
        EdgeStartCustom, // edge drag limited to the outer segment (inner handles exist)
        EdgeEndCustom,
        WarpHandle,
    }

    private readonly TimeLineCanvas _canvas;
    private readonly ValueSnapHandler _snapHandler;
    private readonly IValueSnapAttractor[] _snapExclusions;
    private readonly TimeWarpHandleSet _handles;
    private readonly TimeWarpDrag _warpDrag;
    private readonly SourceRegionIndicator _sourceRegion;

    private DragMode _currentDragMode = DragMode.None;
    private bool _autoSelectKeyframesOnDrag;
    private bool _lastHadKeyframeSelection;
    private double _lastDragU;
    private double _edgeDragOrigU;
    private TimeRange _range;
    private Instance? _lastComposition;

    private int _draggedWarpHandleIndex = -1;
    private int _pendingAltToggleIndex = -1;
    private int _pendingRemoveHandleIndex = -1;
    private double _pendingAltInsertU;
    private bool _pendingAltInsertActive;

    // Middle-drag snap state (captured once at drag-start).
    private double _middleOrigPressU;
    private double _middleOrigStart;
    private double _middleOrigEnd;
    private double _middleLastAppliedDu;
}
