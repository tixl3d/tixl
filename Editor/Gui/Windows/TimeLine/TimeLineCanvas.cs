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
internal sealed class TimeLineCanvas : AnimationCanvas
{
    public TimeLineCanvas(NodeSelection nodeSelection, Func<Instance> getCompositionOp, Func<Guid, bool> requestChildCompositionFunc)
    {
        _nodeSelection = nodeSelection;

        DopeSheetArea = new DopeSheetArea(SnapHandlerForU, this);
        _timelineCurveEditArea = new TimelineCurveEditArea(this, SnapHandlerForU, SnapHandlerForV);
        _timeSelectionRange = new TimeSelectionRange(this, SnapHandlerForU);
        _selectionRangeIndicator = new SelectionRangeIndicator(this, SnapHandlerForU);
        _timeSelectionArea = new TimeSelectionArea(this);
        LayersArea = new LayersArea(this, getCompositionOp, requestChildCompositionFunc, SnapHandlerForU);
        _curveEditCanvas = new CurveEditCanvas(this, _timelineCurveEditArea, _horizontalRaster);

        SnapHandlerForV.AddSnapAttractor(_horizontalRaster);
        SnapHandlerForU.AddSnapAttractor(_clipRange);
        SnapHandlerForU.AddSnapAttractor(_loopRange);
        SnapHandlerForU.AddSnapAttractor(_timeRasterSwitcher);
        SnapHandlerForU.AddSnapAttractor(_currentTimeMarker);
        SnapHandlerForU.AddSnapAttractor(LayersArea);
        SnapHandlerForU.AddSnapAttractor(_selectionRangeIndicator);
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
    /// a single editor (DopeSheetArea in DopeView mode, TimelineCurveEditArea in CurveEditor mode);
    /// future split-view will expose both simultaneously. Cross-mode components (SRI, TimeSelectionArea,
    /// TimeWarpDrag) aggregate through <see cref="KeyframeEditors"/> so they don't need to pick one.
    /// </summary>
    public readonly KeyframeEditorGroup KeyframeEditors;

    /// <summary>
    /// Invoked when the keyframe selection is explicitly replaced (not merely added to / removed from).
    /// Lets the SRI drop its TimeWarp handles since they were configured for the previous selection.
    /// </summary>
    internal void OnKeyframeSelectionReplaced() => _selectionRangeIndicator.ClearTimeWarpHandles();

    public NodeSelection NodeSelection => _nodeSelection;

    private int RulerHeight => (int)(25 * T3Ui.UiScaleFactor);
    private int SummaryHeight => (int)(11 * T3Ui.UiScaleFactor);

    private int _lastSelectionRevision = -1;
    
    public void Draw(ProjectView projectView, Playback playback)
    {
        Debug.Assert(projectView.CompositionInstance != null);
        
        var compositionOp = projectView.CompositionInstance;
        Current = this;
        Playback = playback;
        SyncStateWithComposition(compositionOp);

        if(MathUtils.HasChanged(ref _lastSelectionRevision, projectView.NodeSelection.ChangeCounter))
            _selectedAnimationParameters = GetAnimationParametersForSelectedNodes(compositionOp);

        PruneExpandedForMissingParams();

        // Very ugly hack to prevent scaling the output above window size
        var keepScale = T3Ui.UiScaleFactor;

        ScrollToTimeAfterStopped();

        var modeChanged = UpdateMode();
        SyncInlineCurveEditorRegistration();

        // When the inline curve-edit layout is active, the two sub-canvases own interaction
        // (zoom, pan, fence) inside their own child windows. Suppress the outer canvas's
        // zoom/pan/fence so they don't double-fire.
        var inlineLayoutActive = Mode == Modes.DopeView && CurveEditingParamHashes.Count > 0;
        var outerFlags = T3Ui.EditingFlags.AllowHoveredChildWindows;
        if (inlineLayoutActive)
        {
            // In the inline layout the outer timeline canvas still owns U state, but when the
            // mouse is over the curve-edit area, that child's own V-only canvas should be the
            // one handling wheel/RMB. Cede interaction here to avoid double-processing.
            if (CurveEditAreaScreenRect.HasValue
                && CurveEditAreaScreenRect.Value.Contains(ImGui.GetMousePos()))
            {
                outerFlags |= T3Ui.EditingFlags.PreventZoomWithMouseWheel
                              | T3Ui.EditingFlags.PreventPanningWithMouse;
            }
        }

        DrawAnimationCanvas(drawAdditionalCanvasContent: DrawCanvasContent,
                            inlineLayoutActive ? null : _selectionFence,
                            0,
                            outerFlags);
        Current = null;

        T3Ui.UiScaleFactor = keepScale;
        return;

        void DrawCanvasContent(InteractionState interactionState)
        {
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
                _selectionRangeIndicator.Draw(compositionOp, ImGui.GetWindowDrawList());
                ImGui.EndChild();
            }


            // Selection Area (summary strip below ruler)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.GridLines.Fade(0.15f).Rgba);
                ImGui.BeginChild("##selectionArea", new Vector2(0,SummaryHeight));
                _timeSelectionArea.Draw(compositionOp, _selectedAnimationParameters, KeyframeEditors, ImGui.GetWindowDrawList());
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

                switch (Mode)
                {
                    case Modes.DopeView:
                        if (CurveEditingParamHashes.Count > 0)
                        {
                            var availHeight = ImGui.GetContentRegionAvail().Y;
                            var topHeight = MathF.Max(40f, availHeight * CurvePaneHeightRatio);
                            var bottomHeight = MathF.Max(40f, availHeight - topHeight);

                            // Dope sheet runs on the outer canvas — no sub-canvas wrapping, so U
                            // interactions go directly to TimeLineCanvas and the CEA pane never
                            // touches DSA state. Child window gives us an auto vertical scrollbar
                            // when the parameter list overflows.
                            ImGui.BeginChild("##dopeSheetArea", new Vector2(0, topHeight),
                                             ImGuiChildFlags.None,
                                             ImGuiWindowFlags.NoBackground
                                             | ImGuiWindowFlags.NoScrollWithMouse);
                            LayersArea.Draw(compositionOp, Playback, SnapHandlerForU);
                            DopeSheetArea.Draw(compositionOp, _selectedAnimationParameters);

                            // Dope-local fence (outer fence is null in inline layout). Dispatches
                            // only to DSA + LayersArea so a fence drawn in dope never selects keys
                            // that live only in the curve pane.
                            if (!T3Ui.IsAnyPopupOpen)
                            {
                                switch (_dopeFence.UpdateAndDraw(out var selectMode))
                                {
                                    case SelectionFence.States.Updated:
                                    case SelectionFence.States.CompletedAsClick:
                                        if (selectMode == SelectionFence.SelectModes.Replace)
                                        {
                                            SharedSelectedKeyframes.Clear();
                                            selectMode = SelectionFence.SelectModes.Add;
                                        }
                                        DopeSheetArea.UpdateSelectionForArea(_dopeFence.BoundsInScreen, selectMode);
                                        LayersArea.UpdateSelectionForArea(_dopeFence.BoundsInScreen, selectMode);
                                        break;
                                }
                            }

                            // RMB-drag-to-scroll applies to the *current* ImGui window — i.e. the
                            // dope child here, not the outer timeline body. Without this, the
                            // outer-scoped call further down scrolls a window with no overflow
                            // and the user's drag does nothing.
                            CustomComponents.HandleDragScrolling(this);
                            ImGui.EndChild();

                            ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundFull.Fade(0.2f).Rgba);
                            _curveEditCanvas.Draw(compositionOp, _selectedAnimationParameters, bottomHeight, modeChanged);
                            ImGui.PopStyleColor();
                        }
                        else
                        {
                            LayersArea.Draw(compositionOp, Playback, SnapHandlerForU);
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
                    _clipRange.Draw(this, compositionTimeClip, Drawlist, SnapHandlerForU);
                }

                // In the inline layout the dope-child handles its own drag-scroll (above); outer
                // call would act on ImGuiTitle (no overflow) and nothing would scroll.
                if (!inlineLayoutActive)
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
    /// Keeps TimelineCurveEditArea registered with the snap handler and keyframe-editor group
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
                foreach (var curve in next.Curves)
                {
                    if (!curve.TryGetNextKey(time, out var key)
                        || key.U > bestNextTime)
                        continue;

                    foundNext = true;
                    bestNextTime = key.U;
                }
            }

            if (foundNext)
                Playback.TimeInBars = bestNextTime;
        }

