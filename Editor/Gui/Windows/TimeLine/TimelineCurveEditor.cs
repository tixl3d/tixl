using System.Text;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.InputUi.CombinedInputs;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Animation;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Interaction.WithCurves;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.Selection;

// ReSharper disable CompareOfFloatsByEqualityOperator

namespace T3.Editor.Gui.Windows.TimeLine;

[HelpUiID("CurveEditor")]
internal sealed class TimelineCurveEditor : AnimationParameterEditing, ITimeObjectManipulation, IValueSnapAttractor
{
    internal override int? GetHoveredKeyframeUniqueId() => TimeLineCanvas.HoveredKeyframeUniqueId;

    public TimelineCurveEditor(TimeLineCanvas timeLineCanvas, ValueSnapHandler snapHandlerForU, ValueSnapHandler snapHandlerV)
        : base(timeLineCanvas.SharedSelectedKeyframes)
    {
        _snapHandlerU = snapHandlerForU;
        _snapHandlerV = snapHandlerV;
        TimeLineCanvas = timeLineCanvas;
    }

    private readonly StringBuilder _stringBuilder = new(100);
    private readonly List<VDefinition> _visibleKeyframes = new(1000);

    public void Draw(Instance compositionOp, List<TimeLineCanvas.AnimationParameter> animationParameters,
                     bool fitCurvesVertically = false, bool fitVerticalOnly = false,
                     HashSet<int>? parameterHashFilter = null,
                     bool showParameterList = true)
    {
        _visibleKeyframes.Clear();
        AnimationParameters = animationParameters;

        if (fitVerticalOnly)
        {
            // In inline mode, fit only the expanded parameters' keyframes — otherwise the
            // pane scales to include curves of non-expanded params that aren't even visible.
            var pointsForFit = parameterHashFilter != null
                                   ? EnumerateKeyframesInFilter(animationParameters, parameterHashFilter)
                                   : GetSelectedOrAllPoints();
            if (TryGetBoundsOnCanvas(pointsForFit, out var bounds))
                TimeLineCanvas.Current?.SetVerticalScopeToCanvasArea(bounds, flipY: true, paddingFraction: 0.15f);
        }
        else if (fitCurvesVertically)
        {
            ViewAllOrSelectedKeys(alsoChangeTimeRange: false);
        }

        ImGui.BeginGroup();
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.ChannelsSplit(3);
            if (UserActions.FocusSelection.Triggered())
                ViewAllOrSelectedKeys(alsoChangeTimeRange: true);

            if (UserActions.Duplicate.Triggered())
                DuplicateSelectedKeyframes();

            var lineStartPosition = ImGui.GetCursorPos();
            var visibleCurveCount = 0;

            ImGui.PushFont(Fonts.FontSmall);
            var compositionSymbolId = compositionOp.GetSymbolUi().Symbol.Id;
            foreach (var param in animationParameters)
            {
                if (parameterHashFilter != null && !parameterHashFilter.Contains(param.Hash))
                    continue;

                ImGui.PushID(param.Input.GetHashCode());
                drawList.ChannelsSetCurrent(1);
                var hash = param.Input.GetHashCode();
                var isParamPinned = _pinnedParameterComponents.ContainsKey(hash);
                var isParamHovered = false;

                var cursorPosition = lineStartPosition;

                if (showParameterList)
                {
                    _stringBuilder.Clear();
                    _stringBuilder.Append(param.Instance.Symbol.Name);
                    _stringBuilder.Append('.');
                    _stringBuilder.Append(param.Input.Input.Name);
                    var paramName = _stringBuilder.ToString();

                    ImGui.SetCursorPos(cursorPosition);
                    if (DrawPinButton(isParamPinned, paramName))
                    {
                        if (isParamPinned)
                        {
                            _pinnedParameterComponents.Remove(hash);
                            isParamPinned = false;
                        }
                        else
                        {
                            _pinnedParameterComponents[hash] = 0xffff;
                            isParamPinned = true;
                        }
                    }

                    cursorPosition += new Vector2(ImGui.GetItemRectSize().X, 0);
                    isParamHovered = ImGui.IsItemHovered();
                }

                var curveNames = param.Input.ValueType == typeof(Vector4)
                                     ? DopeSheetArea.ColorCurveNames
                                     : DopeSheetArea.CurveNames;

                var curveIndex = 0;
                foreach (var curve in param.Curves)
                {
                    var bit = 1 << curveIndex;
                    bool isParamComponentPinned;
                    var isParamComponentHovered = false;

                    if (showParameterList)
                    {
                        ImGui.SetCursorPos(cursorPosition);
                        var componentName = curveNames[curveIndex % curveNames.Length];
                        isParamComponentPinned = isParamPinned && (_pinnedParameterComponents[hash] & bit) != 0;
                        if (DrawPinButton(isParamComponentPinned, componentName))
                        {
                            if (isParamComponentPinned)
                            {
                                var flags = _pinnedParameterComponents[hash] ^ bit;
                                if (flags == 0)
                                {
                                    _pinnedParameterComponents.Remove(hash);
                                    isParamPinned = false;
                                }
                                else
                                {
                                    _pinnedParameterComponents[hash] = flags;
                                }
                            }
                            else
                            {
                                if (isParamPinned)
                                    _pinnedParameterComponents[hash] |= bit;
                                else
                                    _pinnedParameterComponents[hash] = bit;
                            }

                            isParamComponentPinned = !isParamComponentPinned;
                        }

                        cursorPosition += new Vector2(ImGui.GetItemRectSize().X, 0);
                        ImGui.SetCursorPos(cursorPosition);
                        isParamComponentHovered = ImGui.IsItemHovered();
                    }
                    else
                    {
                        // Inline mode: no param-list pinning; component visibility comes from TimeLineCanvas.VisibleComponentMask.
                        // Empty/missing entry means "all components visible".
                        isParamComponentPinned = true;
                    }

                    drawList.ChannelsSetCurrent(0);

                    var shouldDrawCurve = showParameterList
                                              ? (_pinnedParameterComponents.Count == 0 || isParamComponentPinned)
                                              : IsComponentVisible(param.Hash, bit);
                    if (shouldDrawCurve)
                    {
                        var color = param.Curves.Length == 1 ? DopeSheetArea.GrayCurveColor
                            : DopeSheetArea.CurveColors[curveIndex % DopeSheetArea.CurveColors.Length];

                        // Cross-view hover: in inline mode, this curve is "emphasized" if the
                        // shared hover state points at its parameter and either no specific
                        // component is selected or this curve's bit matches.
                        var canvas = TimeLineCanvas;
                        var crossViewHovered = !showParameterList
                                               && canvas.HoveredParameterHash == param.Hash
                                               && (canvas.HoveredComponentBit == 0 || canvas.HoveredComponentBit == bit);
                        DrawCurveLine(curve, canvas, color,
                                      isParamHovered: isParamHovered || isParamComponentHovered || crossViewHovered);
                        drawList.ChannelsSetCurrent(1);
                        visibleCurveCount++;

                        // Curve-line hit-test (inline mode only): if the mouse hovers this
                        // polyline and no keyframe-level hover has claimed the state yet,
                        // mark this param + component as hovered so other-param curves fade.
                        if (!showParameterList
                            && TimeLineCanvas.HoveredKeyframeUniqueId == null
                            && ImGui.IsWindowHovered()
                            && IsMouseOverCurve(curve, TimeLineCanvas, ImGui.GetMousePos(), CurveHoverToleranceSquared))
                        {
                            TimeLineCanvas.HoveredParameterHash = param.Hash;
                            TimeLineCanvas.HoveredComponentBit = bit;
                        }

                        var keyframes = curve.GetVDefinitions();
                        var keyframeCount = keyframes.Count;
                        for (var ki = 0; ki < keyframeCount; ki++)
                        {
                            var keyframe = keyframes[ki];
                            var isSelected = SelectedKeyframes.Contains(keyframe);
                            var isNeighborOfSelected = !isSelected
                                                       && ((ki > 0 && SelectedKeyframes.Contains(keyframes[ki - 1]))
                                                           || (ki < keyframeCount - 1 && SelectedKeyframes.Contains(keyframes[ki + 1])));
                            var prevKey = ki > 0 ? keyframes[ki - 1] : null;
                            var nextKey = ki < keyframeCount - 1 ? keyframes[ki + 1] : null;
                            CurvePoint.Draw(compositionSymbolId, keyframe, TimeLineCanvas, isSelected, this, isNeighborOfSelected, prevKey, nextKey);
                            _visibleKeyframes.Add(keyframe);

                            // Keyframe-level hover wins over curve-line hover — it's more
                            // specific. The "key<UniqueId>" invisible button is the last item
                            // CurvePoint registers, so IsItemHovered reflects its state.
                            if (!showParameterList && ImGui.IsItemHovered())
                            {
                                TimeLineCanvas.HoveredParameterHash = param.Hash;
                                TimeLineCanvas.HoveredComponentBit = bit;
                                TimeLineCanvas.HoveredKeyframeUniqueId = keyframe.UniqueId;
                            }
                        }

                        HandleCreateNewKeyframes(curve);
                    }

                    curveIndex++;
                }

                ImGui.PopID();
                if (showParameterList)
                    lineStartPosition += new Vector2(0, 24);
            }

            drawList.ChannelsMerge();
            ImGui.PopFont();
            if (_addKeyframesCommands.Count > 0)
            {
                var command = new MacroCommand("Insert keyframes", _addKeyframesCommands);
                UndoRedoStack.AddAndExecute(command);
                _addKeyframesCommands.Clear();
            }

            if (visibleCurveCount == 0 && _pinnedParameterComponents.Count > 0)
            {
                _pinnedParameterComponents.Clear();
            }

            DrawContextMenu(compositionOp);
        }
        ImGui.EndGroup();

