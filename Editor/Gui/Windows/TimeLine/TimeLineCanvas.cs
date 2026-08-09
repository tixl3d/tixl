#nullable enable

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Interaction.WithCurves;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.TimeLine.Raster;
using T3.Editor.Gui.Windows.TimeLine.TimeClips;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Combines multiple <see cref="ITimeObjectManipulation"/>s into a single consistent
/// timeline that allows dragging selected time elements of various types.
/// </summary>
[HelpUiID("Timeline")]
internal sealed class TimeLineCanvas : AnimationCanvas
{
    public TimeLineCanvas(NodeSelection nodeSelection, Func<Instance> getCompositionOp, Func<Guid, bool> requestChildCompositionFunc)
    {
        _nodeSelection = nodeSelection;

        DopeSheetArea = new DopeSheetArea(SnapHandlerForU, this);
        _timelineCurveEditArea = new TimelineCurveEditor(this, SnapHandlerForU, SnapHandlerForV);
        _timeSelectionRange = new TimeSelectionRange(this, SnapHandlerForU);
        _selectionRangeIndicator = new SelectionRangeIndicator(this, SnapHandlerForU);
        _sourceExtentEditor = new SourceExtentEditor(this);
        _keySetStrip = new KeySetStrip(this);
        ClipArea = new ClipArea(this, getCompositionOp, requestChildCompositionFunc, SnapHandlerForU);
        _curveEditCanvas = new InlineCurveArea(this, _timelineCurveEditArea, _horizontalRaster);
        _inlineDataClipArea = new InlineDataClipArea(this);
        _detailsArea = new TimelineDetailsArea(this, _curveEditCanvas, _inlineDataClipArea);

        SnapHandlerForV.AddSnapAttractor(_horizontalRaster);
        SnapHandlerForU.AddSnapAttractor(_clipRange);
        SnapHandlerForU.AddSnapAttractor(_loopRange);
        SnapHandlerForU.AddSnapAttractor(_timeRasterSwitcher);
        SnapHandlerForU.AddSnapAttractor(_currentTimeMarker);
        SnapHandlerForU.AddSnapAttractor(ClipArea);
        SnapHandlerForU.AddSnapAttractor(_selectionRangeIndicator);
        SnapHandlerForU.AddSnapAttractor(_sourceExtentEditor);
        _selectionDragSnapExclusions = [_selectionRangeIndicator];

        KeyframeEditors = new KeyframeEditorGroup(_activeKeyframeEditors);

        FoldingHeight = new TimelineHeight(this);
        Playback = null!;
    }

    /// <summary>
    /// Snap-attractors to skip while dragging selected keyframes or keyset-indicator clusters.
    /// The <see cref="SelectionRangeIndicator"/> anchors at the first/last selected-keyframe U,
    /// so snapping to it during such a drag forms a feedback loop that stutters the boundary keys.
    /// </summary>
    internal IValueSnapAttractor[] SelectionDragSnapExclusions => _selectionDragSnapExclusions;

    /// <summary>
    /// Every currently-active animation-parameter editor on the timeline. Today this is always
    /// a single editor (DopeSheetArea in DopeView mode, TimelineCurveEditor in CurveEditor mode);
    /// future split-view will expose both simultaneously. Cross-mode components (SRI, KeySetStrip,
    /// TimeWarpDrag) aggregate through <see cref="KeyframeEditors"/> so they don't need to pick one.
    /// </summary>
    public readonly KeyframeEditorGroup KeyframeEditors;

    /// <summary>
    /// Invoked when the keyframe selection is explicitly replaced (not merely added to / removed from).
    /// Lets the SRI drop its TimeWarp handles since they were configured for the previous selection.
    /// </summary>
    internal void OnKeyframeSelectionReplaced() => _selectionRangeIndicator.ClearTimeWarpHandles();

    public NodeSelection NodeSelection => _nodeSelection;

    /// <summary>
    /// Selection-range edge drag: keyframes stretch about the fixed end, while time clips are trimmed to
    /// the dragged boundary instead of stretched — their speed and source mapping stay intact.
    /// </summary>
    public void UpdateSelectionRangeEdgeDrag(double scaleU, double originU, double boundaryU, double origBoundaryU, bool isStart)
    {
        KeyframeEditors.UpdateDragStretchCommand(scaleU, 1, originU, 0);
        ClipArea.TrimSelectedClipsToBoundary(boundaryU, origBoundaryU, isStart);
    }

    private int RulerHeight => (int)(33 * T3Ui.UiScaleFactor);
    private int KeySetStripHeight => (int)(11 * T3Ui.UiScaleFactor);