        if (UserActionRegistry.WasActionQueued(UserActions.PlaybackJumpToPreviousKeyframe))
        {
            var bestPreviousTime = double.NegativeInfinity;
            var foundNext = false;

            var time = Playback.TimeInBars - 0.001f;
            foreach (var next in _selectedAnimationParameters)
            {
                foreach (var curve in next.Curves)
                {
                    if (!curve.TryGetPreviousKey(time, out var key)
                        || key.U < bestPreviousTime)
                        continue;

                    foundNext = true;
                    bestPreviousTime = key.U;
                }
            }

            if (foundNext)
                Playback.TimeInBars = bestPreviousTime;
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

    private void ScrollToTimeAfterStopped()
    {
        var isPlaying = Math.Abs(Playback.PlaybackSpeed) > 0.01f;
        var wasPlaying = Math.Abs(_lastPlaybackSpeed) > 0.01f;

        if (!isPlaying && wasPlaying)
        {
            if (!IsCurrentTimeVisible())
            {
                // assume we are not scrolling, what screen position would the playhead be at?
                var oldScroll = Scroll;
                Scroll = new Vector2(0, Scroll.Y);
                var posScreen = TransformX((float)Playback.TimeInBars);
                // position that playhead in the center of the window
                ScrollTarget.X = InverseTransformX(posScreen - WindowSize.X * 0.5f);
                // restore old state of scrolling
                Scroll = oldScroll;
            }
        }

        _lastPlaybackSpeed = Playback.PlaybackSpeed;
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
                TimeObjectManipulators.Remove(LayersArea);
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
                TimeObjectManipulators.Add(LayersArea);
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

    // --- Per-parameter curve-expand state (see Plan_DopeSheetCurveExpand.md) ---
    // Populated by the curve-toggle icon on each dope-sheet row.
    // When non-empty in DopeView mode, the timeline body splits into a dope-sheet pane
    // on top and an inline curve-editor pane below.
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
    internal float CurvePaneHeightRatio = 0.5f;

    // Published by CurveEditCanvas each frame it draws; null when the pane isn't visible.
    // TimeLineCanvas.Draw reads this at the top of the next frame to cede wheel/pan to the
    // curve-area sub-canvas when the mouse is inside it.
    internal ImRect? CurveEditAreaScreenRect;

    // Shared keyframe selection — DopeSheetArea and TimelineCurveEditArea both receive this
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

        // Save to previous composition
        if (_lastSyncedSymbolUi != null)
        {
            _lastSyncedSymbolUi.TimelineState ??= new TimelineState();
            SaveStateTo(_lastSyncedSymbolUi.TimelineState);
            _lastSyncedSymbolUi.FlagAsModified();
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
        state.TimelineHeight = FoldingHeight._customTimeLineHeight;
    }

    internal void LoadStateFrom(TimelineState state)
    {
        ScaleTarget = new Vector2(state.ScaleX, ScaleTarget.Y);
        Scale = new Vector2(state.ScaleX, Scale.Y);
        ScrollTarget = new Vector2(state.ScrollX, ScrollTarget.Y);
        Scroll = new Vector2(state.ScrollX, Scroll.Y);
        Mode = state.Mode;
        FoldingHeight._customTimeLineHeight = state.TimelineHeight;
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
    public readonly LayersArea LayersArea;
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

    private readonly TimelineCurveEditArea _timelineCurveEditArea;
    private readonly CurveEditCanvas _curveEditCanvas;

    private readonly CurrentTimeMarker _currentTimeMarker = new();
    private readonly TimeSelectionRange _timeSelectionRange;
    private readonly SelectionRangeIndicator _selectionRangeIndicator;
    private readonly TimeSelectionArea _timeSelectionArea;
    private readonly IValueSnapAttractor[] _selectionDragSnapExclusions;
    private readonly List<AnimationParameterEditing> _activeKeyframeEditors = new(2);
    private readonly NodeSelection _nodeSelection;

    private double _lastPlaybackSpeed;
    private readonly List<AnimationParameter> _pinnedParams = new(20);
    private readonly List<AnimationParameter> _curvesForSelection = new(64);
    private readonly SelectionFence _selectionFence = new();
    private readonly SelectionFence _dopeFence = new();

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
    }
    
    internal sealed class TimelineHeight
    {
        public TimelineHeight(TimeLineCanvas timeline)
        {
            _timeline = timeline;
        }

        public void DrawSplit(out int newContentHeight)
        {
            var currentTimelineHeight = _timeline.FoldingHeight.CurrentHeight;
            if (CustomComponents.SplitFromBottom(ref currentTimelineHeight))
            {
                _customTimeLineHeight = (int)currentTimelineHeight;
            }

            newContentHeight = (int)ImGui.GetWindowHeight() - (int)currentTimelineHeight -
                               4; // Hack that also depends on when a window-title is being rendered            
        }

        private const int UseComputedHeight = -1;
        internal int _customTimeLineHeight = UseComputedHeight;
        private readonly TimeLineCanvas _timeline;
        public bool UsingCustomTimelineHeight => _customTimeLineHeight > UseComputedHeight;

        private float CurrentHeight => UsingCustomTimelineHeight ? _customTimeLineHeight : ComputedTimelineHeight;

        private float ComputedTimelineHeight => MathF.Min( _timeline._selectedAnimationParameters.Count * DopeSheetArea.LayerHeight
                                                + _timeline.LayersArea.LastHeight
                                                + TimeLineDragHeight
                                                + 10, 200 * T3Ui.UiScaleFactor);

        public void Toggle()
        {
            _customTimeLineHeight = UsingCustomTimelineHeight ? UseComputedHeight : 200;
        }
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