        RebuildCurveTables();
    }

    /// <summary>
    /// Compute the canvas-space bounds for an auto-fit. Used by <see cref="InlineCurveArea"/>
    /// so it can apply the fit to its own V scope rather than the outer timeline canvas.
    /// </summary>
    public bool TryGetFitBounds(List<TimeLineCanvas.AnimationParameter> animationParameters,
                                HashSet<int>? parameterHashFilter,
                                out ImRect bounds)
    {
        var points = parameterHashFilter != null
                         ? EnumerateKeyframesInFilter(animationParameters, parameterHashFilter)
                         : GetSelectedOrAllPoints();
        return TryGetBoundsOnCanvas(points, out bounds);
    }

    private static IEnumerable<VDefinition> EnumerateKeyframesInFilter(
        List<TimeLineCanvas.AnimationParameter> animationParameters,
        HashSet<int> parameterHashFilter)
    {
        for (var i = 0; i < animationParameters.Count; i++)
        {
            var p = animationParameters[i];
            if (!parameterHashFilter.Contains(p.Hash))
                continue;
            foreach (var curve in p.Curves)
                foreach (var v in curve.GetVDefinitions())
                    yield return v;
        }
    }

    private bool IsComponentVisible(int paramHash, int bit)
    {
        // Missing entry in the shared mask = "all components visible" (default on first expand).
        if (!TimeLineCanvas.VisibleComponentMask.TryGetValue(paramHash, out var mask))
            return true;
        return (mask & bit) != 0;
    }

    private void HandleCreateNewKeyframes(Curve curve)
    {
        var hoverNewKeyframe = !ImGui.IsAnyItemActive()
                               && ImGui.IsWindowHovered()
                               && ImGui.GetIO().KeyAlt
                               && ImGui.IsWindowHovered();
        if (!hoverNewKeyframe)
            return;

        var hoverTime = TimeLineCanvas.InverseTransformX(ImGui.GetIO().MousePos.X);
        if (TimeLineCanvas.SnapHandlerForU.TryCheckForSnapping(hoverTime, out var snappedValue, TimeLineCanvas.Scale.X))
        {
            hoverTime = (float)snappedValue;
        }

        if (ImGui.IsMouseReleased(0))
        {
            var dragDistance = ImGui.GetIO().MouseDragMaxDistanceAbs[0].Length();
            if (dragDistance < 2)
            {
                _addKeyframesCommands.Add(InsertNewKeyframe(curve, hoverTime));
            }
        }
        else
        {
            var sampledValue = (float)curve.GetSampledValue(hoverTime);
            var posOnCanvas = new Vector2(hoverTime, sampledValue);
            var posOnScreen = TimeLineCanvas.TransformPosition(posOnCanvas)
                              - new Vector2((int)(KeyframeIconWidth / 2f -1 ),(int)(KeyframeIconWidth / 2f-1 )  );
            Icons.DrawIconAtScreenPosition(Icon.CurveKeyframe, posOnScreen);
        }

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }

    private readonly List<AddKeyframesCommand> _addKeyframesCommands = new();

    private static AddKeyframesCommand InsertNewKeyframe(Curve curve, float u)
    {
        var value = curve.GetSampledValue(u);

        var newKey = curve.TryGetPreviousKey(u, out var previousKey)
                         ? previousKey.Clone()
                         : new VDefinition();

        newKey.Value = value;
        newKey.U = u;

        var command = new AddKeyframesCommand(curve, newKey);
        return command;
    }

    private const int KeyframeIconWidth = 16;

    private static bool DrawPinButton(bool isParamComponentPinned, string componentName)
    {
        var buttonColor = isParamComponentPinned ? UiColors.StatusAnimated : UiColors.Gray;
        ImGui.PushStyleColor(ImGuiCol.Text, buttonColor.Rgba);
        var result = ImGui.Button(componentName);
        ImGui.PopStyleColor();
        return result;
    }

    protected internal override void HandleCurvePointDragging(in Guid compositionSymbolId, VDefinition vDef, bool isSelected)
    {
        if (vDef.U < Playback.Current.TimeInBars)
        {
            FrameStats.Current.HasKeyframesBeforeCurrentTime = true;
        }

        if (vDef.U > Playback.Current.TimeInBars)
        {
            FrameStats.Current.HasKeyframesAfterCurrentTime = true;
        }

        var isItemActive = ImGui.IsItemActive();
        if (ImGui.IsItemHovered() || isItemActive)
        {
            var imGuiCursor = CurveInputEditing.MoveDirection switch
                                  {
                                      CurveInputEditing.MoveDirections.Horizontal => ImGuiMouseCursor.ResizeEW,
                                      CurveInputEditing.MoveDirections.Vertical   => ImGuiMouseCursor.ResizeNS,
                                      _                                            => ImGuiMouseCursor.ResizeAll
                                  };
            ImGui.SetMouseCursor(imGuiCursor);

            // During an active drag, also force the OS cursor directly. ImGui routes cursor
            // updates through WM_SETCURSOR which doesn't fire reliably while the mouse button
            // is held; without this the cursor reverts to default mid-drag. For pure hover we
            // skip this — calling Cursor.Current here causes a one-frame flicker on every
            // mouse move because Windows re-queries the form's cursor on WM_SETCURSOR.
            if (isItemActive)
            {
                var winCursor = CurveInputEditing.MoveDirection switch
                                    {
                                        CurveInputEditing.MoveDirections.Horizontal => System.Windows.Forms.Cursors.SizeWE,
                                        CurveInputEditing.MoveDirections.Vertical   => System.Windows.Forms.Cursors.SizeNS,
                                        _                                            => System.Windows.Forms.Cursors.SizeAll
                                    };
                System.Windows.Forms.Cursor.Current = winCursor;
            }
        }

        if (ImGui.IsItemDeactivated())
        {
            // MoveDirection is the authoritative "a drag was in progress" signal —
            // _changeKeyframesCommand is a static that only gets assigned when this editor
            // is the dispatcher's drag target (it isn't in inline-curve-edit mode, so it
            // stays null even while DSA's command is alive). If the latch fired this drag,
            // finalize the dispatched command and reset the axis latch for next time.
            if (CurveInputEditing.MoveDirection != CurveInputEditing.MoveDirections.Undecided)
            {
                TimeLineCanvas.Current.CompleteDragCommand();
                CurveInputEditing.MoveDirection = CurveInputEditing.MoveDirections.Undecided;
            }
        }

        if (!ImGui.IsItemActive() || !ImGui.IsMouseDragging(0, 0f))
            return;

        if (ImGui.GetIO().KeyCtrl && _changeKeyframesCommand == null)
        {
            if (isSelected)
                SelectedKeyframes.Remove(vDef);

            return;
        }

        if (!isSelected)
        {
            if (!ImGui.GetIO().KeyShift)
            {
                TimeLineCanvas.Current.ClearSelection();
            }

            SelectedKeyframes.Add(vDef);
        }

        // Per-drag axis latch. Mouse-down starts Undecided (4-way cursor via the branch
        // near the top). When total drag exceeds threshold, lock to whichever axis has the
        // larger delta and dispatch StartDragCommand once. Reset on release (see above).
        if (CurveInputEditing.MoveDirection == CurveInputEditing.MoveDirections.Undecided)
        {
            var mouseDragDelta = ImGui.GetMouseDragDelta();
            if (mouseDragDelta.Length() > CurveInputEditing.MoveDirectionThreshold)
            {
                CurveInputEditing.MoveDirection = Math.Abs(mouseDragDelta.X) >= Math.Abs(mouseDragDelta.Y)
                                                      ? CurveInputEditing.MoveDirections.Horizontal
                                                      : CurveInputEditing.MoveDirections.Vertical;
                TimeLineCanvas.Current.StartDragCommand(compositionSymbolId);
            }
        }

        var newDragPosition = TimeLineCanvas.Current.InverseTransformPositionFloat(ImGui.GetIO().MousePos);

        var allowHorizontal = CurveInputEditing.MoveDirection == CurveInputEditing.MoveDirections.Both
                              || CurveInputEditing.MoveDirection == CurveInputEditing.MoveDirections.Horizontal
                              || (ImGui.GetIO().KeyCtrl);

        var allowVertical = CurveInputEditing.MoveDirection == CurveInputEditing.MoveDirections.Both
                            || CurveInputEditing.MoveDirection == CurveInputEditing.MoveDirections.Vertical
                            || (ImGui.GetIO().KeyCtrl);

        var enableSnapping = ImGui.GetIO().KeyShift;
        double u = allowHorizontal ? newDragPosition.X : vDef.U;
        if (allowHorizontal)
        {
            if (enableSnapping && _snapHandlerU.TryCheckForSnapping(u, out var snappedValue, TimeLineCanvas.Scale.X,
                                                                    TimeLineCanvas.SelectionDragSnapExclusions))
            {
                u = snappedValue;
            }
        }

        double v = allowVertical ? newDragPosition.Y : vDef.Value;
        if (allowVertical)
        {
            if (enableSnapping && _snapHandlerV.TryCheckForSnapping(v, out var snappedValue, TimeLineCanvas.Scale.Y))
            {
                v = snappedValue;
            }
        }

        UpdateDragCommand(u - vDef.U, v - vDef.Value);
    }

    void ITimeObjectManipulation.DeleteSelectedElements(Instance compositionOp)
    {
        AnimationOperations.DeleteSelectedKeyframesFromAnimationParameters(SelectedKeyframes, AnimationParameters, compositionOp);
        RebuildCurveTables();
    }

    public void ClearSelection()
    {
        SelectedKeyframes.Clear();
    }

    public void UpdateSelectionForArea(ImRect screenArea, SelectionFence.SelectModes selectMode)
    {
        var canvasArea = TimeLineCanvas.Current!.InverseTransformRect(screenArea).MakePositive();
        UpdateSelectionForCanvasArea(canvasArea, selectMode);
    }

    /// <summary>
    /// Same as <see cref="UpdateSelectionForArea"/> but takes an already-inverted canvas-space rect.
    /// Use this from sub-canvases (e.g. <see cref="InlineCurveArea"/>) that have their own
    /// coordinate origin — letting them do the inverse with their own WindowPos rather than the
    /// outer timeline's.
    /// </summary>
    public void UpdateSelectionForCanvasArea(ImRect canvasArea, SelectionFence.SelectModes selectMode)
    {
        if (selectMode == SelectionFence.SelectModes.Replace)
            SelectedKeyframes.Clear();

        var matchingItems = new List<VDefinition>();
        foreach (var keyframe in _visibleKeyframes)
        {
            if (canvasArea.Contains(new Vector2((float)keyframe.U, (float)keyframe.Value)))
                matchingItems.Add(keyframe);
        }

        switch (selectMode)
        {
            case SelectionFence.SelectModes.Add:
            case SelectionFence.SelectModes.Replace:
                SelectedKeyframes.UnionWith(matchingItems);
                break;
            case SelectionFence.SelectModes.Remove:
                SelectedKeyframes.ExceptWith(matchingItems);
                break;
        }
    }

    public ICommand StartDragCommand(in Guid symbolId)
    {
        _changeKeyframesCommand = new ChangeKeyframesCommand(SelectedKeyframes, GetAllCurves());
        return _changeKeyframesCommand;
    }

    public void UpdateDragCommand(double dt, double dv)
    {
        foreach (var vDefinition in SelectedKeyframes)
        {
            vDefinition.U += dt;
            vDefinition.Value += dv;
        }

        RebuildCurveTables();
        foreach (var ap in AnimationParameters)
        {
            foreach (var c in ap.Curves)
            {
                c.UpdateTangents();
            }
        }
    }

    public void CompleteDragCommand()
    {
        if (_changeKeyframesCommand == null)
            return;

        CurveInputEditing.MoveDirection = CurveInputEditing.MoveDirections.Undecided;

        // Update reference in macro command
        _changeKeyframesCommand.StoreCurrentValues();
        _changeKeyframesCommand = null;
    }

    public void UpdateDragAtStartPointCommand(double dt, double dv)
    {
    }

    public void UpdateDragAtEndPointCommand(double dt, double dv)
    {
    }

    #region implement snapping -------------------------
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        _snapThresholdOnCanvas = SnapDistance / snapResult.CanvasScale;
        foreach (var vDefinition in GetAllKeyframes())
        {
            if (SelectedKeyframes.Contains(vDefinition))
                continue;

            snapResult.TryToImproveWithAnchorValue(vDefinition.U);
        }
    }

    private void CheckForSnapping(double targetTime, double anchorTime, ref double maxForce, ref double bestSnapTime)
    {
        var distance = Math.Abs(anchorTime - targetTime);
        if (distance < 0.001)
            return;

        var force = Math.Max(0, _snapThresholdOnCanvas - distance);
        if (force <= maxForce)
            return;

        bestSnapTime = anchorTime;
        maxForce = force;
    }

    private double _snapThresholdOnCanvas;
    private const float SnapDistance = 4;
    #endregion

    // Screen-space tolerance for curve-line hit-testing (≈4 px, squared for cheap compare).
    private static readonly float CurveHoverToleranceSquared = (4f * T3Ui.UiScaleFactor) * (4f * T3Ui.UiScaleFactor);

    /// <summary>
    /// True if the mouse is within <paramref name="toleranceSq"/> screen-space-squared of any
    /// segment of the curve's cached sample polyline. Used for cross-view hover: hovering a
    /// curve in the inline pane should fade other parameters' curves (in both panes).
    ///
    /// Two pruning passes keep this cheap even with many curves per frame:
    /// 1) Binary-search a narrow U window around the mouse in <c>SampleCache</c>. Curves whose
    ///    time range doesn't overlap the cursor return zero points here — constant-time skip.
    /// 2) Per-segment screen AABB pre-check; only segments whose rect contains the mouse (plus
    ///    tolerance padding) pay for the point-to-segment distance calc.
    /// </summary>
    private static bool IsMouseOverCurve(Curve curve, ScalableCanvas canvas, Vector2 mousePos, float toleranceSq)
    {
        var scaleXAbs = MathF.Abs(canvas.Scale.X);
        if (scaleXAbs < 1e-6f)
            return false;

        var tolerancePx = MathF.Sqrt(toleranceSq);
        var mouseU = canvas.InverseTransformX(mousePos.X);
        // Query slightly wider than the tolerance so we still catch the boundary segment that
        // straddles the tolerance edge (whose anchor point sits just outside).
        var uQueryRange = 2f * tolerancePx / scaleXAbs;

        var points = curve.SampleCache.GetPointsInRange(mouseU - uQueryRange, mouseU + uQueryRange);
        if (points.Length < 2)
            return false;

        var prev = canvas.TransformPositionFloat(points[0]);
        for (var i = 1; i < points.Length; i++)
        {
            var curr = canvas.TransformPositionFloat(points[i]);

            // Per-segment AABB pre-check (cheap). Skip the sqrt-free distance calc when the
            // mouse is clearly outside this segment's screen rect + tolerance padding.
            var minX = (prev.X < curr.X ? prev.X : curr.X) - tolerancePx;
            var maxX = (prev.X > curr.X ? prev.X : curr.X) + tolerancePx;
            var minY = (prev.Y < curr.Y ? prev.Y : curr.Y) - tolerancePx;
            var maxY = (prev.Y > curr.Y ? prev.Y : curr.Y) + tolerancePx;
            if (mousePos.X >= minX && mousePos.X <= maxX && mousePos.Y >= minY && mousePos.Y <= maxY
                && PointSegmentDistanceSquared(mousePos, prev, curr) <= toleranceSq)
            {
                return true;
            }
            prev = curr;
        }
        return false;
    }

    private static float PointSegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lenSq = Vector2.Dot(ab, ab);
        if (lenSq < 1e-6f)
        {
            var d0 = p - a;
            return Vector2.Dot(d0, d0);
        }
        var t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        var closest = a + t * ab;
        var diff = p - closest;
        return Vector2.Dot(diff, diff);
    }

    public static void DrawCurveLine(Curve curve, ScalableCanvas canvas, Color color, bool isParamHovered = false)
    {
        var visibleStartU = canvas.InverseTransformPositionFloat(canvas.WindowPos).X;
        var visibleEndU = canvas.InverseTransformPositionFloat(canvas.WindowPos + new Vector2(ImGui.GetWindowWidth(), 0)).X;
        var screenScaleX = (double)canvas.Scale.X;

        curve.SampleCache.Update(curve, visibleStartU, visibleEndU, screenScaleX);

        var cache = curve.SampleCache;
        var firstKeyU = cache.FirstKeyU;
        var lastKeyU = cache.LastKeyU;
        if (double.IsNaN(firstKeyU))
            return;
        
        var drawList = ImGui.GetWindowDrawList();
        var thickness = isParamHovered ? 3f : 1.4f;
        var outsideColor = color.Fade(0.3f);

        // Always draw 3 segments: dimmed pre, full body, dimmed post
        // Use -/+Infinity for outer bounds to include all cached pre/post points beyond visible edges
        DrawPolylineSegment(cache.GetPointsInRange(double.NegativeInfinity, firstKeyU), canvas, drawList, outsideColor, thickness);
        DrawPolylineSegment(cache.GetPointsInRange(firstKeyU, lastKeyU), canvas, drawList, color, thickness);
        DrawPolylineSegment(cache.GetPointsInRange(lastKeyU, double.PositiveInfinity), canvas, drawList, outsideColor, thickness);
    }

    private static void DrawPolylineSegment(ReadOnlySpan<Vector2> points, ScalableCanvas canvas,
                                            ImDrawListPtr drawList, Color color, float thickness)
    {
        var pointCount = Math.Min(points.Length, MaxPolylinePoints);
        if (pointCount < 2)
            return;

        for (var i = 0; i < pointCount; i++)
        {
            var screenPos = canvas.TransformPosition(points[i]);
            _polylineBuffer[i] = new Vector2(screenPos.X, (int)screenPos.Y + 0.5f);
        }

        drawList.AddPolyline(ref _polylineBuffer[0], pointCount, color, ImDrawFlags.None, thickness);
    }

    private static ChangeKeyframesCommand _changeKeyframesCommand;
    private readonly ValueSnapHandler _snapHandlerU;
    private readonly ValueSnapHandler _snapHandlerV;
    private readonly Dictionary<int, int> _pinnedParameterComponents = new();

    /// <summary>
    /// Shared pre-allocated buffer for building polyline screen points.
    /// Used by both curve editor and dope sheet to avoid per-frame allocations.
    /// Clamped to MaxPolylinePoints — dense clusters beyond this are visually indistinguishable.
    /// </summary>
    internal const int MaxPolylinePoints = 2000;
    internal static readonly Vector2[] _polylineBuffer = new Vector2[MaxPolylinePoints];
}