    public void Draw(ProjectView projectView, Playback playback)
    {
        Debug.Assert(projectView.CompositionInstance != null);
        
        var compositionOp = projectView.CompositionInstance;
        Current = this;
        Playback = playback;
        SyncStateWithComposition(compositionOp);

        _selectedAnimationParameters = GetAnimationParametersForSelectedNodes(compositionOp);

        PruneExpandedForMissingParams();

        // Very ugly hack to prevent scaling the output above window size
        var keepScale = T3Ui.UiScaleFactor;

        ScrollToTimeAfterStopped();
        KeepPlayheadInView();

        var modeChanged = UpdateMode();
        SyncInlineCurveEditorRegistration();

        // Details pane (curve editor OR DataClip editor) takes over the bottom strip of
        // the timeline body when it's active. The outer canvas suppresses its fence in
        // that case so clicks inside the details pane don't bubble up and clear the
        // clip / keyframe selection that drove the pane open in the first place; a
        // dope-local fence inside the top pane covers the rubber-band-select use case.
        var detailsActive = _detailsArea.IsActive(compositionOp);
        var outerFlags = T3Ui.EditingFlags.AllowHoveredChildWindows;
        if (detailsActive && CurveEditAreaScreenRect.HasValue
            && CurveEditAreaScreenRect.Value.Contains(ImGui.GetMousePos()))
        {
            // Curve editor's sub-canvas owns wheel / RMB while the cursor is inside it;
            // ceding here keeps zoom / pan from double-processing on the outer canvas.
            outerFlags |= T3Ui.EditingFlags.PreventZoomWithMouseWheel
                          | T3Ui.EditingFlags.PreventPanningWithMouse;
        }

        DrawAnimationCanvas(drawAdditionalCanvasContent: DrawCanvasContent,
                            detailsActive ? null : _selectionFence,
                            0,
                            outerFlags,
                            drawVSnapIndicator: !detailsActive);
        Current = null;

        T3Ui.UiScaleFactor = keepScale;
        return;

        void DrawCanvasContent(InteractionState interactionState)
        {
            // Panning hands view control to the user: follow-playback scrolling stays suspended until
            // playback is (re)started, which re-arms it. Zooming deliberately does NOT suspend — changing
            // the zoom level while the view keeps tracking the playhead is a common way to adjust context.
            if (interactionState.UserPannedCanvas)
                _followPlaybackSuspended = true;

            // Cross-view hover state is rebuilt each frame by whichever renderer's hit-test
            // sets it. Stale values would leak hover fade to the next frame on mouse-exit.
            HoveredParameterHash = null;
            HoveredComponentBit = 0;
            HoveredKeyframeUniqueId = null;

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() );
            if (PlaybackUtils.TryFindingSoundtrack(out var soundtrack, out var composition))
            {
                TimeLineImage.Draw(Drawlist, soundtrack);
            }
            
            _timeRasterSwitcher.Draw(this);

            // Ruler
            {
                ImGui.BeginChild("##ruler", new Vector2(0,RulerHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
                DrawTimeRuler(interactionState.MouseState.Position.X);
                _sourceExtentEditor.Draw(compositionOp, ImGui.GetWindowDrawList());
                _selectionRangeIndicator.Draw(compositionOp, ImGui.GetWindowDrawList());
                ImGui.EndChild();
            }


            // KeySet strip (keyframe-cluster indicators below the ruler)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.GridLines.Fade(0.20f).Rgba);
                ImGui.BeginChild("##keySetStrip", new Vector2(0,KeySetStripHeight));
                _keySetStrip.Draw(compositionOp, _selectedAnimationParameters, KeyframeEditors, ImGui.GetWindowDrawList());
                ImGui.EndChild();
                ImGui.PopStyleColor();
            }

            // SRI and SA may have mutated keyframe U values via TimeWarpDrag / the SA's ApplyKeyframeTimeOffset.
            // Rebuild each active editor's curve tables once, centrally, so downstream editor draws see sorted curves.
            KeyframeEditors.ProcessPendingCurveTableRebuild();

            HandleDeferredActions();

            ImGui.BeginChild(ImGuiTitle, new Vector2(0, 0), ImGuiChildFlags.Borders,
                             ImGuiWindowFlags.NoMove
                             | ImGuiWindowFlags.NoBackground
                             | ImGuiWindowFlags.NoScrollWithMouse);

            {
                if (KeyActionHandling.Triggered(UserActions.DeleteSelection))
                    DeleteSelectedElements(compositionOp);

                // Layout: top pane (dope + clips) | resize handle | details pane.
                // The 8 px handle gap sits outside any child's hit rect so ImGui routes
                // clicks to the handle; the two ItemSpacing.Y values close out the math
                // so the three stacked items consume exactly rawAvailHeight.
                var detailsActive = _detailsArea.IsActive(compositionOp);
                var rawAvailHeight = ImGui.GetContentRegionAvail().Y;
                if (detailsActive)
                    DetailsAreaHeight = MathF.Min(MathF.Max(DetailsAreaHeight, 80f), MathF.Max(80f, rawAvailHeight - 80f));

                const float splitterReserve = 8f;
                var splitterGap = detailsActive ? splitterReserve : 0f;
                var spacingReserve = detailsActive ? 2f * ImGui.GetStyle().ItemSpacing.Y : 0f;
                var detailsHeight = detailsActive ? DetailsAreaHeight : 0f;
                var topHeight = MathF.Max(60f, rawAvailHeight - detailsHeight - splitterGap - spacingReserve);

                switch (Mode)
                {
                    case Modes.DopeView:
                        if (detailsActive)
                        {
                            ImGui.BeginChild("##timelineTopPane", new Vector2(0, topHeight),
                                             ImGuiChildFlags.None,
                                             ImGuiWindowFlags.NoBackground
                                             | ImGuiWindowFlags.NoScrollWithMouse);
                            var dopeContentStartY = ImGui.GetCursorScreenPos().Y;
                            ClipArea.Draw(compositionOp, Playback, SnapHandlerForU);
                            _currentTimeMarker.DrawLineInCurrentWindow(Playback.TimeInBars, this);
                            DopeSheetArea.Draw(compositionOp, _selectedAnimationParameters);

                            // Dope-local fence — outer fence is suppressed while the
                            // details pane is up so its clicks don't bubble out and
                            // wipe the selection that opened the pane. Delegate to the
                            // shared UpdateSelectionForArea so background clicks clear
                            // every manipulator (keyframes AND clips), matching the
                            // outer-fence behaviour the user expects.
                            if (!T3Ui.IsAnyPopupOpen)
                            {
                                switch (_dopeFence.UpdateAndDraw(out var selectMode))
                                {
                                    case SelectionFence.States.Updated:
                                    case SelectionFence.States.CompletedAsClick:
                                        UpdateSelectionForArea(_dopeFence.BoundsInScreen, selectMode);
                                        break;
                                }
                            }

                            CustomComponents.HandleDragScrolling(this);
                            _lastDopeContentHeight = MathF.Max(0f, ImGui.GetCursorScreenPos().Y - dopeContentStartY);
                            ImGui.EndChild();

                            _detailsArea.DrawResizeHandle(splitterReserve);

                            ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundFull.Fade(0.2f).Rgba);
                            _detailsArea.Draw(compositionOp, _selectedAnimationParameters,
                                              detailsHeight, detailsHeight, modeChanged);
                            ImGui.PopStyleColor();
                        }
                        else
                        {
                            ClipArea.Draw(compositionOp, Playback, SnapHandlerForU);
                            _currentTimeMarker.DrawLineInCurrentWindow(Playback.TimeInBars, this);
                            DopeSheetArea.Draw(compositionOp, _selectedAnimationParameters);
                        }
                        break;
                    case Modes.CurveEditor:
                    {
                        _horizontalRaster.Draw(this);
                        var heightChanged = Math.Abs(ImGui.GetWindowHeight() - _lastCurveEditorHeight) > 1f;
                        _lastCurveEditorHeight = ImGui.GetWindowHeight();

                        var selectionHash = ComputeSelectionHash();
                        var selectionChanged = selectionHash != _lastSelectionHash;
                        _lastSelectionHash = selectionHash;

                        _timelineCurveEditArea.Draw(compositionOp, _selectedAnimationParameters,
                                                    fitVerticalOnly: modeChanged || selectionChanged || heightChanged);
                        break;
                    }
                }

                var compositionTimeClip = Structure.GetCompositionTimeClip(compositionOp);

                if (Playback.IsLooping)
                {
                    _loopRange.Draw(this, Playback, Drawlist, SnapHandlerForU);
                }
                else if (compositionTimeClip != null)
                {
                    _clipRange.Draw(this, compositionTimeClip, Drawlist);
                }

                // When the details pane is up, the top-pane child already handles its
                // own drag-scroll; outer call would hit ImGuiTitle (no overflow).
                if (!detailsActive)
                    CustomComponents.HandleDragScrolling(this);
            }

            ImGui.EndChild();

            _currentTimeMarker.Draw(Playback.TimeInBars, this);
            _timeSelectionRange.Draw(compositionOp, Drawlist);

            if (_selectionFence.State == SelectionFence.States.CompletedAsClick)
            {
                var newTime = InverseTransformPositionFloat(ImGui.GetMousePos()).X;
                Playback.TimeInBars = newTime;

                // Background click clears keyframe selection (both panes).
                if (SharedSelectedKeyframes.Count > 0)
                {
                    SharedSelectedKeyframes.Clear();
                    OnKeyframeSelectionReplaced();
                }
            }
        }
    }

    /// <summary>
    /// Keeps TimelineCurveEditor registered with the snap handler and keyframe-editor group
    /// while the inline curve pane is visible (DopeView + any expanded parameter). We do NOT
    /// add it to <see cref="AnimationCanvas.TimeObjectManipulators"/>: that list dispatches
    /// drag / selection mutations to every entry, and both editors share the same
    /// <c>SelectedKeyframes</c> set — so double-registering makes every <c>UpdateDragCommand</c>
    /// apply U += dt twice per frame (oscillating keyframe jitter). DopeSheetArea is the single
    /// drag dispatcher; curve-pane visuals pick up changes via the shared selection set.
    /// </summary>
    private void SyncInlineCurveEditorRegistration()
    {
        var shouldBeActive = Mode == Modes.DopeView && CurveEditingParamHashes.Count > 0;
        if (shouldBeActive && !_curveEditorRegisteredInline)
        {
            SnapHandlerForU.AddSnapAttractor(_timelineCurveEditArea);
            _activeKeyframeEditors.Add(_timelineCurveEditArea);
            _curveEditorRegisteredInline = true;
        }
        else if (!shouldBeActive && _curveEditorRegisteredInline)
        {
            SnapHandlerForU.RemoveSnapAttractor(_timelineCurveEditArea);
            _activeKeyframeEditors.Remove(_timelineCurveEditArea);
            _curveEditorRegisteredInline = false;
        }
    }

    private bool _curveEditorRegisteredInline;

    /// <summary>
    /// In the inline curve-edit layout the timeline canvas becomes the U-axis authority and
    /// the curve-edit sub-canvas owns V. Constrain wheel zoom to U here so DSA wheel-zoom
    /// doesn't incidentally scale V (which outer canvas doesn't visibly render anyway, but we
    /// don't want stray ScrollTarget.Y updates either). Outside the inline layout, fall back
    /// to the base behavior.
    /// </summary>
    protected override void ApplyZoomDelta(Vector2 position, float zoomDelta, out bool zoomed)
    {
        if (Mode != Modes.DopeView || CurveEditingParamHashes.Count == 0)
        {
            base.ApplyZoomDelta(position, zoomDelta, out zoomed);
            return;
        }

        zoomed = false;
        if (Math.Abs(zoomDelta - 1) < 0.001f)
            return;
        var clamped = ClampScaleToValidRange(ScaleTarget * zoomDelta);
        if (clamped == ScaleTarget)
            return;

        var zoom = new Vector2(zoomDelta, 1f);
        ScaleTarget *= zoom;
        if (Math.Abs(zoomDelta) > 0.1f)
            zoomed = true;

        var focus = InverseTransformPositionFloat(position);
        ScrollTarget += (focus - ScrollTarget) * (zoom - Vector2.One) / zoom;
    }

    /// <summary>
    /// Drops any curve-editing hash whose parameter is no longer among the current
    /// selection's animated parameters. If the selection no longer includes an expanded
    /// param (e.g. the user selected a different node in the graph), the inline curve
    /// area would otherwise linger empty; pruning closes it automatically when the last
    /// expanded param disappears.
    /// </summary>
    private void PruneExpandedForMissingParams()
    {
        if (CurveEditingParamHashes.Count == 0)
            return;

        CurveEditingParamHashes.RemoveWhere(hash => !ContainsHash(_selectedAnimationParameters, hash));

        // Drop orphaned component-mask entries (small alloc only when a hash was actually removed).
        if (VisibleComponentMask.Count > 0)
        {
            foreach (var h in VisibleComponentMask.Keys.ToList())
            {
                if (!CurveEditingParamHashes.Contains(h))
                    VisibleComponentMask.Remove(h);
            }
        }
    }

    private static bool ContainsHash(List<AnimationParameter> list, int hash)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Hash == hash)
                return true;
        }
        return false;
    }
    //
    // #region handle nested timelines ----------------------------------
    // public override void UpdateScaleAndTranslation(Instance compositionOp, ScalableCanvas.Transition transition)
    // {
    //     if (transition == ScalableCanvas.Transition.Instant)
    //         return;
    //
    //     // remember the old scroll state
    //     var oldScale = Scale;
    //     var oldScroll = Scroll;
    //
    //     var clip = Structure.GetCompositionTimeClip(compositionOp);
    //     if (clip == null) return;
    //
    //     // determine scaling factor
    //     TimeRange sourceRange, targetRange;
    //     if (transition == ScalableCanvas.Transition.JumpIn)
    //     {
    //         sourceRange = clip.TimeRange;
    //         targetRange = clip.SourceRange;
    //     }
    //     else
    //     {
    //         sourceRange = clip.SourceRange;
    //         targetRange = clip.TimeRange;
    //     }
    //
    //     float scale = targetRange.Duration / sourceRange.Duration;
    //
    //     // remove scrolling, then determine where the time clip is centered
    //     Scroll = new Vector2(0, Scroll.Y);
    //     var centerOfSourceRange = (sourceRange.Start + sourceRange.End) * 0.5f;
    //     var originalScreenPos = TransformX(centerOfSourceRange);
    //
    //     // now apply scaling and determine where the source clip is centered
    //     Scale /= scale;
    //     var centerOfTargetRange = (targetRange.Start + targetRange.End) * 0.5f;
    //     var newScreenPos = TransformX(centerOfTargetRange);
    //
    //     // set final scale and "undo" the movement of the position
    //     ScaleTarget.X = Scale.X;
    //     var positionDelta = new Vector2(newScreenPos - originalScreenPos, 0f);
    //     ScrollTarget.X = oldScroll.X * scale + InverseTransformDirection(positionDelta).X;
    //
    //     // restore the old scale and scroll state
    //     Scale = oldScale;
    //     Scroll = oldScroll;
    // }
    // #endregion

    private void HandleDeferredActions()
    {
        if (UserActionRegistry.WasActionQueued(UserActions.PlaybackJumpToNextKeyframe))
        {
            var bestNextTime = double.PositiveInfinity;
            var foundNext = false;
            var time = Playback.TimeInBars + 0.001f;
            foreach (var next in _selectedAnimationParameters)
            {
                // Curves live in the op's local time — search there, then compare candidates at the
                // playback time the key takes effect, so jumps land where playback reads the key.
                var localTime = Animator.GetLocalAnimationTime(next.Instance, time);
                foreach (var curve in next.Curves)
                {
                    if (!curve.TryGetNextKey(localTime, out var key))
                        continue;

                    var keyPlaybackTime = Animator.GetGlobalAnimationTime(next.Instance, key.U);
                    if (keyPlaybackTime <= time || keyPlaybackTime > bestNextTime)
                        continue;

                    foundNext = true;
                    bestNextTime = keyPlaybackTime;
                }
            }

            // Clip starts and ends are jump targets too — stepping through cuts with . and ,
            foreach (var clip in ClipArea.AllTimeClips)
            {
                if (clip.TimeRange.Start > time && clip.TimeRange.Start < bestNextTime)
                {
                    foundNext = true;
                    bestNextTime = clip.TimeRange.Start;
                }

                if (clip.TimeRange.End > time && clip.TimeRange.End < bestNextTime)
                {
                    foundNext = true;
                    bestNextTime = clip.TimeRange.End;
                }
            }

            if (foundNext)
            {
                Playback.TimeInBars = bestNextTime;
                CenterCurrentTimeIfNotVisible();
            }
        }

        if (UserActionRegistry.WasActionQueued(UserActions.PlaybackJumpToPreviousKeyframe))
        {
            var bestPreviousTime = double.NegativeInfinity;
            var foundNext = false;

            var time = Playback.TimeInBars - 0.001f;
            foreach (var next in _selectedAnimationParameters)
            {
                var localTime = Animator.GetLocalAnimationTime(next.Instance, time);
                foreach (var curve in next.Curves)
                {
                    if (!curve.TryGetPreviousKey(localTime, out var key))
                        continue;

                    var keyPlaybackTime = Animator.GetGlobalAnimationTime(next.Instance, key.U);
                    if (keyPlaybackTime >= time || keyPlaybackTime < bestPreviousTime)
                        continue;

                    foundNext = true;
                    bestPreviousTime = keyPlaybackTime;
                }
            }

            // Clip starts and ends are jump targets too — stepping through cuts with . and ,
            foreach (var clip in ClipArea.AllTimeClips)
            {
                if (clip.TimeRange.Start < time && clip.TimeRange.Start > bestPreviousTime)
                {
                    foundNext = true;
                    bestPreviousTime = clip.TimeRange.Start;
                }

                if (clip.TimeRange.End < time && clip.TimeRange.End > bestPreviousTime)
                {
                    foundNext = true;
                    bestPreviousTime = clip.TimeRange.End;
                }
            }

            if (foundNext)
            {
                Playback.TimeInBars = bestPreviousTime;
                CenterCurrentTimeIfNotVisible();
            }
        }
    }

    private void DrawTimeRuler(float mouseX)
    {
        var max = ImGui.GetWindowSize();
        var clampedSize = max;
        clampedSize.Y = Math.Min(TimeLineDragHeight, max.Y - 1);

        ImGui.SetCursorPos(new Vector2(0, max.Y - clampedSize.Y));

        // Allow the SelectionRangeIndicator (emitted later in the same ruler child) to steal hover/press in its overlapping area.
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton("##TimeDrag", clampedSize);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        }

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left) || ImGui.IsItemClicked())
        {
            var draggedTime = InverseTransformX(mouseX);
            if (ImGui.GetIO().KeyShift)
            {
                if (SnapHandlerForU.TryCheckForSnapping(draggedTime, out var snappedValue, Scale.X, [_currentTimeMarker]))
                {
                    draggedTime = (float)snappedValue;
                }
            }

            Playback.TimeInBars = draggedTime;
        }

        ImGui.SetCursorPos(Vector2.Zero);
    }

    /// <summary>
    /// While playing, keeps the time marker inside the middle 60% of the view: once it crosses the
    /// 20%-padding edge in the playback direction, the canvas scrolls along (ScrollTarget smoothing
    /// makes it glide). Panning or zooming suspends the follow (user-controlled view) until playback
    /// is started again.
    /// </summary>
    private void KeepPlayheadInView()
    {
        if (!UserSettings.Config.TimelineFollowsPlayback || _followPlaybackSuspended)
            return;

        if (Math.Abs(Playback.PlaybackSpeed) < 0.01f)
            return;

        var markerX = TransformX((float)Playback.TimeInBars);
        var leftEdge = WindowPos.X + WindowSize.X * 0.2f;
        var rightEdge = WindowPos.X + WindowSize.X * 0.8f;

        if (markerX > rightEdge)
        {
            ScrollTarget.X += InverseTransformDirection(new Vector2(markerX - rightEdge, 0)).X;
        }
        else if (markerX < leftEdge && Playback.PlaybackSpeed < 0)
        {
            ScrollTarget.X -= InverseTransformDirection(new Vector2(leftEdge - markerX, 0)).X;
        }
    }

    private void ScrollToTimeAfterStopped()
    {
        var isPlaying = Math.Abs(Playback.PlaybackSpeed) > 0.01f;
        var wasPlaying = Math.Abs(_lastPlaybackSpeed) > 0.01f;

        // Starting playback (space, transport icon, hotkeys — any stop→play transition) re-arms the
        // follow-playback scrolling that a manual pan/zoom suspended.
        if (isPlaying && !wasPlaying)
        {
            _followPlaybackSuspended = false;
        }

        if (!isPlaying && wasPlaying)
        {
            CenterCurrentTimeIfNotVisible();
        }

        _lastPlaybackSpeed = Playback.PlaybackSpeed;
    }

    /// <summary>Scrolls the view so the current playback time sits centered — only when it isn't visible,
    /// so it never disturbs a view the user framed deliberately.</summary>
    private void CenterCurrentTimeIfNotVisible()
    {
        if (IsCurrentTimeVisible())
            return;

        // assume we are not scrolling, what screen position would the playhead be at?
        var oldScroll = Scroll;
        Scroll = new Vector2(0, Scroll.Y);
        var posScreen = TransformX((float)Playback.TimeInBars);
        // position that playhead in the center of the window
        ScrollTarget.X = InverseTransformX(posScreen - WindowSize.X * 0.5f);
        // restore old state of scrolling
        Scroll = oldScroll;
    }

    private bool IsCurrentTimeVisible()
    {
        var timePosInScreen = TransformPosition(new Vector2((float)this.Playback.TimeInBars, 0));
        var timelineArea = ImRect.RectWithSize(WindowPos, WindowSize);
        timePosInScreen.Y = timelineArea.GetCenter().Y; // Adjust potential vertical scrolling of timeline area
        return timelineArea.Contains(timePosInScreen);
    }

    #region view modes
    private bool UpdateMode()
    {
        if (Mode == _lastMode)
            return false;

        // Tear down previous mode (skip on first call when _lastMode is null)
        switch (_lastMode)
        {
            case Modes.DopeView:
                TimeObjectManipulators.Remove(DopeSheetArea);
                TimeObjectManipulators.Remove(ClipArea);
                SnapHandlerForU.RemoveSnapAttractor(DopeSheetArea);
                _activeKeyframeEditors.Remove(DopeSheetArea);
                break;

            case Modes.CurveEditor:
                TimeObjectManipulators.Remove(_timelineCurveEditArea);
                SnapHandlerForU.RemoveSnapAttractor(_timelineCurveEditArea);
                _activeKeyframeEditors.Remove(_timelineCurveEditArea);
                break;
        }

        switch (Mode)
        {
            case Modes.DopeView:
                TimeObjectManipulators.Add(DopeSheetArea);
                TimeObjectManipulators.Add(ClipArea);
                SnapHandlerForU.AddSnapAttractor(DopeSheetArea);
                _activeKeyframeEditors.Add(DopeSheetArea);
                break;

            case Modes.CurveEditor:
                TimeObjectManipulators.Add(_timelineCurveEditArea);
                SnapHandlerForU.AddSnapAttractor(_timelineCurveEditArea);
                _activeKeyframeEditors.Add(_timelineCurveEditArea);
                break;

            case Modes.Undefined:
                Mode = Modes.DopeView;
                goto case Modes.DopeView;
        }

        _lastMode = Mode;
        return true;
    }

    public enum Modes
    {
        Undefined,
        DopeView,
        CurveEditor,
    }

    public Modes Mode = Modes.DopeView;
    private Modes _lastMode = Modes.Undefined;
    private float _lastCurveEditorHeight;
    private int _lastSelectionHash;

    // Per-parameter curve-expand state, populated by the curve-toggle icon on each dope-sheet row.
    // When non-empty in DopeView mode, the timeline body splits into dope-sheet (top) + curve-editor (below).
    internal readonly HashSet<int> CurveEditingParamHashes = new();

    // paramHash -> visible-component bitmask; missing entry = "all components visible".
    // Cleared when a parameter is un-expanded so re-expanding resets to all-on.
    internal readonly Dictionary<int, int> VisibleComponentMask = new();

    // Cross-view hover link (Phase 3). Written by whichever view the mouse is over;
    // both views read these to render matching emphasis.
    internal int? HoveredParameterHash;
    internal int HoveredComponentBit;
    internal int? HoveredKeyframeUniqueId;

    internal bool NormalizeCurveView;

    // Published by InlineCurveArea each frame it draws; null when the pane isn't visible.
    // TimeLineCanvas.Draw reads this at the top of the next frame to cede wheel/pan to the
    // curve-area sub-canvas when the mouse is inside it.
    internal ImRect? CurveEditAreaScreenRect;

    // Shared keyframe selection — DopeSheetArea and TimelineCurveEditor both receive this
    // via CurveEditing's shared-selection ctor, so edits in one view reflect in the other
    // (and in the SRI / SelectionArea aggregators).
    internal readonly VersionedKeyframeSet SharedSelectedKeyframes = new();

    private int ComputeSelectionHash()
    {
        var hash = _selectedAnimationParameters.Count;
        for (var i = 0; i < _selectedAnimationParameters.Count; i++)
        {
            hash = hash * 397 ^ _selectedAnimationParameters[i].Hash;
        }
        return hash;
    }

    private void SyncStateWithComposition(Instance compositionOp)
    {
        var symbolId = compositionOp.Symbol.Id;
        if (symbolId == _lastSyncedSymbolId)
        {
            // Sync copy fields every frame so saves capture current values
            var currentSymbolUi = compositionOp.Symbol.GetSymbolUi();
            if (currentSymbolUi != null)
            {
                currentSymbolUi.TimelineState ??= new TimelineState();
                SaveStateTo(currentSymbolUi.TimelineState);
            }
            return;
        }

        // Save to previous composition. Read-only symbols keep the state in memory but must not be
        // flagged as modified — merely viewing them would prompt to save them as a copy.
        if (_lastSyncedSymbolUi != null)
        {
            _lastSyncedSymbolUi.TimelineState ??= new TimelineState();
            SaveStateTo(_lastSyncedSymbolUi.TimelineState);
            if (!_lastSyncedSymbolUi.ReadOnly)
            {
                _lastSyncedSymbolUi.FlagAsModified();
            }
        }

        _lastSyncedSymbolId = symbolId;
        _lastSyncedSymbolUi = compositionOp.Symbol.GetSymbolUi();

        // Load from new composition
        if (_lastSyncedSymbolUi?.TimelineState is { } state)
        {
            LoadStateFrom(state);
        }
    }

    private Guid _lastSyncedSymbolId;
    private SymbolUi? _lastSyncedSymbolUi;

    internal void SaveStateTo(TimelineState state)
    {
        state.ScaleX = Scale.X;
        state.ScrollX = Scroll.X;
        state.Mode = Mode;
        state.InlineDataClipEditEnabled = InlineDataClipEditEnabled;
        state.DetailsAreaHeight = DetailsAreaHeight;
    }

    internal void LoadStateFrom(TimelineState state)
    {
        ScaleTarget = new Vector2(state.ScaleX, ScaleTarget.Y);
        Scale = new Vector2(state.ScaleX, Scale.Y);
        ScrollTarget = new Vector2(state.ScrollX, ScrollTarget.Y);
        Scroll = new Vector2(state.ScrollX, Scroll.Y);
        Mode = state.Mode;
        InlineDataClipEditEnabled = state.InlineDataClipEditEnabled;
        DetailsAreaHeight = state.DetailsAreaHeight;
    }

    /// <summary>
    /// Toggle for the inline DataClip edit area. Driven by the AudioFile icon button next
    /// to the Record toggle on the timeline toolbar. When on AND a TimeClip with a
    /// <c>DataClip</c> output is selected, the inline area renders below the dope sheet.
    /// </summary>
    internal bool InlineDataClipEditEnabled;

    /// <summary>
    /// User-controlled height of the inline details pane (curve editor OR DataClip
    /// editor — both share one pane). Persisted with <see cref="TimelineState"/> and
    /// resized via the splitter shown above the pane. Clamped at layout time so a value
    /// saved from a taller window can't starve the dope sheet on a smaller monitor.
    /// </summary>
    internal float DetailsAreaHeight = 200f;

    /// <summary>Forwards a mouse-wheel zoom from an embedded pane to this canvas's X axis.</summary>
    internal void ZoomXFromEmbedded(float wheelDelta, Vector2 mouseScreenPos)
    {
        if (Math.Abs(wheelDelta) < 0.0001f)
            return;
        var mouseState = new MouseState(mouseScreenPos, ImGui.GetIO().MouseDelta, wheelDelta);
        ZoomWithMouseWheel(mouseState, out _);
    }

    /// <summary>
    /// Pans this canvas's X scroll by a screen-pixel delta measured from a drag-start
    /// snapshot. Embedded panes capture <see cref="Scroll"/>.X on RMB-down then call
    /// this each frame so cumulative drag deltas don't over-accumulate.
    /// </summary>
    internal void PanXFromEmbedded(float startScrollX, float screenDeltaX)
    {
        if (Scale.X <= 0)
            return;
        var canvasDelta = screenDeltaX / Scale.X;
        Scroll = new Vector2(startScrollX - canvasDelta, Scroll.Y);
        ScrollTarget = new Vector2(startScrollX - canvasDelta, ScrollTarget.Y);
    }

    #endregion

    // TODO: this is horrible and should be refactored
    private List<AnimationParameter> GetAnimationParametersForSelectedNodes(Instance compositionOp)
    {
        var symbolUi = compositionOp.GetSymbolUi();
        var animator = symbolUi.Symbol.Animator;

        // Copy objects to reuse if possible
        _tmpAnimationParameters.Clear();
        _tmpAnimationParameters.AddRange(_pinnedParams);

        _pinnedParams.Clear();
        _curvesForSelection.Clear();

        var children = compositionOp.Children.Values;
        foreach (var child in children)
        {
            var isChildSelected = Selected.Unknown;

            for (var inputIndex = 0; inputIndex < child.Inputs.Count; inputIndex++)
            {
                var input = child.Inputs[inputIndex];
                if (!animator.IsInputSlotAnimated(input))
                    continue;

                // Test if child is selected only once and only if required
                if (isChildSelected == Selected.Unknown)
                {
                    isChildSelected = Selected.No;
                    foreach (var s in _nodeSelection.Selection)
                    {
                        if (s.Id != child.SymbolChildId)
                            continue;

                        isChildSelected = Selected.Yes;
                        break;
                    }
                }

                var paramHash = input.GetChildSlotHash();
                var isPinned = false;

                // Collect pinned
                foreach (var pinnedInputSlot in DopeSheetArea.PinnedParametersHashes)
                {
                    if (pinnedInputSlot != input.GetChildSlotHash())
                        continue;

                    if (!TryGetParamFromSpan(paramHash, out var param))
                    {
                        if (!animator.TryGetCurvesForInputSlot(input, out var curves))
                        {
                            Log.Warning("Can't find curves for animated parameter?");
                            continue;
                        }

                        param = new AnimationParameter
                                    {
                                        Instance = child,
                                        Input = input,
                                        Curves = curves,
                                        ChildUi = symbolUi.ChildUis[child.SymbolChildId],
                                        Hash = paramHash,
                                    };
                    }

                    _pinnedParams.Add(param);
                    isPinned = true;
                }

                if (isPinned || isChildSelected != Selected.Yes) 
                    continue;
                
                {
                    if (!TryGetParamFromSpan(paramHash, out var param))
                    {
                        if (!animator.TryGetCurvesForInputSlot(input, out var curves))
                        {
                            Log.Warning("Can't find curves for animated parameter?");
                            continue;
                        }
                           
                        param = new AnimationParameter
                                    {
                                        Instance = child,
                                        Input = input,
                                        Curves = curves,
                                        ChildUi = symbolUi.ChildUis[child.SymbolChildId],
                                        Hash = paramHash,
                                    };
                    }

                    _curvesForSelection.Add(param);
                }
            }
        }

        // Merge collections
        _pinnedParams.AddRange(_curvesForSelection.FindAll(sp => _pinnedParams.All(pp => pp.Input != sp.Input)));
        return _pinnedParams;
    }

    private static bool TryGetParamFromSpan(int paramHash, [NotNullWhen(true)] out AnimationParameter? parameter)
    {
        parameter = null;
        foreach (var p in _tmpAnimationParameters)
        {
            if (p.Hash != paramHash)
                continue;

            parameter = p;
            return true;
        }

        return false;
    }

    internal Playback Playback;
    public readonly DopeSheetArea DopeSheetArea;
    public readonly ClipArea ClipArea;
    public static TimeLineCanvas? Current;

    private enum Selected
    {
        Yes,
        No,
        Unknown,
    }



    /** Only used to avoid allocations */
    private static readonly List<AnimationParameter> _tmpAnimationParameters = [];

    private List<AnimationParameter> _selectedAnimationParameters = [];

    private readonly TimeRasterSwitcher _timeRasterSwitcher = new();
    private readonly HorizontalRaster _horizontalRaster = new();
    private readonly ClipRange _clipRange = new();
    private readonly LoopRange _loopRange = new();

    private readonly TimelineCurveEditor _timelineCurveEditArea;
    private readonly InlineCurveArea _curveEditCanvas;
    private readonly InlineDataClipArea _inlineDataClipArea;
    private readonly TimelineDetailsArea _detailsArea;

    private readonly CurrentTimeMarker _currentTimeMarker = new();
    private readonly TimeSelectionRange _timeSelectionRange;
    private readonly SelectionRangeIndicator _selectionRangeIndicator;
    private readonly SourceExtentEditor _sourceExtentEditor;
    private readonly KeySetStrip _keySetStrip;
    private readonly IValueSnapAttractor[] _selectionDragSnapExclusions;
    private readonly List<AnimationParameterEditing> _activeKeyframeEditors = new(2);
    private readonly NodeSelection _nodeSelection;

    private double _lastPlaybackSpeed;
    private bool _followPlaybackSuspended;
    private readonly List<AnimationParameter> _pinnedParams = new(20);
    private readonly List<AnimationParameter> _curvesForSelection = new(64);
    private readonly SelectionFence _selectionFence = new();
    private readonly SelectionFence _dopeFence = new();

    // Measured dope-pane content height from the previous frame (ClipArea + DSA rows + any
    // other interactive content drawn in that child). Drives the inline-layout split so the
    // dope pane takes only what it needs, capped at 50% of available height.
    private float _lastDopeContentHeight = 120f;

    // Styling
    private const float TimeLineDragHeight = 30;

    internal readonly TimelineHeight FoldingHeight;

    
    public sealed class AnimationParameter
    {
        public required Curve[] Curves;
        public required IInputSlot Input;
        public required Instance Instance;
        public required SymbolUi.Child ChildUi;

        public required int Hash;
        public float DampedMinValue;
        public float DampedMaxValue;

        /// <summary>
        /// Snapshot of the affine curve-time ↔ playback-time relation for this parameter (composed from the
        /// enclosing time clips). Rebuild per frame/use — clip drags change it continuously.
        /// </summary>
        public ParamTimeMapping BuildTimeMapping() => ParamTimeMapping.For(Instance);
    }

    /// <summary>
    /// An animated parameter's curves live in the op's local time (see <see cref="Animator.GetLocalAnimationTime"/>);
    /// the timeline canvas is in playback time. This caches the composed affine relation
    /// (<c>global = GlobalAtLocalZero + Rate · localU</c>) so per-key conversions don't re-walk the instance chain.
    /// </summary>
    public readonly struct ParamTimeMapping
    {
        public static ParamTimeMapping For(Instance instance)
        {
            var globalAtZero = Animator.GetGlobalAnimationTime(instance, 0);
            var rate = Animator.GetGlobalAnimationTime(instance, 1) - globalAtZero;
            return new ParamTimeMapping(globalAtZero, rate);
        }

        /// <summary>True when no enclosing clip remaps — the common case; lets callers skip conversions.</summary>
        public bool IsIdentity => GlobalAtLocalZero == 0 && Rate == 1;

        public double ToGlobal(double localU) => GlobalAtLocalZero + Rate * localU;

        public double ToLocal(double globalTime) => Math.Abs(Rate) < MinRate
                                                        ? GlobalAtLocalZero
                                                        : (globalTime - GlobalAtLocalZero) / Rate;

        public readonly double GlobalAtLocalZero;
        public readonly double Rate;

        private ParamTimeMapping(double globalAtLocalZero, double rate)
        {
            GlobalAtLocalZero = globalAtLocalZero;
            Rate = rate;
        }

        // A clip with ~zero source duration produces a degenerate rate; treat as unmappable rather than divide.
        private const double MinRate = 1e-9;
    }
    
    /// <summary>
    /// Height of the timeline below the graph. Unlike the rest of <see cref="TimelineState"/> this is
    /// project-global window layout, so it is persisted on the project's root symbol rather than on the
    /// composition the user happened to be inside while dragging the splitter.
    /// </summary>
    internal sealed class TimelineHeight
    {
        public TimelineHeight(TimeLineCanvas timeline)
        {
            _timeline = timeline;
        }

        public void DrawSplit(ProjectView projectView, out int newContentHeight)
        {
            SyncWithProject(projectView);

            var currentTimelineHeight = CurrentHeight;
            if (CustomComponents.SplitFromBottom(ref currentTimelineHeight))
            {
                _customTimeLineHeight = (int)currentTimelineHeight;
                _changedByDrag = true;
            }
            else if (_changedByDrag && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _changedByDrag = false;
                SaveToProject();
            }

            newContentHeight = (int)ImGui.GetWindowHeight() - (int)currentTimelineHeight -
                               4; // Hack that also depends on when a window-title is being rendered
        }

        public void Toggle()
        {
            _customTimeLineHeight = UsingCustomTimelineHeight ? UseComputedHeight : 200;
            SaveToProject();
        }

        public bool UsingCustomTimelineHeight => _customTimeLineHeight > UseComputedHeight;

        private void SyncWithProject(ProjectView projectView)
        {
            var symbolUi = projectView.RootInstance.Symbol.GetSymbolUi();
            if (symbolUi.Symbol.Id == _lastSyncedSymbolId)
                return;

            _lastSyncedSymbolId = symbolUi.Symbol.Id;
            _lastSyncedSymbolUi = symbolUi;
            _changedByDrag = false;
            _customTimeLineHeight = symbolUi.TimelineState?.TimelineHeight ?? UseComputedHeight;
        }

        private void SaveToProject()
        {
            var symbolUi = _lastSyncedSymbolUi;
            if (symbolUi == null || symbolUi.ReadOnly)
                return;

            symbolUi.TimelineState ??= new TimelineState();
            if (symbolUi.TimelineState.TimelineHeight == _customTimeLineHeight)
                return;

            symbolUi.TimelineState.TimelineHeight = _customTimeLineHeight;
            symbolUi.FlagAsModified();
        }

        private float CurrentHeight => UsingCustomTimelineHeight ? _customTimeLineHeight : ComputedTimelineHeight;

        private float ComputedTimelineHeight => MathF.Min( _timeline._selectedAnimationParameters.Count * DopeSheetArea.LayerHeight
                                                + _timeline.ClipArea.LastHeight
                                                + TimeLineDragHeight
                                                + 10, 200 * T3Ui.UiScaleFactor);

        private const int UseComputedHeight = -1;
        private int _customTimeLineHeight = UseComputedHeight;
        private bool _changedByDrag;
        private Guid _lastSyncedSymbolId;
        private SymbolUi? _lastSyncedSymbolUi;
        private readonly TimeLineCanvas _timeline;
    }
}

public enum FrameStepAmount
{
    FrameAt60Fps,
    FrameAt30Fps,
    FrameAt15Fps,
    Bar,
    Beat,
    Tick,
}