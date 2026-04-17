#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Thin horizontal indicator drawn inside the timeline ruler showing the time range
/// of currently selected keyframes / clips. Start and end handles stretch the selection;
/// the middle section translates it.
/// When nothing is selected, it falls back to the extent of all visible keyframes so the
/// user can stretch/translate everything without pre-selecting.
///
/// TimeWarp handles (Alt+click on the bar): small circular markers that subdivide the
/// selection range into segments. Dragging a handle linearly retimes the segment's
/// keyframes / clip endpoints around it; the fixed neighbours (previous handle or SRI-Start,
/// next handle or SRI-End) pin the segment ends. If handles exist, dragging the SRI start or
/// end handle retimes only the outer segment rather than stretching the whole selection.
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
        _composition = composition;
        var dopeSheet = _canvas.DopeSheetArea;
        var layers = _canvas.LayersArea;

        // Keyframes: selection if it covers a positive range, else fall back to all keyframes.
        // Clips: only selected clips contribute.
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

        // Detect "selection just became empty" (user deselected everything) — drop handles.
        var hasKeyframeSelectionNow = !_autoSelectKeyframesOnDrag;
        if (_lastHadKeyframeSelection && !hasKeyframeSelectionNow)
            _warpHandles.Clear();
        _lastHadKeyframeSelection = hasKeyframeSelectionNow;

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
        var altHeld = ImGui.GetIO().KeyAlt;

        // Visible handles for this frame (those within the current range).
        _visibleWarpHandles.Clear();
        for (var i = 0; i < _warpHandles.Count; i++)
        {
            var u = _warpHandles[i];
            if (u >= _range.Start && u <= _range.End)
                _visibleWarpHandles.Add(i);
        }

        // Emit the middle hit-target first; edges + warp handles emitted later steal overlap.
        var middleHovered = false;
        var middleActive = false;
        var middlePressed = false;
        if (rightClamped - leftClamped > handleSize.X * 2)
        {
            var middleStart = xStart + handleSize.X;
            var middleEnd = xEnd - handleSize.X;
            ImGui.SetCursorScreenPos(new Vector2(middleStart, hitY));
            ImGui.SetNextItemAllowOverlap();
            ImGui.InvisibleButton("##SriMiddle", new Vector2(middleEnd - middleStart, hitHeight));
            middleHovered = ImGui.IsItemHovered();
            middleActive = ImGui.IsItemActive();
            middlePressed = ImGui.IsItemActivated();
            if (middleHovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            // Translate drag only when Alt is NOT held (Alt reserved for marker insert).
            // Continue driving an ongoing drag regardless of Alt state.
            if (!altHeld || _currentDragMode == DragMode.Middle || _currentDragMode == DragMode.MiddleCustom)
                HandleMiddleDrag(compositionSymbolId);
        }

        // Start handle
        ImGui.SetCursorScreenPos(new Vector2(xStart - handleSize.X, hitY));
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton("##SriStart", new Vector2(handleSize.X * 2, hitHeight));
        var startHovered = ImGui.IsItemHovered();
        var startActive = ImGui.IsItemActive();
        if (startHovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        HandleEdgeDrag(compositionSymbolId, isStart: true);

        // End handle
        ImGui.SetCursorScreenPos(new Vector2(xEnd - handleSize.X, hitY));
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton("##SriEnd", new Vector2(handleSize.X * 2, hitHeight));
        var endHovered = ImGui.IsItemHovered();
        var endActive = ImGui.IsItemActive();
        if (endHovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        HandleEdgeDrag(compositionSymbolId, isStart: false);

        // Warp handle interaction (drag / alt+click toggle). Emitted last = on top.
        var warpHandleRadius = 4 * scale;
        var warpHitSize = new Vector2(warpHandleRadius * 2 + 2, hitHeight);
        var warpHandleHoveredIndex = -1;
        for (var vi = 0; vi < _visibleWarpHandles.Count; vi++)
        {
            var handleIdx = _visibleWarpHandles[vi];
            var hx = _canvas.TransformX((float)_warpHandles[handleIdx]);
            ImGui.SetCursorScreenPos(new Vector2(hx - warpHitSize.X * 0.5f, hitY));
            ImGui.InvisibleButton($"##SriWarp{handleIdx}", warpHitSize);

            if (ImGui.IsItemHovered())
            {
                warpHandleHoveredIndex = handleIdx;
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            HandleWarpHandleDrag(compositionSymbolId, handleIdx);
        }

        // Alt+click on middle (no existing handle under cursor) inserts a handle.
        // Alt+click on existing handle is handled in HandleWarpHandleDrag (toggle/remove).
        if (altHeld && middlePressed && warpHandleHoveredIndex < 0 && _currentDragMode == DragMode.None)
        {
            // Mark pending insert — commit on release without drag.
            _pendingAltInsertU = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
            _pendingAltInsertActive = true;
        }

        if (_pendingAltInsertActive && middleActive && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f * scale))
        {
            // Dragging after Alt+press: cancel the pending insert (Alt+drag is reserved for other uses).
            _pendingAltInsertActive = false;
        }

        if (_pendingAltInsertActive && !middleActive)
        {
            // Release without drag — commit the insert.
            InsertWarpHandle(_pendingAltInsertU);
            _pendingAltInsertActive = false;
        }

        // Determine opacities per sub-section.
        var baseOpacity = _autoSelectKeyframesOnDrag ? 0.3f : 0.75f;
        var alt = altHeld && (middleHovered || middleActive);
        var lineColor = alt
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

        DrawHandle(drawList, new Vector2(xStart, lineY + 0.5f), handleSize, startColor);
        DrawHandle(drawList, new Vector2(xEnd, lineY + 0.5f), handleSize, endColor);

        // Draw the warp handles themselves as small filled circles.
        // Skip any that were queued for removal this frame — their index is about to become invalid.
        for (var vi = 0; vi < _visibleWarpHandles.Count; vi++)
        {
            var handleIdx = _visibleWarpHandles[vi];
            if (handleIdx == _pendingRemoveHandleIndex || handleIdx >= _warpHandles.Count)
                continue;
            var hx = _canvas.TransformX((float)_warpHandles[handleIdx]);
            var center = new Vector2(hx, lineY + 0.5f);
            var hovered = handleIdx == warpHandleHoveredIndex || handleIdx == _draggedWarpHandleIndex;
            var color = hovered ? UiColors.StatusAutomated : UiColors.ForegroundFull;
            drawList.AddCircleFilled(center, warpHandleRadius, color);
        }

        // Alt+hover preview: outlined circle at mouse X (or on an existing handle to signal toggle).
        if (altHeld && middleHovered && _currentDragMode == DragMode.None)
        {
            float previewX;
            var previewColor = UiColors.StatusAutomated;
            if (warpHandleHoveredIndex >= 0 && warpHandleHoveredIndex < _warpHandles.Count
                && warpHandleHoveredIndex != _pendingRemoveHandleIndex)
            {
                previewX = _canvas.TransformX((float)_warpHandles[warpHandleHoveredIndex]);
            }
            else
            {
                previewX = ImGui.GetIO().MousePos.X;
            }

            drawList.AddCircle(new Vector2(previewX, lineY + 0.5f), warpHandleRadius + 1, previewColor, 12, 1.2f);
        }

        // Apply the deferred handle removal now that all per-frame iterations have finished.
        if (_pendingRemoveHandleIndex >= 0 && _pendingRemoveHandleIndex < _warpHandles.Count)
        {
            _warpHandles.RemoveAt(_pendingRemoveHandleIndex);
        }
        _pendingRemoveHandleIndex = -1;
    }

    private static void DrawHandle(ImDrawListPtr drawList, Vector2 center, Vector2 size, Color color)
    {
        var hx = size.X * 0.5f;
        var hy = size.Y * 0.5f;
        drawList.AddRectFilled(new Vector2(center.X - hx, center.Y - hy),
                               new Vector2(center.X + hx, center.Y + hy),
                               color);
    }

    private void HandleEdgeDrag(in Guid compositionSymbolId, bool isStart)
    {
        var originalU = isStart ? _range.Start : _range.End;
        var origin = isStart ? _range.End : _range.Start;

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);

            // When handles exist, dragging an edge retimes only the outer segment (up to first/last handle).
            // We use a custom warp-drag path instead of the canvas-wide stretch command.
            var hasInnerHandles = HasAnyVisibleWarpHandle();

            if (_currentDragMode == DragMode.None)
            {
                if (_autoSelectKeyframesOnDrag)
                    _canvas.DopeSheetArea.SelectAllKeyframes();

                if (hasInnerHandles)
                {
                    var (prev, next, origEdgeU) = isStart
                                                      ? (GetFirstVisibleWarpHandleU(), (double)0, _range.Start)
                                                      : (GetLastVisibleWarpHandleU(), (double)0, _range.End);
                    // For start edge: movingU = Start, fixed boundary = first inner handle.
                    // For end edge: movingU = End, fixed boundary = last inner handle.
                    // Model as a single-segment warp where the "handle" is the edge itself.
                    var boundary = prev;
                    BeginCustomWarpDrag(compositionSymbolId, origHandleU: origEdgeU,
                                        prevBoundary: isStart ? double.NegativeInfinity : boundary,
                                        nextBoundary: isStart ? boundary : double.PositiveInfinity,
                                        restrictToSingleSegment: true,
                                        segmentLeftU: isStart ? Math.Min(origEdgeU, boundary) : boundary,
                                        segmentRightU: isStart ? boundary : Math.Max(origEdgeU, boundary));
                    _currentDragMode = isStart ? DragMode.EdgeStartCustom : DragMode.EdgeEndCustom;
                }
                else
                {
                    _canvas.StartDragCommand(compositionSymbolId);
                    _currentDragMode = isStart ? DragMode.EdgeStart : DragMode.EdgeEnd;
                }

                _lastDragU = originalU;
            }

            if (_currentDragMode == DragMode.EdgeStartCustom || _currentDragMode == DragMode.EdgeEndCustom)
            {
                UpdateCustomWarpDrag(u);
                _lastDragU = u;
                return;
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
        else if (ImGui.IsItemDeactivated() &&
                 (_currentDragMode == (isStart ? DragMode.EdgeStart : DragMode.EdgeEnd)
                  || _currentDragMode == (isStart ? DragMode.EdgeStartCustom : DragMode.EdgeEndCustom)))
        {
            if (_currentDragMode == DragMode.EdgeStart || _currentDragMode == DragMode.EdgeEnd)
                _canvas.CompleteDragCommand();
            else
                CompleteCustomWarpDrag();

            _currentDragMode = DragMode.None;
        }
    }

    private void HandleMiddleDrag(in Guid compositionSymbolId)
    {
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);

            if (_currentDragMode == DragMode.None)
            {
                // With handles present we use a custom path so handle positions can be snapshotted
                // into the undo command alongside the keyframe / clip mutations.
                if (_warpHandles.Count > 0)
                {
                    if (_autoSelectKeyframesOnDrag)
                        _canvas.DopeSheetArea.SelectAllKeyframes();
                    BeginCustomWarpDrag(compositionSymbolId, origHandleU: u,
                                        prevBoundary: 0, nextBoundary: 0,
                                        restrictToSingleSegment: false,
                                        segmentLeftU: 0, segmentRightU: 0,
                                        pureTranslation: true,
                                        trackHandlePositions: true);
                    _currentDragMode = DragMode.MiddleCustom;
                    _lastDragU = u;
                    return;
                }

                if (_autoSelectKeyframesOnDrag)
                    _canvas.DopeSheetArea.SelectAllKeyframes();
                _canvas.StartDragCommand(compositionSymbolId);
                _lastDragU = u;
                _currentDragMode = DragMode.Middle;
                return;
            }

            if (_currentDragMode == DragMode.MiddleCustom)
            {
                UpdateCustomWarpDrag(u);
                _lastDragU = u;
                return;
            }

            if (_currentDragMode != DragMode.Middle)
                return;

            var du = u - _lastDragU;
            if (du == 0)
                return;

            _canvas.UpdateDragCommand(du, 0);
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
                CompleteCustomWarpDrag();
                _currentDragMode = DragMode.None;
            }
        }
    }

    private void HandleWarpHandleDrag(in Guid compositionSymbolId, int handleIdx)
    {
        var altHeld = ImGui.GetIO().KeyAlt;

        if (ImGui.IsItemActivated())
        {
            if (altHeld)
            {
                // Alt+press: candidate for toggle-removal on clean release.
                _pendingAltToggleIndex = handleIdx;
                return;
            }

            // Start a warp drag around this handle (no Alt required once the handle exists).
            var (prev, next) = GetNeighborBoundaries(handleIdx);
            BeginCustomWarpDrag(compositionSymbolId, origHandleU: _warpHandles[handleIdx],
                                prevBoundary: prev, nextBoundary: next,
                                restrictToSingleSegment: false,
                                segmentLeftU: 0, segmentRightU: 0,
                                pureTranslation: false,
                                trackHandlePositions: true);
            _draggedWarpHandleIndex = handleIdx;
            _currentDragMode = DragMode.WarpHandle;
        }

        // If user alt-pressed then dragged past click threshold, abandon the toggle and
        // promote to a warp drag (Alt+drag on a handle behaves the same as drag without Alt).
        if (_pendingAltToggleIndex == handleIdx && ImGui.IsItemActive()
            && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 2f))
        {
            _pendingAltToggleIndex = -1;
            var (prev, next) = GetNeighborBoundaries(handleIdx);
            BeginCustomWarpDrag(compositionSymbolId, origHandleU: _warpHandles[handleIdx],
                                prevBoundary: prev, nextBoundary: next,
                                restrictToSingleSegment: false,
                                segmentLeftU: 0, segmentRightU: 0,
                                pureTranslation: false,
                                trackHandlePositions: true);
            _draggedWarpHandleIndex = handleIdx;
            _currentDragMode = DragMode.WarpHandle;
        }

        if (_currentDragMode == DragMode.WarpHandle && _draggedWarpHandleIndex == handleIdx
            && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            var rawU = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
            var (prev, next) = GetNeighborBoundaries(handleIdx);
            const double minGap = 1e-4;
            var clamped = Math.Clamp(rawU, prev + minGap, next - minGap);
            _warpHandles[handleIdx] = clamped;
            UpdateCustomWarpDrag(clamped);
        }

        if (ImGui.IsItemDeactivated())
        {
            if (_pendingAltToggleIndex == handleIdx)
            {
                // Release without drag — toggle (remove) this handle. Defer the actual removal
                // until after the interaction/render loops so list indices stay valid for the
                // remaining iterations of this frame.
                _pendingRemoveHandleIndex = handleIdx;
                _pendingAltToggleIndex = -1;
                return;
            }

            if (_currentDragMode == DragMode.WarpHandle && _draggedWarpHandleIndex == handleIdx)
            {
                CompleteCustomWarpDrag();
                _draggedWarpHandleIndex = -1;
                _currentDragMode = DragMode.None;
            }
        }
    }

    private (double prev, double next) GetNeighborBoundaries(int handleIdx)
    {
        var u = _warpHandles[handleIdx];
        double prev = _range.Start;
        double next = _range.End;
        for (var i = 0; i < _warpHandles.Count; i++)
        {
            if (i == handleIdx) continue;
            var other = _warpHandles[i];
            if (other < u && other > prev) prev = other;
            else if (other > u && other < next) next = other;
        }
        return (prev, next);
    }

    private bool HasAnyVisibleWarpHandle() => _visibleWarpHandles.Count > 0;

    private double GetFirstVisibleWarpHandleU()
    {
        var best = double.PositiveInfinity;
        for (var vi = 0; vi < _visibleWarpHandles.Count; vi++)
        {
            var u = _warpHandles[_visibleWarpHandles[vi]];
            if (u < best) best = u;
        }
        return best;
    }

    private double GetLastVisibleWarpHandleU()
    {
        var best = double.NegativeInfinity;
        for (var vi = 0; vi < _visibleWarpHandles.Count; vi++)
        {
            var u = _warpHandles[_visibleWarpHandles[vi]];
            if (u > best) best = u;
        }
        return best;
    }

    private void InsertWarpHandle(double u)
    {
        if (u <= _range.Start || u >= _range.End)
            return;
        _warpHandles.Add(u);
        _warpHandles.Sort();
    }

    public void ClearTimeWarpHandles()
    {
        _warpHandles.Clear();
    }

    /// <summary>Restore handle positions from an undo/redo snapshot.</summary>
    internal void RestoreTimeWarpHandlesForUndo(IReadOnlyList<double> snapshot)
    {
        _warpHandles.Clear();
        for (var i = 0; i < snapshot.Count; i++)
            _warpHandles.Add(snapshot[i]);
    }

    //
    // Custom piecewise-linear warp drag ---------------------------------------
    //

    private void BeginCustomWarpDrag(Guid compositionSymbolId,
                                     double origHandleU,
                                     double prevBoundary,
                                     double nextBoundary,
                                     bool restrictToSingleSegment,
                                     double segmentLeftU,
                                     double segmentRightU,
                                     bool pureTranslation = false,
                                     bool trackHandlePositions = false)
    {
        _warpDrag.Reset();
        _warpDrag.OrigHandleU = origHandleU;
        _warpDrag.PrevBoundary = prevBoundary;
        _warpDrag.NextBoundary = nextBoundary;
        _warpDrag.SingleSegment = restrictToSingleSegment;
        _warpDrag.SegmentLeftU = segmentLeftU;
        _warpDrag.SegmentRightU = segmentRightU;
        _warpDrag.PureTranslation = pureTranslation;

        if (trackHandlePositions)
        {
            // Capture the handle layout for undo/redo of this drag and for per-frame re-translation.
            _warpDrag.OrigHandles.Clear();
            for (var i = 0; i < _warpHandles.Count; i++)
                _warpDrag.OrigHandles.Add(_warpHandles[i]);
            _warpDrag.HandlesCommand = new SetTimeWarpHandlesCommand(this, _warpHandles);
        }

        // Snapshot selected keyframes (or all if fallback) that lie inside the affected range.
        var dopeSheet = _canvas.DopeSheetArea;
        var source = _autoSelectKeyframesOnDrag
                         ? dopeSheet.EnumerateAllKeyframes()
                         : dopeSheet.EnumerateSelectedKeyframes();

        foreach (var def in source)
        {
            // Pure translation mode affects every target regardless of position.
            if (!pureTranslation
                && !InsideAffectedRange(def.U, restrictToSingleSegment, segmentLeftU, segmentRightU, prevBoundary, nextBoundary))
                continue;
            _warpDrag.Keys.Add(def);
            _warpDrag.KeyOrigU.Add(def.U);
        }

        if (_warpDrag.Keys.Count > 0)
        {
            // Curves for ChangeKeyframesCommand: need the full curve set (restricting is harmless).
            dopeSheet.CopyAllCurvesTo(_warpDragCurves);
            _warpDrag.KeyframesCommand = new ChangeKeyframesCommand(_warpDrag.Keys, _warpDragCurves);
        }

        // Snapshot selected clip endpoints independently.
        foreach (var clip in _canvas.LayersArea.EnumerateSelectedClips())
        {
            _warpDrag.Clips.Add(clip);
            _warpDrag.ClipOrigStart.Add(clip.TimeRange.Start);
            _warpDrag.ClipOrigEnd.Add(clip.TimeRange.End);
        }

        if (_warpDrag.Clips.Count > 0)
        {
            _warpDragClipBuffer.Clear();
            for (var i = 0; i < _warpDrag.Clips.Count; i++)
                _warpDragClipBuffer.Add(_warpDrag.Clips[i]);
            _warpDrag.ClipsCommand = new MoveTimeClipsCommand(_composition!, _warpDragClipBuffer);
        }
    }

    private static bool InsideAffectedRange(double u, bool singleSegment, double segLeft, double segRight, double prev, double next)
    {
        if (singleSegment)
            return u >= segLeft && u <= segRight;
        return u >= prev && u <= next;
    }

    private void UpdateCustomWarpDrag(double currentHandleU)
    {
        var origU = _warpDrag.OrigHandleU;
        var prev = _warpDrag.PrevBoundary;
        var next = _warpDrag.NextBoundary;

        if (_warpDrag.PureTranslation)
        {
            var du = currentHandleU - origU;
            for (var i = 0; i < _warpDrag.Keys.Count; i++)
                _warpDrag.Keys[i].U = _warpDrag.KeyOrigU[i] + du;

            for (var i = 0; i < _warpDrag.Clips.Count; i++)
            {
                var clip = _warpDrag.Clips[i];
                clip.TimeRange.Start = _warpDrag.ClipOrigStart[i] + (float)du;
                clip.TimeRange.End = _warpDrag.ClipOrigEnd[i] + (float)du;
            }

            if (_warpDrag.HandlesCommand != null)
            {
                for (var i = 0; i < _warpDrag.OrigHandles.Count && i < _warpHandles.Count; i++)
                    _warpHandles[i] = _warpDrag.OrigHandles[i] + du;
            }

            AnimationParameterEditing.CurvesTablesNeedsRefresh = true;
            return;
        }

        // Remap each snapshot key by piecewise linear around (prev, origU, next) with new midpoint = currentHandleU.
        for (var i = 0; i < _warpDrag.Keys.Count; i++)
        {
            var t = _warpDrag.KeyOrigU[i];
            _warpDrag.Keys[i].U = RemapPiecewise(t, origU, currentHandleU, prev, next, _warpDrag.SingleSegment, _warpDrag.SegmentLeftU, _warpDrag.SegmentRightU);
        }

        // Clips: retime start and end independently.
        for (var i = 0; i < _warpDrag.Clips.Count; i++)
        {
            var clip = _warpDrag.Clips[i];
            var origStart = _warpDrag.ClipOrigStart[i];
            var origEnd = _warpDrag.ClipOrigEnd[i];
            var newStart = (float)RemapPiecewise(origStart, origU, currentHandleU, prev, next, _warpDrag.SingleSegment, _warpDrag.SegmentLeftU, _warpDrag.SegmentRightU);
            var newEnd = (float)RemapPiecewise(origEnd, origU, currentHandleU, prev, next, _warpDrag.SingleSegment, _warpDrag.SegmentLeftU, _warpDrag.SegmentRightU);
            if (newEnd - newStart < 1 / 60f)
                newEnd = newStart + 1 / 60f;
            clip.TimeRange.Start = newStart;
            clip.TimeRange.End = newEnd;
        }

        // Keep curve tables in sync so the dope sheet reflects the moved keys without waiting.
        AnimationParameterEditing.CurvesTablesNeedsRefresh = true;
    }

    private static double RemapPiecewise(double t, double origU, double newU, double prev, double next,
                                         bool singleSegment, double segLeft, double segRight)
    {
        if (singleSegment)
        {
            // One-segment warp: (segLeft..segRight), where either segLeft or segRight is the moving point.
            if (t < segLeft || t > segRight) return t;
            // Determine which end is moving: whichever equals origU.
            if (Math.Abs(segLeft - origU) < 1e-9)
            {
                // Left end is moving, right end fixed.
                var right = segRight;
                var lenOrig = right - origU;
                if (Math.Abs(lenOrig) < 1e-9) return t;
                var ratio = (t - origU) / lenOrig; // 0 at movingPoint, 1 at right
                return newU + ratio * (right - newU);
            }
            else
            {
                // Right end is moving, left end fixed.
                var left = segLeft;
                var lenOrig = origU - left;
                if (Math.Abs(lenOrig) < 1e-9) return t;
                var ratio = (t - left) / lenOrig; // 0 at left, 1 at movingPoint
                return left + ratio * (newU - left);
            }
        }

        if (t < prev || t > next) return t;
        if (t <= origU)
        {
            var lenOrig = origU - prev;
            if (Math.Abs(lenOrig) < 1e-9) return t;
            var ratio = (t - prev) / lenOrig;
            return prev + ratio * (newU - prev);
        }
        else
        {
            var lenOrig = next - origU;
            if (Math.Abs(lenOrig) < 1e-9) return t;
            var ratio = (t - origU) / lenOrig;
            return newU + ratio * (next - newU);
        }
    }

    private void CompleteCustomWarpDrag()
    {
        if (_warpDrag.Keys.Count == 0 && _warpDrag.Clips.Count == 0)
            return;

        var commands = new List<ICommand>(3);
        if (_warpDrag.KeyframesCommand != null)
        {
            _warpDrag.KeyframesCommand.StoreCurrentValues();
            commands.Add(_warpDrag.KeyframesCommand);
        }
        if (_warpDrag.ClipsCommand != null)
        {
            _warpDrag.ClipsCommand.StoreCurrentValues();
            commands.Add(_warpDrag.ClipsCommand);
        }
        if (_warpDrag.HandlesCommand != null)
        {
            _warpDrag.HandlesCommand.StoreCurrentValues(_warpHandles);
            commands.Add(_warpDrag.HandlesCommand);
        }

        if (commands.Count > 0)
        {
            var macro = new MacroCommand("TimeWarp", commands);
            UndoRedoStack.AddAndExecute(macro);
        }

        _warpDrag.Reset();
    }

    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        if (!_range.IsValid || _range.Duration <= 0)
            return;
        snapResult.TryToImproveWithAnchorValue(_range.Start);
        snapResult.TryToImproveWithAnchorValue(_range.End);
    }

    private enum DragMode
    {
        None,
        Middle,
        MiddleCustom,    // middle drag when inner handles exist (tracks handle positions for undo)
        EdgeStart,
        EdgeEnd,
        EdgeStartCustom, // edge drag when inner handles exist (warp-retime only the outer segment)
        EdgeEndCustom,
        WarpHandle,
    }

    private sealed class WarpDragState
    {
        public double OrigHandleU;
        public double PrevBoundary;
        public double NextBoundary;
        public bool SingleSegment;
        public double SegmentLeftU;
        public double SegmentRightU;
        public bool PureTranslation;
        public readonly List<VDefinition> Keys = new(64);
        public readonly List<double> KeyOrigU = new(64);
        public readonly List<TimeClip> Clips = new(16);
        public readonly List<float> ClipOrigStart = new(16);
        public readonly List<float> ClipOrigEnd = new(16);
        public readonly List<double> OrigHandles = new(8);
        public ChangeKeyframesCommand? KeyframesCommand;
        public MoveTimeClipsCommand? ClipsCommand;
        public SetTimeWarpHandlesCommand? HandlesCommand;

        public void Reset()
        {
            Keys.Clear();
            KeyOrigU.Clear();
            Clips.Clear();
            ClipOrigStart.Clear();
            ClipOrigEnd.Clear();
            OrigHandles.Clear();
            KeyframesCommand = null;
            ClipsCommand = null;
            HandlesCommand = null;
            PureTranslation = false;
        }
    }

    private DragMode _currentDragMode = DragMode.None;
    private bool _autoSelectKeyframesOnDrag;
    private bool _lastHadKeyframeSelection;
    private double _lastDragU;
    private TimeRange _range;

    // TimeWarp handles (absolute U values).
    private readonly List<double> _warpHandles = new(8);
    private readonly List<int> _visibleWarpHandles = new(8);
    private int _draggedWarpHandleIndex = -1;
    private int _pendingAltToggleIndex = -1;
    private int _pendingRemoveHandleIndex = -1;
    private double _pendingAltInsertU;
    private bool _pendingAltInsertActive;
    private Instance? _composition;

    private readonly WarpDragState _warpDrag = new();
    private readonly List<Curve> _warpDragCurves = new(32);
    private readonly List<TimeClip> _warpDragClipBuffer = new(16);

    private readonly TimeLineCanvas _canvas;
    private readonly ValueSnapHandler _snapHandler;
    private readonly IValueSnapAttractor[] _snapExclusions;
}
