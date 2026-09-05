#nullable enable
using ImGuiNET;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Setup;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Interactive editor for a selected output, in two modes: the Output canvas (corner-pin each surface's
/// quad into the output, over the live composite) and the Content canvas (drag each surface's source
/// slice over the incoming content texture). Both reuse <see cref="CornerPinHandles"/> and the
/// <see cref="ScalableCanvas"/> pan/zoom; drags go through undo commands and persist. One per output window.
/// </summary>
internal sealed partial class SetupOutputView
{
    // Declaration order is the tab order in the segmented control — the Board first, then source-to-send:
    // lay out content, rectify the surface, view the projector composite, calibrate the projector. The morph
    // axis and every switch key off the enum values, not their order, so this is a purely visual arrangement.
    private enum EditMode
    {
        Board,
        Content,
        Straight,
        Output,
        Calibrate,
    }

    public SetupOutputView(EntityItem entityItem)
    {
        _entityItem = entityItem;
        _canvas.FillMode = ScalableCanvas.FillModes.FillAvailableContentRegion;
        _projection = new ScalableCanvasProjection(_canvas);
        _boardProjection = new BoardProjection(_boardCanvas);
    }

    /// <param name="shownSurfaceId">The surface this window is showing — the selection primary, or the pin —
    /// which gets the exclusive affordances (edge handles, anchor, the rectify basis).</param>
    public void Draw(Guid outputId, Guid shownSurfaceId = default, SetupEntitySelection? selection = null)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
            return;

        var output = setup.FindOutput(outputId);
        if (output == null)
            return;

        _shownSurfaceId = shownSurfaceId;

        DrawHeader(setup, output, outputId);

        // The Board is a view of the whole setup, not of this output — it just keeps the header's tabs.
        if (_editMode == EditMode.Board)
        {
            var boardTop = ImGui.GetCursorScreenPos();
            _boardCanvas.UpdateCanvas(out _);
            var boardList = ImGui.GetWindowDrawList();
            boardList.PushClipRect(boardTop, ImGui.GetWindowPos() + ImGui.GetWindowSize(), true);
            DrawBoardCanvas(setup, machineConfig, selection);
            boardList.PopClipRect();
            return;
        }

        DrawCameraEditor(output);

        // Calibration controls sit above the canvas, so draw them before UpdateCanvas measures the region.
        if (_editMode == EditMode.Calibrate)
            DrawCalibrationControls(output);

        // Original (0) → Straight (1) → Content (2) is one continuous axis, not three modes. The composite is
        // the content texture already warped through the corner-pin, so all three are the same pixels at
        // different points of one homography chain — a blended rectify plus a framing that tightens onto the
        // focused surface. No cross-fading anywhere. Time-driven with an ease-in power (slow start, fast
        // finish): the visual midpoint lands at 75% of the duration.
        // A Layout child straightens against its parent — that's the space it lives in — so the basis is
        // whichever surface up the chain actually carries the corner pin.
        var hasFocusBasis = SurfaceGeometry.FindCarrier(setup, _shownSurfaceId, outputId) != null;

        var target = !hasFocusBasis
                         ? 0f
                         : _editMode switch
                               {
                                   EditMode.Straight => 1f,
                                   EditMode.Content  => 2f,
                                   _                 => 0f,
                               };

        if (target != _morphTarget)
        {
            _morphTarget = target;
            _morphFrom = _viewMorph;
            _morphProgress = 0f;
            _morphFromScope = _canvas.GetCurrentScope(); // so the pan/zoom eases too, instead of snapping
        }

        if (_morphProgress < 1f)
        {
            var dt = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.1f);
            _morphProgress = MathF.Min(1f, _morphProgress + dt / _morphDuration);
            var eased = MathF.Pow(_morphProgress, _morphEaseExponent);
            _viewMorph = _morphProgress >= 1f ? _morphTarget : _morphFrom + (_morphTarget - _morphFrom) * eased;
        }

        // Clip the canvas to the region below the toolbar: it draws straight to the window draw list, so
        // without this the pan/zoom transform lets content spill up over the header.
        var canvasTop = ImGui.GetCursorScreenPos();
        _canvas.UpdateCanvas(out _);
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(canvasTop, ImGui.GetWindowPos() + ImGui.GetWindowSize(), true);

        if (_editMode == EditMode.Calibrate)
            DrawCalibrationMarkers(output, outputId);
        else if (_editMode == EditMode.Content && !hasFocusBasis)
            DrawContentCanvas(outputId); // no surface to frame onto — plain content preview
        else
            DrawOutputCanvas(setup, output, outputId, selection); // Original / Straight / Content, morphed by _viewMorph

        dl.PopClipRect();
    }



    // The output canvas carries a global rectify transform R (output px → view space): identity at _viewMorph
    // 0 (Original), and by 1 (Straight) it maps the focused surface's quad onto its own axis-aligned bounding
    // box, carrying the whole composite and every surface with it. From 1 to 2 (Content) R holds and the
    // framing tightens onto that surface. Blending R and the framing is what makes the views morph.
    private void DrawOutputCanvas(Setup setup, OutputDefinition output, Guid outputId, SetupEntitySelection? selection)
    {
        var canvasSize = new Vector2(Math.Max(1, output.CanvasResolution.Width),
                                     Math.Max(1, output.CanvasResolution.Height));

        // Stage one (0→1) blends the rectify in; stage two (1→2) keeps it, drops the surround, and undoes the
        // content→surface fit so the source ends up framed at its own aspect rather than the surface's.
        var straighten = Math.Clamp(_viewMorph, 0f, 1f);
        var toContent = Math.Clamp(_viewMorph - 1f, 0f, 1f);

        // Pulled before the transform so the content aspect below reads a live evaluation context.
        var composite = OutputManager.RenderOutput(outputId);

        // Rectify basis = the focused surface. Freeze it while it is the one being dragged, so the transform
        // doesn't chase its own edit; otherwise the live quad keeps R settled and current.
        // Whose space the focus lives in: itself, or the parent when a Layout child is selected.
        var focusCarrier = SurfaceGeometry.FindCarrier(setup, _shownSurfaceId, outputId);
        var focusCarrierId = focusCarrier?.Id ?? Guid.Empty;

        var basis = _viewMorph > 0.0001f ? focusCarrier : null;
        var basisMapping = basis?.OutputMappings.Find(m => m.OutputId == outputId);
        var basisId = basis?.Id ?? Guid.Empty;

        var rToView = _identity;
        var rToOutput = _identity;
        var viewMin = Vector2.Zero;
        var viewSize = canvasSize;

        // The framing is held still for the duration of an edit (see below), so the size it is derived from
        // only catches up on release. Refitting to that is a jump the user never asked for, so the frame the
        // freeze lifts adopts the new framing without moving the view.
        var framingFrozen = false;

        if (basisMapping != null && basisMapping.Quad.Length >= 4)
        {
            // Freeze the basis while the focused surface is being dragged, so the transform doesn't chase its
            // own edit. A corner drag only moves the quad; an edge crop rewrites the quad *and* the size, and
            // R depends on both — leaving either live makes the drag run away.
            var basisQuad = basisMapping.Quad;
            var basisSize = basis!.SizeInMeters;
            var anchor = basis.Anchor;
            if (_dragSurfaceId == basisId && _cornerDragOldQuads.TryGetValue(basisId, out var preDragQuad))
            {
                basisQuad = preDragQuad;
                framingFrozen = true;
            }
            else if (_resizeOldState != null && _edgeDragSurfaceId == basisId)
            {
                framingFrozen = true;
                if (_resizeOldState.Value.TryGetQuad(outputId, out var frozenQuad) && frozenQuad.Length >= 4)
                    basisQuad = frozenQuad;

                // The anchor is re-derived on every crop, and R is built from it — leaving it live feeds that
                // correction straight back into the drag, which runs away when the dragged edge is the anchor's.
                basisSize = _resizeOldState.Value.Size;
                anchor = _resizeOldState.Value.Anchor;
            }

            // Selecting a different surface while rectified moves the basis; ease it so the whole scene turns
            // toward the new selection instead of snapping there.
            basisQuad = BlendBasisTransition(basisId, basisQuad, ref basisSize, ref anchor, framingFrozen);

            Bounds(basisQuad, out var quadMin, out var quadMax);

            // Straightening lands on the surface's real content canvas (metres × px/m) — so Size (m) is what
            // gives the rectangle its aspect. Anchored at the anchor, so changing a dimension extends the rect
            // from there rather than recentring it.
            var straightSize = new Vector2(MathF.Max(basisSize.X, 0.001f),
                                           MathF.Max(basisSize.Y, 0.001f)) * MathF.Max(basis.PixelsPerMeter, 1f);
            var stageTarget = AnchoredRect(quadMin, quadMax, anchor, straightSize);

            // Stage two restretches to the content's own aspect (the composite holds the source already fitted
            // to the surface, so this un-squeezes it) and then keeps going, expanding to the *whole* source
            // with the slice left where the surface was — so Straight→Content zooms out from the wall onto the
            // atlas rather than stopping at the crop.
            _sliceRectInView = null;
            if (toContent > 0f
                && OutputManager.TryGetSurfaceSlice(basis.Id, out _, out var sourceTexture, out var liveUv)
                && sourceTexture is { IsDisposed: false })
            {
                // The framing is pinned to the slice as it was when Content was entered. Deriving it from the
                // live slice instead would feed every edit back into the view transform — which is what made
                // dragging one edge shift the other, and what re-framed the atlas on release.
                _sliceViewUv ??= liveUv;
                var uv = _sliceViewUv.Value;
                var uvWidth = MathF.Max(uv.Z - uv.X, 0.0001f);
                var uvHeight = MathF.Max(uv.W - uv.Y, 0.0001f);
                var aspect = MathF.Max(sourceTexture.Description.Width * uvWidth
                                       / MathF.Max(sourceTexture.Description.Height * uvHeight, 1f), 0.0001f);

                Bounds(stageTarget, out var straightMin, out var straightMax);
                var width = straightMax.X - straightMin.X;
                Bounds(AnchoredRect(straightMin, straightMax, anchor, new Vector2(width, width / aspect)),
                       out var sliceMin, out var sliceMax);

                // Grow the slice out to the whole source it was cut from, leaving the slice itself in place.
                var sliceSize = sliceMax - sliceMin;
                var sourceSize = new Vector2(sliceSize.X / uvWidth, sliceSize.Y / uvHeight);
                var sourceOrigin = new Vector2(sliceMin.X - uv.X * sourceSize.X, sliceMin.Y - uv.Y * sourceSize.Y);
                // R lands the surface on its *slice*, not on the whole source — the surface's pixels are the
                // slice's pixels. Revealing the rest of the atlas is the framing's job, below.
                var sliceCorners = RectCorners(sliceMin, sliceMax);
                stageTarget =
                    [
                        Vector2.Lerp(stageTarget[0], sliceCorners[0], toContent),
                        Vector2.Lerp(stageTarget[1], sliceCorners[1], toContent),
                        Vector2.Lerp(stageTarget[2], sliceCorners[2], toContent),
                        Vector2.Lerp(stageTarget[3], sliceCorners[3], toContent),
                    ];

                _sliceSourceTexture = sourceTexture;
                _sliceFramingTarget = (sourceOrigin, sourceOrigin + sourceSize);

                // The editable rect follows the *live* slice against that pinned source, so it resizes under
                // the cursor while the atlas stays put.
                if (toContent > 0.999f)
                {
                    _sliceSourceOrigin = sourceOrigin;
                    _sliceSourceSize = sourceSize;
                    _sliceRectInView = (sourceOrigin + new Vector2(liveUv.X, liveUv.Y) * sourceSize,
                                        sourceOrigin + new Vector2(liveUv.Z, liveUv.W) * sourceSize);
                }
            }
            else if (toContent <= 0f)
            {
                _sliceViewUv = null; // left Content — re-pin next time it's entered
                _sliceFramingTarget = null;
                _sliceSourceTexture = null;
            }

            var interp = new[]
                             {
                                 Vector2.Lerp(basisQuad[0], stageTarget[0], straighten),
                                 Vector2.Lerp(basisQuad[1], stageTarget[1], straighten),
                                 Vector2.Lerp(basisQuad[2], stageTarget[2], straighten),
                                 Vector2.Lerp(basisQuad[3], stageTarget[3], straighten),
                             };

            if (Homography.TryComputeQuadToQuad(basisQuad, interp, out rToView)
                && Homography.TryComputeQuadToQuad(interp, basisQuad, out rToOutput))
            {
                // Frame to the focused surface's straightened bounds + margin — not the whole warped canvas,
                // which a steep rectify sends toward infinity. Interpolated from the full canvas at t=0.
                // The surround shrinks to nothing as we go on to Content, so the surface itself fills the view.
                Bounds(interp, out var focusMin, out var focusMax);

                // Uniform surround from the larger dimension, not per-axis: a thin surface (a beam, a strip) has
                // a near-zero short axis, and a per-axis margin there collapses the frame onto the surface,
                // clipping the neighbouring surfaces' content out of the warped composite. Off the long side it
                // stays generous on both.
                var focusSpan = focusMax - focusMin;
                var surround = MathF.Max(focusSpan.X, focusSpan.Y) * _straightSurroundFactor * (1f - toContent);
                var m = new Vector2(surround);
                var framedMin = focusMin - m;
                var framedMax = focusMax + m;

                // Stage two opens the framing out to the whole source, so the atlas around the slice comes
                // into view while the slice itself stays put.
                if (toContent > 0f && _sliceFramingTarget != null)
                {
                    framedMin = Vector2.Lerp(framedMin, _sliceFramingTarget.Value.Min, toContent);
                    framedMax = Vector2.Lerp(framedMax, _sliceFramingTarget.Value.Max, toContent);
                }

                // Once the view and basis transitions have settled, the framing — the world window this
                // rectified view renders — stays put across edits and releases: a dragged surface stays
                // where it was dropped instead of the window re-centering on it. Re-framing comes only from
                // a basis or mode change; anything else is the user's own pan/zoom. R itself stays live, so
                // corner edits still update the rectification within the held window.
                // Held framing is only ever captured *at* the settled state — capturing during a transition
                // would freeze a half-way window. A post-edit settle ease (same basis) keeps the hold, so
                // releasing a drag never moves the camera; a basis/mode transition re-derives live.
                var framingHeld = _morphProgress >= 1f && (_basisMorph >= 1f || _easeKeepsFraming);
                if (!framingHeld)
                {
                    _frozenFramedMin = null;
                }
                else if (_frozenFramedMin == null)
                {
                    _frozenFramedMin = framedMin;
                    _frozenFramedMax = framedMax;
                }
                else
                {
                    framedMin = _frozenFramedMin.Value;
                    framedMax = _frozenFramedMax;
                }

                viewMin = Vector2.Lerp(Vector2.Zero, framedMin, straighten);
                var viewMax = Vector2.Lerp(canvasSize, framedMax, straighten);
                viewSize = viewMax - viewMin;
            }
            else
            {
                rToView = _identity;
                rToOutput = _identity;
            }
        }

        if (basisMapping == null)
            _frozenFramedMin = null; // left the rectified context — next entry re-derives the framing

        var rectifying = _viewMorph > 0.0001f;
        FitToArea(viewSize, EditMode.Output, outputId, keepScope: _framingWasFrozen && !framingFrozen);
        _framingWasFrozen = framingFrozen;

        var dl = ImGui.GetWindowDrawList();
        var frameMin = _projection.CanvasToScreen(Vector2.Zero);
        var frameMax = _projection.CanvasToScreen(viewSize);

        // The projector canvas boundary, carried through R like everything else. Drawing it as an axis-aligned
        // rect around the framing window instead would read as a false edge once the view straightens — the
        // framing is just where we render, not where the projector's coverage actually ends.
        var canvasOutline = new[]
                                {
                                    _projection.CanvasToScreen(rToView.TransformPoint(Vector2.Zero) - viewMin),
                                    _projection.CanvasToScreen(rToView.TransformPoint(new Vector2(canvasSize.X, 0)) - viewMin),
                                    _projection.CanvasToScreen(rToView.TransformPoint(canvasSize) - viewMin),
                                    _projection.CanvasToScreen(rToView.TransformPoint(new Vector2(0, canvasSize.Y)) - viewMin),
                                };

        dl.AddQuadFilled(canvasOutline[0], canvasOutline[1], canvasOutline[2], canvasOutline[3], UiColors.BackgroundFull.Fade(0.4f));

        // Past the halfway point of stage two we show the source itself rather than the composite: the two
        // agree over the slice, but only the source has the rest of the atlas, which is what's being framed.
        if (toContent > 0.5f && _sliceSourceTexture is { IsDisposed: false } && _sliceFramingTarget != null)
        {
            var sourceSrv = SrvManager.GetSrvForTexture(_sliceSourceTexture);
            if (sourceSrv is { IsDisposed: false })
            {
                var sourceMin = _projection.CanvasToScreen(_sliceFramingTarget.Value.Min - viewMin);
                var sourceMax = _projection.CanvasToScreen(_sliceFramingTarget.Value.Max - viewMin);
                dl.AddImage(sourceSrv.NativePointer, sourceMin, sourceMax);
                dl.AddQuad(canvasOutline[0], canvasOutline[1], canvasOutline[2], canvasOutline[3], UiColors.ForegroundFull.Fade(0.25f));
                DrawSliceEditor(setup, dl, focusCarrierId, viewMin, toContent);
                ResolvePicking(setup, selection);
                return;
            }
        }

        // The composite (rendered above), transformed by R. At t=0 it's drawn 1:1; while rectifying it's warped
        // into a scratch target so the perspective stays correct.
        var hasContent = false;
        if (composite is { IsDisposed: false })
        {
            if (rectifying)
            {
                var maxDim = Math.Max(viewSize.X, viewSize.Y);
                var renderScale = maxDim > 4096f ? 4096f / maxDim : 1f;
                var rtSize = new T3.Core.DataTypes.Vector.Int2(Math.Max(1, (int)(viewSize.X * renderScale)),
                                                               Math.Max(1, (int)(viewSize.Y * renderScale)));
                var w = canvasSize.X;
                var h = canvasSize.Y;
                var dest = new[]
                               {
                                   (rToView.TransformPoint(new Vector2(0, 0)) - viewMin) * renderScale,
                                   (rToView.TransformPoint(new Vector2(w, 0)) - viewMin) * renderScale,
                                   (rToView.TransformPoint(new Vector2(w, h)) - viewMin) * renderScale,
                                   (rToView.TransformPoint(new Vector2(0, h)) - viewMin) * renderScale,
                               };

                var warped = OutputManager.RenderWarpedTexture(composite, dest, rtSize);
                var warpedSrv = warped is { IsDisposed: false } ? SrvManager.GetSrvForTexture(warped) : null;
                if (warpedSrv is { IsDisposed: false })
                {
                    dl.AddImage(warpedSrv.NativePointer, frameMin, frameMax);
                    hasContent = true;
                }
            }
            else
            {
                var srv = SrvManager.GetSrvForTexture(composite);
                if (srv is { IsDisposed: false })
                {
                    dl.AddImage(srv.NativePointer, frameMin, frameMax);
                    hasContent = true;
                }
            }
        }

        dl.AddQuad(canvasOutline[0], canvasOutline[1], canvasOutline[2], canvasOutline[3], UiColors.ForegroundFull.Fade(0.25f));

        // Corner-pin handles are editable only when the morph has settled (so a mid-animation drag can't fight
        // the moving transform) and we're not on the Content end, where the projection isn't the subject.
        var editable = _morphProgress >= 1f && _viewMorph < 1.5f;

        // ...and they fade out over stage two rather than being switched off, so nothing pops.
        var handleFade = 1f - toContent;

        // A label grab that never became a drag (released before the move machinery picked it up) must not linger.
        if (_labelGrabScreen != null && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            _labelGrabScreen = null;

        // Patches sit under the surfaces in the composite, so their frames go first and the surfaces draw over them.
        DrawPatches(setup, output, selection, dl, rToView, rToOutput, viewMin, canvasSize, editable, handleFade, hasContent);

        _fenceCandidates.Clear();
        Span<Vector2> labelQuad = stackalloc Vector2[4]; // hoisted: one buffer reused by every surface
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            var mappingData = surface.OutputMappings.Find(m => m.OutputId == outputId);

            // A Layout child carries no corner pin — its quad is derived from its parent's, so it follows the
            // parent automatically. Drawn (not handled) until child editing lands, so you can see it do that.
            if (mappingData == null)
            {
                if (surface.Kind != Surface.SurfaceKinds.Layout || surface.ParentId == Guid.Empty)
                    continue;

                // The pin lives on some ancestor (possibly several levels up); edits live in the immediate
                // parent's space, so both are needed.
                var carrier = SurfaceGeometry.FindCarrier(setup, surface.Id, outputId);
                var carrierMapping = carrier?.OutputMappings.Find(m => m.OutputId == outputId);
                var immediateParent = setup.FindSurface(surface.ParentId);
                if (carrier == null || carrierMapping == null || immediateParent == null
                    || !SurfaceGeometry.TryGetChildQuad(setup, carrier, surface, carrierMapping, _childQuadBuffer))
                    continue;

                DrawChildRegion(setup, selection, dl, rToView, rToOutput, viewMin, carrier, carrierMapping, immediateParent, surface, editable, handleFade);
                continue;
            }

            // The quad in view space: R applied, then offset into the framed region.
            var viewQuad = new[]
                               {
                                   rToView.TransformPoint(mappingData.Quad[0]) - viewMin,
                                   rToView.TransformPoint(mappingData.Quad[1]) - viewMin,
                                   rToView.TransformPoint(mappingData.Quad[2]) - viewMin,
                                   rToView.TransformPoint(mappingData.Quad[3]) - viewMin,
                               };

            ImGui.PushID(surface.Id.GetHashCode());

            // A parent recedes while one of its children is the subject, so the child's handles read first.
            // Selection styling covers the whole multi-selection; the *focused* (primary) surface keeps the
            // exclusive affordances below (edge handles, anchor, isolate).
            var isFocused = surface.Id == _shownSurfaceId;
            var isSelected = isFocused
                             || (selection?.IsSelected(SetupEntitySelection.EntityKind.Surface, surface.Id) ?? false);
            var emphasis = handleFade * (!isSelected && surface.Id == focusCarrierId ? 0.45f : 1f);

            // Still draggable when unselected — the canvas has no click-to-select yet, so gating edits on
            // selection would strand every surface but the one picked in the sidebar.
            var style = CornerPinHandles.Style.ForSurface(surface.Name, editable, isSelected, emphasis);
            style.DrawChecker = !hasContent;

            // The label doubles as the surface's grab area, and it sits over the middle where an edge or corner
            // handle can land under it. Grabbing the label was the intent, so while the pointer rests on it the
            // handles go non-interactive — unless a handle drag is already live, which must not be dropped just
            // because the cursor passed over the label.
            for (var c = 0; c < 4; c++)
                labelQuad[c] = _projection.CanvasToScreen(viewQuad[c]);

            // Hovered from the sidebar or a handle (not itself the subject): highlight the frame so "which
            // frame is that row?" answers itself. The outline carries it (it reads first); the fill is only a
            // faint wash behind, and the label picks it up below.
            var surfacePulse = isSelected ? 0 : FrameStats.GetPulse(surface.Id);
            if (surfacePulse > 0.001f)
                dl.AddQuadFilled(labelQuad[0], labelQuad[1], labelQuad[2], labelQuad[3],
                                 UiColors.StatusActivated.Fade(surfacePulse * 0.2f * handleFade));

            style.EdgeColor = PulseColor(style.EdgeColor, surfacePulse);

            var handleActive = (_cornerDragOldQuads.Count > 0 && _dragSurfaceId == surface.Id)
                               || (_resizeOldState != null && _edgeDragSurfaceId == surface.Id);
            var pointerOverLabel = !handleActive && !string.IsNullOrEmpty(surface.Name)
                                   && IsMouseOverLabel(labelQuad, surface.Name);
            // In isolate only the focused frame is editable; the others are locked (they still snap).
            var lockedByIsolate = _isolate && !isFocused;
            var handlesEditable = editable && !pointerOverLabel && !lockedByIsolate;
            style.Editable = handlesEditable;

            // Selected corners render marked, and every editable corner is a fence-select candidate.
            var selectedMask = 0;
            for (var c = 0; c < 4; c++)
            {
                var cornerTarget = new SelectionTarget(SetupEntitySelection.EntityKind.Surface, surface.Id, SubPart.Corner, c);
                if (_canvasSelection.Contains(cornerTarget))
                    selectedMask |= 1 << c;

                if (handlesEditable)
                    _fenceCandidates.Add((cornerTarget, labelQuad[c]));
            }

            // The label is drawn separately so it can be hit-tested as the surface's pick/grab area.
            style.Label = null;
            var phase = CornerPinHandles.Draw(viewQuad, _projection, style, out var draggedCorner, out var cornerHovered, selectedMask);

            if (phase != CanvasPointHandle.DragPhase.None)
            {
                // Grabbing a corner selects it in the sub-element plane: ctrl toggles, shift adds, plain replaces —
                // unless the corner is already selected, which keeps the set so the grab starts a group drag.
                if (phase == CanvasPointHandle.DragPhase.Started && draggedCorner >= 0)
                {
                    var target = new SelectionTarget(SetupEntitySelection.EntityKind.Surface, surface.Id, SubPart.Corner, draggedCorner);
                    var io = ImGui.GetIO();
                    if (io.KeyCtrl)
                        _canvasSelection.Toggle(target);
                    else if (io.KeyShift)
                        _canvasSelection.Add(target);
                    else if (!_canvasSelection.Contains(target))
                        _canvasSelection.Set(target);
                }

                // Map the edited view-space quad back to projector space — only while a corner drag is live.
                // At rest the round-trip is only near-identity in float, so writing it back every frame would
                // slowly drift the stored quad while merely viewing in a rectified mode.
                var previousDraggedCorner = draggedCorner >= 0 ? mappingData.Quad[draggedCorner] : Vector2.Zero;
                for (var c = 0; c < 4; c++)
                    mappingData.Quad[c] = rToOutput.TransformPoint(viewQuad[c] + viewMin);

                // Group drag: the dragged corner's output-space delta rides onto every other selected corner.
                if (phase == CanvasPointHandle.DragPhase.Dragging && draggedCorner >= 0)
                    ApplyGroupCornerDelta(setup, outputId, surface.Id, draggedCorner,
                                          mappingData.Quad[draggedCorner] - previousDraggedCorner);
            }

            HandleDrag(phase, setup, surface.Id, outputId, mappingData.Quad);

            // The label doubles as the surface's move handle: the press selects it (through the picker, so
            // stacked labels still cycle), and holding on continues into a whole-quad move — one gesture,
            // no select-first click. The move rides the corner-drag lifecycle, so undo and the straighten
            // freeze come along for free.
            if (phase == CanvasPointHandle.DragPhase.None)
            {
                var movePhase = CanvasPointHandle.DragPhase.None;
                if (_surfaceMoveId == surface.Id)
                {
                    movePhase = ImGui.IsMouseDown(ImGuiMouseButton.Left)
                                    ? CanvasPointHandle.DragPhase.Dragging
                                    : CanvasPointHandle.DragPhase.Completed;
                }
                else if (_surfaceMoveId == Guid.Empty && _labelGrabScreen != null
                         && surface.Id == _shownSurfaceId
                         && editable && !lockedByIsolate
                         && !string.IsNullOrEmpty(surface.Name)
                         && ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                         // Below the click threshold a press is a selection click, not a grab — otherwise
                         // switching surfaces by clicking labels triggers zero-distance "moves".
                         && (ImGui.GetMousePos() - _labelGrabScreen.Value).Length() > UserSettings.Config.ClickThreshold
                         && IsPointOverLabel(labelQuad, surface.Name, _labelGrabScreen.Value))
                {
                    _labelGrabScreen = null;
                    _surfaceMoveId = surface.Id;
                    _surfaceMoveGrabCanvas = _projection.ScreenToCanvas(ImGui.GetMousePos());
                    movePhase = CanvasPointHandle.DragPhase.Started;
                }

                if (movePhase == CanvasPointHandle.DragPhase.Started)
                {
                    HandleDrag(movePhase, setup, surface.Id, outputId, mappingData.Quad);
                }
                else if (movePhase == CanvasPointHandle.DragPhase.Dragging
                         && _cornerDragOldQuads.TryGetValue(surface.Id, out var preMoveQuad))
                {
                    // Rigid in view space; carried through R per corner, so in a rectified view the quad
                    // warps exactly as if each corner had been dragged by the same screen offset.
                    var moveDelta = _projection.ScreenToCanvas(ImGui.GetMousePos()) - _surfaceMoveGrabCanvas;
                    for (var c = 0; c < 4; c++)
                        mappingData.Quad[c] = rToOutput.TransformPoint(rToView.TransformPoint(preMoveQuad[c]) + moveDelta);
                }
                else if (movePhase == CanvasPointHandle.DragPhase.Completed)
                {
                    HandleDrag(movePhase, setup, surface.Id, outputId, mappingData.Quad);
                    _surfaceMoveId = Guid.Empty;
                }
            }

            // A handle stands in for its frame: hovering one lights the frame (and its sidebar row), and
            // grabbing one selects it — so you can't edit a frame that isn't the selected item. Isolate mode
            // takes selection off the canvas entirely, so it doesn't fire there.
            if (cornerHovered || phase != CanvasPointHandle.DragPhase.None)
                FrameStats.PulseItemWithId(surface.Id);

            if (phase == CanvasPointHandle.DragPhase.Started && !_isolate)
                selection?.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);

            // Only the focused surface shows its anchor — one origin at a time, or the canvas fills with them.
            if (isFocused)
                DrawAnchorMarker(dl, surface, mappingData, rToView, viewMin, handleFade);

            // Edge handles belong to the focused surface only — they're contextual, and four extra dots on
            // every quad would drown the canvas. A corner moves freely (perspective); an edge crops.
            if (handlesEditable && surface.Id == _shownSurfaceId)
            {
                var edgePhase = CornerPinHandles.DrawEdgeHandles(viewQuad, _projection, style, out var edge, out var edgePos);
                if (edge >= 0)
                    HandleEdgeDrag(edgePhase, surface, mappingData, edge, edgePos, rToOutput, viewMin);
            }

            ImGui.PopID();

            // Under isolate the other frames' labels recede further, so the focused one clearly owns the canvas.
            var labelEmphasis = lockedByIsolate ? emphasis * 0.4f : emphasis;
            DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, labelQuad, surface.Id, surface.Name, isSelected, labelEmphasis, surfacePulse);
        }

        // Marquee over corner handles — plain output view only for now, and never while another canvas
        // drag is live (label moves and slice/annotation drags are manual, so the fence can't see them
        // through IsAnyItemActive alone).
        if (_editMode == EditMode.Output && editable
            && _cornerDragOldQuads.Count == 0 && _resizeOldState == null && _labelMoveSurfaceId == Guid.Empty
            && !ImGui.IsAnyItemActive())
        {
            UpdateCornerFence();
        }
        else
        {
            _fence.Reset();
        }

        if (basis != null && basisMapping != null)
            DrawAnnotations(dl, basis, basisMapping, rToView, rToOutput, viewMin, editable, handleFade * straighten);

        DrawSliceEditor(setup, dl, focusCarrierId, viewMin, toContent);
        ResolvePicking(setup, selection);
    }

    /// <summary>
    /// The output's patches: rectangles (or keystone quads) of canvas pixels on the direct pipe. Corners drag
    /// freely — a warped patch is a surface-less keystone; the selected patch's edges crop axis-aligned and its
    /// label moves it whole. Every edit snaps to the canvas edges and to the other patches, so tiles butt up.
    /// </summary>
    private void DrawPatches(Setup setup, OutputDefinition output, SetupEntitySelection? selection, ImDrawListPtr dl,
                             Homography rToView, Homography rToOutput, Vector2 viewMin, Vector2 canvasSize,
                             bool editable, float fade, bool hasContent)
    {
        if (output.Patches.Count == 0 || fade <= 0.01f)
            return;

        var focusedPatchId = selection != null && selection.Targets.Count > 0
                             && selection.Targets[0].Kind == SetupEntitySelection.EntityKind.Patch
                                 ? selection.Targets[0].EntityId
                                 : Guid.Empty;

        Span<Vector2> screen = stackalloc Vector2[4];
        for (var i = 0; i < output.Patches.Count; i++)
        {
            var patch = output.Patches[i];
            if (patch.Quad.Length < 4)
                continue;

            for (var c = 0; c < 4; c++)
            {
                _patchViewQuad[c] = rToView.TransformPoint(patch.Quad[c]) - viewMin;
                screen[c] = _projection.CanvasToScreen(_patchViewQuad[c]);
            }

            ImGui.PushID(patch.Id.GetHashCode());

            var label = SetupActions.PatchLabel(output, patch);
            var isFocused = patch.Id == focusedPatchId;
            var isSelected = isFocused || (selection?.IsSelected(SetupEntitySelection.EntityKind.Patch, patch.Id) ?? false);
            var pulse = isSelected ? 0f : FrameStats.GetPulse(patch.Id);

            var style = CornerPinHandles.Style.ForSurface(null, editable, isSelected, fade);
            style.DrawChecker = !hasContent;
            style.EdgeColor = PulseColor(style.EdgeColor, pulse);

            // Same label-over-handle rule as surfaces: the label is the grab area, so handles under it yield.
            var handleActive = _dragPatchId == patch.Id;
            var pointerOverLabel = !handleActive && IsMouseOverLabel(screen, label);
            style.Editable = editable && !pointerOverLabel && !_isolate;

            var phase = CornerPinHandles.Draw(_patchViewQuad, _projection, style, out var draggedCorner, out var cornerHovered);
            if (phase != CanvasPointHandle.DragPhase.None)
            {
                for (var c = 0; c < 4; c++)
                    patch.Quad[c] = rToOutput.TransformPoint(_patchViewQuad[c] + viewMin);

                if (phase == CanvasPointHandle.DragPhase.Dragging && draggedCorner >= 0 && !ImGui.GetIO().KeyShift)
                {
                    CollectPatchSnapCandidates(output, patch.Id, canvasSize);
                    var threshold = PatchSnapThreshold();
                    ref var corner = ref patch.Quad[draggedCorner];
                    Span<float> x = [corner.X];
                    if (SurfaceGeometry.TrySnapOffset(_snapXs, x, threshold, out var offsetX, out _))
                        corner.X += offsetX;

                    Span<float> y = [corner.Y];
                    if (SurfaceGeometry.TrySnapOffset(_snapYs, y, threshold, out var offsetY, out _))
                        corner.Y += offsetY;
                }
            }

            RunPatchQuadDrag(phase, patch);

            // The label doubles as the move handle — the press selects (through the picker), holding on moves.
            if (phase == CanvasPointHandle.DragPhase.None)
                HandlePatchMove(output, patch, isFocused, editable && !_isolate, label, screen, rToView, rToOutput, viewMin, canvasSize);

            if (cornerHovered || phase != CanvasPointHandle.DragPhase.None)
                FrameStats.PulseItemWithId(patch.Id);

            if (phase == CanvasPointHandle.DragPhase.Started && !_isolate)
                selection?.Select(SetupEntitySelection.EntityKind.Patch, patch.Id);

            // Edge handles for the focused patch only: an edge crops the tile, keeping the opposite edge put.
            if (style.Editable && isFocused)
            {
                var edgePhase = CornerPinHandles.DrawEdgeHandles(_patchViewQuad, _projection, style, out var edge, out var edgePos);
                if (edge >= 0)
                    HandlePatchEdgeDrag(edgePhase, output, patch, edge, edgePos, rToOutput, viewMin, canvasSize);
            }

            ImGui.PopID();
            DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Patch, screen, patch.Id, label, isSelected, fade, pulse);
        }
    }

    /// <summary>The snapshot → live edit → one undo step skeleton shared by every patch gesture.</summary>
    private void RunPatchQuadDrag(CanvasPointHandle.DragPhase phase, OutputDefinition.Patch patch)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                Array.Copy(patch.Quad, _patchOldQuad, 4);
                _dragPatchId = patch.Id;
                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_dragPatchId == patch.Id)
                {
                    if (QuadsDiffer(_patchOldQuad, patch.Quad))
                    {
                        UndoRedoStack.Add(new ChangePatchQuadCommand(patch.Id, _patchOldQuad, patch.Quad));
                        OutputSetupHandling.SaveActive();
                    }

                    _dragPatchId = Guid.Empty;
                }

                break;
        }
    }

    private void HandlePatchMove(OutputDefinition output, OutputDefinition.Patch patch, bool isFocused, bool editable, string label,
                                 ReadOnlySpan<Vector2> screen, Homography rToView, Homography rToOutput, Vector2 viewMin, Vector2 canvasSize)
    {
        var movePhase = CanvasPointHandle.DragPhase.None;
        if (_patchMoveId == patch.Id)
        {
            movePhase = ImGui.IsMouseDown(ImGuiMouseButton.Left)
                            ? CanvasPointHandle.DragPhase.Dragging
                            : CanvasPointHandle.DragPhase.Completed;
        }
        else if (_patchMoveId == Guid.Empty && _labelGrabScreen != null && isFocused && editable
                 && ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                 && (ImGui.GetMousePos() - _labelGrabScreen.Value).Length() > UserSettings.Config.ClickThreshold
                 && IsPointOverLabel(screen, label, _labelGrabScreen.Value))
        {
            _labelGrabScreen = null;
            _patchMoveId = patch.Id;
            _patchMoveGrabCanvas = _projection.ScreenToCanvas(ImGui.GetMousePos());
            movePhase = CanvasPointHandle.DragPhase.Started;
        }

        switch (movePhase)
        {
            case CanvasPointHandle.DragPhase.Started:
                RunPatchQuadDrag(movePhase, patch);
                break;

            case CanvasPointHandle.DragPhase.Dragging when _dragPatchId == patch.Id:
            {
                // Rigid in view space, carried through R per corner — the same rule as a surface move.
                var moveDelta = _projection.ScreenToCanvas(ImGui.GetMousePos()) - _patchMoveGrabCanvas;
                for (var c = 0; c < 4; c++)
                    patch.Quad[c] = rToOutput.TransformPoint(rToView.TransformPoint(_patchOldQuad[c]) + moveDelta);

                if (!ImGui.GetIO().KeyShift)
                {
                    CollectPatchSnapCandidates(output, patch.Id, canvasSize);
                    var threshold = PatchSnapThreshold();
                    QuadBounds(patch.Quad, out var min, out var max);
                    Span<float> xs = [min.X, (min.X + max.X) * 0.5f, max.X];
                    Span<float> ys = [min.Y, (min.Y + max.Y) * 0.5f, max.Y];
                    var offset = Vector2.Zero;
                    if (SurfaceGeometry.TrySnapOffset(_snapXs, xs, threshold, out var offsetX, out _))
                        offset.X = offsetX;

                    if (SurfaceGeometry.TrySnapOffset(_snapYs, ys, threshold, out var offsetY, out _))
                        offset.Y = offsetY;

                    for (var c = 0; c < 4; c++)
                        patch.Quad[c] += offset;
                }

                break;
            }

            case CanvasPointHandle.DragPhase.Completed:
                RunPatchQuadDrag(movePhase, patch);
                _patchMoveId = Guid.Empty;
                break;
        }
    }

    /// <summary>
    /// An edge drag on a patch moves that edge along its normal (a crop for a tile); with Ctrl the edge slides
    /// by the full delta (a shear). Re-based from the pre-drag quad each frame, so the edit doesn't compound.
    /// </summary>
    private void HandlePatchEdgeDrag(CanvasPointHandle.DragPhase phase, OutputDefinition output, OutputDefinition.Patch patch,
                                     int edge, Vector2 viewPos, Homography rToOutput, Vector2 viewMin, Vector2 canvasSize)
    {
        if (phase == CanvasPointHandle.DragPhase.Started)
            RunPatchQuadDrag(phase, patch);

        if (phase == CanvasPointHandle.DragPhase.Dragging && _dragPatchId == patch.Id)
        {
            var e0 = edge;
            var e1 = (edge + 1) % 4;
            var pos = rToOutput.TransformPoint(viewPos + viewMin);
            var midpoint = (_patchOldQuad[e0] + _patchOldQuad[e1]) * 0.5f;
            var delta = pos - midpoint;

            if (!ImGui.GetIO().KeyCtrl)
            {
                var along = _patchOldQuad[e1] - _patchOldQuad[e0];
                var normal = new Vector2(-along.Y, along.X);
                if (normal.LengthSquared() > 0.0001f)
                {
                    normal = Vector2.Normalize(normal);
                    delta = normal * Vector2.Dot(delta, normal);
                }
            }

            Array.Copy(_patchOldQuad, patch.Quad, 4);
            patch.Quad[e0] += delta;
            patch.Quad[e1] += delta;

            // An axis-aligned edge snaps its coordinate to the canvas edges and the neighbouring tiles.
            var horizontal = edge is 0 or 2;
            var aligned = horizontal
                              ? MathF.Abs(patch.Quad[e0].Y - patch.Quad[e1].Y) < 0.001f
                              : MathF.Abs(patch.Quad[e0].X - patch.Quad[e1].X) < 0.001f;
            if (aligned && !ImGui.GetIO().KeyShift)
            {
                CollectPatchSnapCandidates(output, patch.Id, canvasSize);
                Span<float> coordinate = [horizontal ? patch.Quad[e0].Y : patch.Quad[e0].X];
                if (SurfaceGeometry.TrySnapOffset(horizontal ? _snapYs : _snapXs, coordinate, PatchSnapThreshold(), out var offset, out _))
                {
                    var shift = horizontal ? new Vector2(0, offset) : new Vector2(offset, 0);
                    patch.Quad[e0] += shift;
                    patch.Quad[e1] += shift;
                }
            }
        }

        if (phase == CanvasPointHandle.DragPhase.Completed)
            RunPatchQuadDrag(phase, patch);
    }

    /// <summary>Canvas edges and centre plus every other patch's bounds — what a patch edit snaps to, in output px.</summary>
    private void CollectPatchSnapCandidates(OutputDefinition output, Guid excludeId, Vector2 canvasSize)
    {
        _snapXs.Clear();
        _snapYs.Clear();
        _snapXs.Add(0);
        _snapXs.Add(canvasSize.X * 0.5f);
        _snapXs.Add(canvasSize.X);
        _snapYs.Add(0);
        _snapYs.Add(canvasSize.Y * 0.5f);
        _snapYs.Add(canvasSize.Y);

        foreach (var other in output.Patches)
        {
            if (other.Id == excludeId || other.Quad.Length < 4)
                continue;

            QuadBounds(other.Quad, out var min, out var max);
            _snapXs.Add(min.X);
            _snapXs.Add((min.X + max.X) * 0.5f);
            _snapXs.Add(max.X);
            _snapYs.Add(min.Y);
            _snapYs.Add((min.Y + max.Y) * 0.5f);
            _snapYs.Add(max.Y);
        }
    }

    /// <summary>A constant screen distance expressed in output pixels at the current zoom.</summary>
    private float PatchSnapThreshold()
    {
        var a = _projection.CanvasToScreen(Vector2.Zero);
        var b = _projection.CanvasToScreen(new Vector2(1, 0));
        var screenPerCanvas = Vector2.Distance(a, b);
        return screenPerCanvas > 0.0001f ? 7 * T3Ui.UiScaleFactor / screenPerCanvas : 0f;
    }

    private static void QuadBounds(Vector2[] quad, out Vector2 min, out Vector2 max)
    {
        min = max = quad[0];
        for (var i = 1; i < quad.Length; i++)
        {
            min = Vector2.Min(min, quad[i]);
            max = Vector2.Max(max, quad[i]);
        }
    }

    private void UpdateCornerFence()
    {
        switch (_fence.UpdateAndDraw(out var selectMode))
        {
            case SelectionFence.States.Updated:
            case SelectionFence.States.CompletedAsArea:
                ApplyCornerFence(selectMode);
                break;

            case SelectionFence.States.CompletedAsClick:
                // Empty click clears only this plane — the entity plane keeps its own click rules.
                _canvasSelection.Clear();
                break;
        }
    }

    private void ApplyCornerFence(SelectionFence.SelectModes selectMode)
    {
        // Replace rebuilds from scratch every update frame, so the marquee reads live.
        if (selectMode == SelectionFence.SelectModes.Replace)
            _canvasSelection.Clear();

        var bounds = _fence.BoundsInScreen;
        for (var i = 0; i < _fenceCandidates.Count; i++)
        {
            var (target, screenPos) = _fenceCandidates[i];
            if (!bounds.Contains(screenPos))
                continue;

            if (selectMode == SelectionFence.SelectModes.Remove)
                _canvasSelection.Remove(target);
            else
                _canvasSelection.Add(target);
        }
    }



    // Preview of the content the output manager sends to this output. Per-slice source editing now lives on
    // the SendToOutput op (its SourceRect), so this canvas is a read-only backdrop.
    private void DrawContentCanvas(Guid outputId)
    {
        var content = OutputManager.TryGetOutputContent(outputId);
        if (content is not { IsDisposed: false })
        {
            CustomComponents.EmptyWindowMessage("No content yet — connect a texture to a\nSendToOutput targeting this output.");
            return;
        }

        var texSize = new Vector2(Math.Max(1, content.Description.Width), Math.Max(1, content.Description.Height));
        FitToArea(texSize, EditMode.Content, outputId);

        var dl = ImGui.GetWindowDrawList();
        var min = _projection.CanvasToScreen(Vector2.Zero);
        var max = _projection.CanvasToScreen(texSize);
        dl.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.4f));

        var srv = SrvManager.GetSrvForTexture(content);
        if (srv is { IsDisposed: false })
            dl.AddImage(srv.NativePointer, min, max);

        dl.AddRect(min, max, UiColors.ForegroundFull.Fade(0.25f));
    }

    /// <param name="output">Null while the Board is shown without any output focused.</param>
    private void DrawHeader(Setup setup, OutputDefinition? output, Guid outputId)
    {
        if (output != null)
        {
            CustomComponents.StylizedText($"{output.Name} · {output.CanvasResolution.Width}×{output.CanvasResolution.Height}",
                                          Fonts.FontSmall, UiColors.TextMuted);
        }
        else
        {
            CustomComponents.StylizedText(setup.Name, Fonts.FontSmall, UiColors.TextMuted);
        }

        ImGui.SameLine();

        // The four modes as one segmented control. Straightening rectifies a single surface, so it is only
        // usable when the focused entity resolves to one mapped to this output (for a Layout child, its
        // parent); calibration only for a projector/display. Those segments show disabled rather than
        // vanishing, so the toolbar keeps its shape.
        var straightCarrier = SurfaceGeometry.FindCarrier(setup, _shownSurfaceId, outputId);
        var canCalibrate = output?.Kind is OutputDefinition.Kinds.Projector or OutputDefinition.Kinds.Display;
        var hasOutput = output != null;
        FormInputs.SegmentedButton(ref _editMode,
                                   isItemDisabled: mode => mode switch
                                                               {
                                                                   EditMode.Board => false,
                                                                   EditMode.Straight => straightCarrier == null,
                                                                   EditMode.Calibrate => !canCalibrate,
                                                                   _ => !hasOutput,
                                                               });

        // A disabled segment can't be clicked away, so a mode left selected after its precondition lapses
        // (focus moved off the surface, output kind changed, no output at all) is reset here instead.
        if (!hasOutput)
            _editMode = EditMode.Board;
        else if (straightCarrier == null && _editMode == EditMode.Straight)
            _editMode = EditMode.Output;
        else if (!canCalibrate && _editMode == EditMode.Calibrate)
            _editMode = EditMode.Output;

        // Isolate: locks the canvas to the focused frame — the others stay visible and keep snapping, but
        // can't be selected or edited from the canvas, so you can work one frame without nudging its
        // neighbours. Only meaningful with a frame selected, and auto-clears when that lapses. Rendered in
        // StatusAttention so the locked state reads as deliberate rather than a glitch.
        var canIsolate = straightCarrier != null;
        if (!canIsolate)
            _isolate = false;

        ImGui.SameLine();
        ImGui.BeginDisabled(!canIsolate);
        var isoColor = _isolate ? UiColors.StatusAttention : UiColors.BackgroundButton;
        ImGui.PushStyleColor(ImGuiCol.Button, isoColor.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, isoColor.Fade(0.85f).Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, isoColor.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Text, (_isolate ? UiColors.ForegroundFull : UiColors.Text).Rgba);
        ImGui.AlignTextToFramePadding();
        if (ImGui.Button("Isolate") && canIsolate)
            _isolate = !_isolate;

        ImGui.PopStyleColor(4);
        ImGui.EndDisabled();
        if (canIsolate && ImGui.IsItemHovered())
            ImGui.SetTooltip("Lock the canvas to the selected frame.\nOthers stay visible and snap, but change selection in the sidebar.");

        // Measuring only makes sense against the straightened surface — on the projector canvas the
        // lengths would be perspective-foreshortened and mean nothing.
        if (_editMode == EditMode.Straight && straightCarrier != null)
        {
            ImGui.SameLine();
            if (CustomComponents.StateButton("+ Line", _measureArmed ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
                _measureArmed = !_measureArmed;

            // Nothing else on the canvas says a drag is now expected, and the tool disarms after one
            // line — so say what to do with it while it is armed.
            if (_measureArmed)
            {
                ImGui.SameLine();
                CustomComponents.StylizedText("drag along something straight in reality", Fonts.FontSmall, UiColors.StatusAnimated);
            }

            // Straighten first (it fixes the keystone but cannot know the aspect), lengths second. Both
            // stay visible and disabled rather than appearing once they happen to qualify — a button that
            // isn't there yet can't explain what it wants.
            var canStraighten = straightCarrier.Annotations.Count >= MinLinesToStraighten;
            ImGui.SameLine();
            ImGui.BeginDisabled(!canStraighten);
            if (ImGui.SmallButton("Straighten") && canStraighten)
                SetupActions.RunUndoable("Straighten from lines", setup, () => TryStraightenFromLines(straightCarrier, outputId));

            ImGui.EndDisabled();
            if (!canStraighten && ImGui.IsItemHovered())
                ImGui.SetTooltip($"Trace at least {MinLinesToStraighten} reference lines along features that are straight in reality.");

            var canApply = straightCarrier.Annotations.Exists(a => a.LengthInMeters > 0);
            ImGui.SameLine();
            ImGui.BeginDisabled(!canApply);
            if (ImGui.SmallButton("Apply lengths") && canApply)
                SetupActions.RunUndoable("Apply lengths", setup, () => TryApplyLengths(setup, straightCarrier));

            ImGui.EndDisabled();
            if (!canApply && ImGui.IsItemHovered())
                ImGui.SetTooltip("Double-click a line to give it a real length first.");
        }
        else
        {
            _measureArmed = false;
        }

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            if (surface.OutputMappings.Exists(m => m.OutputId == outputId))
                continue;

            // A Layout child rides its parent's corner pin — offering it one of its own would detach it.
            if (surface.Kind == Surface.SurfaceKinds.Layout && surface.ParentId != Guid.Empty)
                continue;

            ImGui.SameLine();
            ImGui.PushID(surface.Id.GetHashCode());
            var label = string.IsNullOrEmpty(surface.Name) ? "untitled" : surface.Name;
            if (ImGui.SmallButton("+ " + label))
            {
                SetupActions.RunUndoable("Map surface", setup, () => AddMapping(surface, output, outputId));
            }

            CustomComponents.TooltipForLastItem("Map this surface onto the output",
                                                "Drops a centered corner-pin quad you can then drag into place.");
            ImGui.PopID();
        }
    }

    // Manual projector camera used by the UseProjectorCam op (Shape 2), until calibration provides a
    // solved pose/lens. Only meaningful for projector/display outputs; collapsed by default.
    private static void DrawCameraEditor(OutputDefinition output)
    {
        if (output.Kind is not (OutputDefinition.Kinds.Projector or OutputDefinition.Kinds.Display))
            return;

        if (!ImGui.CollapsingHeader("Projector Camera"))
            return;

        var camera = output.Camera ??= new OutputDefinition.ProjectorCamera();
        var changed = false;
        changed |= ImGui.DragFloat3("Position", ref camera.ManualPosition, 0.02f);
        changed |= ImGui.DragFloat3("Target", ref camera.ManualTarget, 0.02f);
        changed |= ImGui.DragFloat("Field of View", ref camera.ManualFovYDegrees, 0.2f, 1f, 179f);
        if (changed)
            OutputSetupHandling.SaveActive();
    }

    // Calibration: place >=6 stage↔pixel correspondences, then solve the projector's pose/lens. The
    // stage positions are entered here; the output pixels are dragged as markers on the canvas.
    private static void DrawCalibrationControls(OutputDefinition output)
    {
        var camera = output.Camera ??= new OutputDefinition.ProjectorCamera();
        var points = camera.CalibrationPoints;

        if (ImGui.Button("Add Point"))
        {
            points.Add(new CalibrationPoint
                           {
                               OutputPixel = new Vector2(output.CanvasResolution.Width * 0.5f, output.CanvasResolution.Height * 0.5f),
                           });
            OutputSetupHandling.SaveActive();
        }

        ImGui.SameLine();
        if (points.Count >= ProjectorSolver.MinPointCount)
        {
            if (ImGui.Button("Solve"))
            {
                if (ProjectorSolver.TrySolve(points, output.CanvasResolution, out var result))
                {
                    camera.Pose = result.Pose;
                    camera.Lens = result.Lens;
                    camera.ResidualPx = result.MeanResidualPx;
                    OutputSetupHandling.SaveActive();
                }
                else
                {
                    Log.Warning("Projector solve failed — need 6+ points spanning two non-parallel planes.");
                }
            }
        }
        else
        {
            ImGui.TextDisabled($"Add {ProjectorSolver.MinPointCount - points.Count} more to solve");
        }

        if (camera.Pose != null)
        {
            ImGui.SameLine();
            CustomComponents.StylizedText($"residual {camera.ResidualPx:0.0} px", Fonts.FontSmall, UiColors.TextMuted);
        }

        if (ImGui.CollapsingHeader("Stage Positions"))
        {
            var removeIndex = -1;
            for (var i = 0; i < points.Count; i++)
            {
                ImGui.PushID(i);
                ImGui.SetNextItemWidth(180 * T3Ui.UiScaleFactor);
                if (ImGui.DragFloat3($"#{i + 1}", ref points[i].StagePosition, 0.02f))
                    OutputSetupHandling.SaveActive();

                ImGui.SameLine();
                if (ImGui.SmallButton("x"))
                    removeIndex = i;

                ImGui.PopID();
            }

            if (removeIndex >= 0)
            {
                points.RemoveAt(removeIndex);
                OutputSetupHandling.SaveActive();
            }
        }
    }

    private void DrawCalibrationMarkers(OutputDefinition output, Guid outputId)
    {
        var camera = output.Camera;
        if (camera == null)
            return;

        var canvasSize = new Vector2(Math.Max(1, output.CanvasResolution.Width), Math.Max(1, output.CanvasResolution.Height));
        FitToArea(canvasSize, EditMode.Calibrate, outputId);

        var dl = ImGui.GetWindowDrawList();
        var frameMin = _projection.CanvasToScreen(Vector2.Zero);
        var frameMax = _projection.CanvasToScreen(canvasSize);
        dl.AddRectFilled(frameMin, frameMax, UiColors.BackgroundFull.Fade(0.4f));

        var composite = OutputManager.RenderOutput(outputId);
        if (composite is { IsDisposed: false })
        {
            var srv = SrvManager.GetSrvForTexture(composite);
            if (srv is { IsDisposed: false })
                dl.AddImage(srv.NativePointer, frameMin, frameMax);
        }

        dl.AddRect(frameMin, frameMax, UiColors.ForegroundFull.Fade(0.25f));

        var style = CanvasPointHandle.Style.Default(UiColors.StatusAnimated);
        for (var i = 0; i < camera.CalibrationPoints.Count; i++)
        {
            ImGui.PushID(i);
            var point = camera.CalibrationPoints[i];
            var phase = CanvasPointHandle.Draw(ref point.OutputPixel, _projection, style);
            if (phase == CanvasPointHandle.DragPhase.Completed)
                OutputSetupHandling.SaveActive();

            var screen = _projection.CanvasToScreen(point.OutputPixel);
            dl.AddText(screen + new Vector2(8, -8) * T3Ui.UiScaleFactor, UiColors.ForegroundFull, $"{i + 1}");
            ImGui.PopID();
        }
    }


    /// <param name="keepScope">Adopt the new framing without moving the view — for a size change the user
    /// caused themselves, where a refit reads as the canvas jumping out from under them.</param>
    /// <summary>
    /// Eases the rectify basis from the previously focused surface to the newly selected one. Blends on a
    /// private buffer so the stored quad is never touched, and only between two real surfaces — entering or
    /// leaving the rectified view snaps (there's nothing to turn from), as does an active edit.
    /// </summary>
    private Vector2[] BlendBasisTransition(Guid basisId, Vector2[] targetQuad, ref Vector2 targetSize, ref Vector2 targetAnchor, bool frozen)
    {
        // A lifted freeze is the same situation as a basis switch: an edge crop rewrote the quad, size, and
        // anchor R is built from, and they'd land in one frame — a view jump the user never asked for. Ease
        // from the frozen state instead, so the rectified view settles onto the edit.
        if (!frozen && _basisWasFrozen && basisId == _basisTransitionId && _basisHasLast)
        {
            for (var i = 0; i < 4; i++)
                _basisFromQuad[i] = _basisLastQuad[i];

            _basisFromSize = _basisLastSize;
            _basisFromAnchor = _basisLastAnchor;
            _basisMorph = 0f;

            // Same basis: the edit settles *inside* the held framing — the camera must not chase it.
            _easeKeepsFraming = true;
        }

        _basisWasFrozen = frozen;

        if (basisId != _basisTransitionId)
        {
            if (!frozen && _basisTransitionId != Guid.Empty && basisId != Guid.Empty && _basisHasLast)
            {
                for (var i = 0; i < 4; i++)
                    _basisFromQuad[i] = _basisLastQuad[i];

                _basisFromSize = _basisLastSize;
                _basisFromAnchor = _basisLastAnchor;
                _basisMorph = 0f;
            }
            else
            {
                _basisMorph = 1f;
            }

            // A different basis is a different rectified world — the framing re-derives (with the ease).
            _easeKeepsFraming = false;
            _basisTransitionId = basisId;
        }

        var resultQuad = targetQuad;
        if (_basisMorph < 1f && !frozen)
        {
            var dt = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.1f);
            _basisMorph = MathF.Min(1f, _basisMorph + dt / _morphDuration);
            var t = MathF.Pow(_basisMorph, _morphEaseExponent);
            for (var i = 0; i < 4; i++)
                _basisBlendQuad[i] = Vector2.Lerp(_basisFromQuad[i], targetQuad[i], t);

            targetSize = Vector2.Lerp(_basisFromSize, targetSize, t);
            targetAnchor = Vector2.Lerp(_basisFromAnchor, targetAnchor, t);
            resultQuad = _basisBlendQuad;
        }

        // Remember the resolved basis, so a transition interrupted by another selection chains from here.
        for (var i = 0; i < 4; i++)
            _basisLastQuad[i] = resultQuad[i];

        _basisLastSize = targetSize;
        _basisLastAnchor = targetAnchor;
        _basisHasLast = true;
        return resultQuad;
    }

    private void FitToArea(Vector2 size, EditMode mode, Guid outputId, bool keepScope = false)
    {
        var key = (outputId, mode, size);

        // A different framed canvas shows different handles — the sub-element plane can't carry over.
        if (_fitKey.Item1 != outputId || _fitKey.Item2 != mode)
            _canvasSelection.Clear();

        // While the view morphs, ease the canvas scope from wherever the user had panned/zoomed it to the fit
        // for the current framing. Snapping straight to the fit (as we do at rest) would throw their view away
        // the instant a transition starts — the scale/offset has to animate along with everything else.
        if (_morphProgress < 1f)
        {
            var fit = FitScopeWithMargin(size);
            var eased = MathF.Pow(_morphProgress, _morphEaseExponent);
            _canvas.SetScopeInstant(new CanvasScope
                                        {
                                            Scale = Vector2.Lerp(_morphFromScope.Scale, fit.Scale, eased),
                                            Scroll = Vector2.Lerp(_morphFromScope.Scroll, fit.Scroll, eased),
                                        });
            _fitKey = key;
            return;
        }

        // Instant fit whenever the framed area changes (output, mode, or content size) — no jump-then-settle.
        if (_fitKey == key)
            return;

        if (keepScope)
        {
            _fitKey = key;
            return;
        }

        _canvas.SetScopeInstant(FitScopeWithMargin(size));
        _fitKey = key;
    }

    /// <summary>
    /// Fits <paramref name="size"/> with a small screen-space margin so a surface that overhangs the output
    /// (common in Content/Output views) isn't jammed against the window edge. Done by inflating the fitted area
    /// by the margin in canvas units — which keeps <see cref="ScalableCanvas.FitAreaOnCanvas"/>'s (correct)
    /// vertical convention, rather than the flipped one <see cref="ScalableCanvas.SetScopeToCanvasArea"/> uses.
    /// The setup canvas only — the presented output window fills its resolution edge-to-edge, deliberately.
    /// </summary>
    private CanvasScope FitScopeWithMargin(Vector2 size)
    {
        var marginPx = 10 * T3Ui.UiScaleFactor;
        _canvas.FitAreaOnCanvas(ImRect.RectWithSize(Vector2.Zero, size));

        var scale = MathF.Abs(_canvas.GetTargetScope().Scale.X);
        if (scale > 0.0001f)
        {
            var m = marginPx / scale;
            _canvas.FitAreaOnCanvas(ImRect.RectWithSize(new Vector2(-m, -m), size + new Vector2(2 * m, 2 * m)));
        }

        return _canvas.GetTargetScope();
    }

    private static void AddMapping(Surface surface, OutputDefinition output, Guid outputId)
    {
        var canvasW = Math.Max(1, output.CanvasResolution.Width);
        var canvasH = Math.Max(1, output.CanvasResolution.Height);

        var aspect = surface.SizeInMeters.Y > 0.0001f ? surface.SizeInMeters.X / surface.SizeInMeters.Y : 1f;
        var maxW = canvasW * 0.6f;
        var maxH = canvasH * 0.6f;
        var w = maxW;
        var h = w / aspect;
        if (h > maxH)
        {
            h = maxH;
            w = h * aspect;
        }

        var cx = canvasW * 0.5f;
        var cy = canvasH * 0.5f;
        var quad = new[]
                       {
                           new Vector2(cx - w * 0.5f, cy - h * 0.5f), // top-left
                           new Vector2(cx + w * 0.5f, cy - h * 0.5f), // top-right
                           new Vector2(cx + w * 0.5f, cy + h * 0.5f), // bottom-right
                           new Vector2(cx - w * 0.5f, cy + h * 0.5f), // bottom-left
                       };

        surface.OutputMappings.Add(new Surface.OutputMapping { OutputId = outputId, Quad = quad });
    }

    /// <summary>
    /// Outlines a Layout child using the quad already derived into <see cref="_childQuadBuffer"/>. Dimmer and
    /// thinner than a mapped surface, and without handles, because it isn't independently editable — its shape
    /// comes from the parent's corner pin plus its own rectangle in the parent's space.
    /// </summary>
    private void DrawChildRegion(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, Homography rToView,
                                 Homography rToOutput, Vector2 viewMin,
                                 Surface carrier, Surface.OutputMapping carrierMapping, Surface parent, Surface child,
                                 bool editable, float fade)
    {
        if (fade <= 0.01f)
            return;

        var isFocused = child.Id == _shownSurfaceId;

        // Multi-selection styling only — editing and the anchor stay with the focused (primary) region.
        var isSelected = isFocused
                         || (selection?.IsSelected(SetupEntitySelection.EntityKind.Surface, child.Id) ?? false);

        // The child's quad (already derived into the buffer) carried into the framed canvas.
        var viewQuad = new[]
                           {
                               rToView.TransformPoint(_childQuadBuffer[0]) - viewMin,
                               rToView.TransformPoint(_childQuadBuffer[1]) - viewMin,
                               rToView.TransformPoint(_childQuadBuffer[2]) - viewMin,
                               rToView.TransformPoint(_childQuadBuffer[3]) - viewMin,
                           };

        Span<Vector2> screen = stackalloc Vector2[4];
        for (var i = 0; i < 4; i++)
            screen[i] = _projection.CanvasToScreen(viewQuad[i]);

        var style = CornerPinHandles.Style.ForSurface(child.Name, editable && isFocused, isSelected, fade);
        var childPulse = isSelected ? 0f : FrameStats.GetPulse(child.Id);
        if (isSelected)
            dl.AddQuadFilled(screen[0], screen[1], screen[2], screen[3], UiColors.StatusActivated.Fade(0.12f * fade));
        else if (childPulse > 0.001f)
            dl.AddQuadFilled(screen[0], screen[1], screen[2], screen[3], UiColors.StatusActivated.Fade(childPulse * 0.2f * fade));

        // The outline carries the hover highlight, same as a top-level surface.
        CanvasDraw.QuadOutline(dl, screen, PulseColor(style.EdgeColor, childPulse), isSelected ? 2f : 1f);

        // A region has its own anchor, in its own space — mapped out through the parent's rectangle and pin.
        if (isFocused
            && SurfaceGeometry.TryGetSurfaceToOutput(carrier, carrierMapping, out var carrierToOutput)
            && SurfaceGeometry.TryGetDescendantRect(setup, carrier, child, out var rectMin, out _, out _))
        {
            var anchorInCarrier = rectMin + child.AnchorInMeters;
            DrawAnchorGlyph(dl, _projection.CanvasToScreen(rToView.TransformPoint(carrierToOutput.TransformPoint(anchorInCarrier)) - viewMin), fade);
        }

        // Edited in the parent's space: the child has no projection of its own, so the parent's inverse maps
        // handles back into plain rectangle edits. Nothing here changes the parent, so the transform driving
        // this view stays put and the drag can't feed back on itself.
        var hasInverse = SurfaceGeometry.TryGetOutputToSurface(carrier, carrierMapping, out var outputToSurface);
        if (!isFocused || !editable || !hasInverse)
        {
            // Still registered as a pick target — an unselected region has to stay clickable, which is the
            // only way to reach it while its parent is selected.
            DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, screen, child.Id, child.Name, isSelected, fade, childPulse);
            return;
        }

        ImGui.PushID(child.Id.GetHashCode());

        // The label doubles as this region's move handle, so it wins over the edge handles beneath it — unless
        // an edge crop is already live, which the cursor passing over the label mustn't drop.
        var edgeActive = _resizeOldState != null && _edgeDragSurfaceId == child.Id && _labelMoveSurfaceId == Guid.Empty;
        if (!edgeActive && !string.IsNullOrEmpty(child.Name) && IsMouseOverLabel(screen, child.Name))
            style.Editable = false;

        var edgePhase = CornerPinHandles.DrawEdgeHandles(viewQuad, _projection, style, out var edge, out var edgePos);
        if (edge >= 0)
        {
            var hasProjection = SurfaceGeometry.TryGetSurfaceToOutput(carrier, carrierMapping, out var parentProjection);
            SurfaceGeometry.TryGetDescendantRect(setup, carrier, child, out _, out _, out var edgeParentOrigin);
            HandleChildEdit(edgePhase, parent, child,
                            () =>
                            {
                                var pos = ToParentSpace(setup, carrier, child, outputToSurface, rToOutput, viewMin, edgePos);
                                var horizontal = edge is 1 or 3;
                                float? guide = null;

                                // The dragged edge snaps to the parent's and the siblings' edges and centres.
                                if (!ImGui.GetIO().KeyShift && hasProjection)
                                {
                                    SurfaceGeometry.CollectSnapCandidates(setup, parent, child.Id, _snapXs, _snapYs);
                                    Span<float> anchor = [horizontal ? pos.X : pos.Y];
                                    var thresholds = SnapThresholds(parentProjection, rToView, viewMin, parent, edgeParentOrigin, pos);
                                    if (SurfaceGeometry.TrySnapOffset(horizontal ? _snapXs : _snapYs, anchor,
                                                                      horizontal ? thresholds.X : thresholds.Y,
                                                                      out var offset, out var target))
                                    {
                                        if (horizontal)
                                            pos.X += offset;
                                        else
                                            pos.Y += offset;

                                        guide = target;
                                    }
                                }

                                SurfaceGeometry.ChildBounds(child, out var min, out var max);
                                switch (edge) // 0 = top … 3 = left in screen winding; parent space is Y-up
                                {
                                    case 0: max.Y = MathF.Max(pos.Y, min.Y + SurfaceGeometry.MinSize); break;
                                    case 1: max.X = MathF.Max(pos.X, min.X + SurfaceGeometry.MinSize); break;
                                    case 2: min.Y = MathF.Min(pos.Y, max.Y - SurfaceGeometry.MinSize); break;
                                    default: min.X = MathF.Min(pos.X, max.X - SurfaceGeometry.MinSize); break;
                                }

                                SurfaceGeometry.SetChildBounds(child, min, max);

                                if (guide.HasValue && hasProjection)
                                    DrawSnapGuide(dl, parentProjection, rToView, viewMin, parent, horizontal, guide.Value, edgeParentOrigin);
                            });
        }

        ImGui.PopID();

        DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, screen, child.Id, child.Name, isFocused, fade, childPulse);
        HandleLabelMove(setup, dl, rToView, rToOutput, viewMin, outputToSurface, carrier, carrierMapping, parent, child, screen);
    }

    /// <summary>
    /// The label doubles as the region's move handle. The label is a plain draw (no ImGui item), so the grab
    /// is detected by hand — but the lifecycle below is the same snapshot/undo skeleton as every other
    /// rectangle edit (<see cref="RunResizeDrag"/>). Free movement, but a nearly-straight drag snaps flat and
    /// draws the axis it locked to — placing a region level with its neighbours is the common case.
    /// </summary>
    private void HandleLabelMove(Setup setup, ImDrawListPtr dl, Homography rToView, Homography rToOutput, Vector2 viewMin,
                                 Homography outputToSurface, Surface carrier, Surface.OutputMapping carrierMapping,
                                 Surface parent, Surface child, ReadOnlySpan<Vector2> screen)
    {
        if (string.IsNullOrEmpty(child.Name))
            return;

        var phase = CanvasPointHandle.DragPhase.None;
        if (_labelMoveSurfaceId == child.Id)
        {
            phase = ImGui.IsMouseDown(ImGuiMouseButton.Left)
                        ? CanvasPointHandle.DragPhase.Dragging
                        : CanvasPointHandle.DragPhase.Completed;
        }
        else if (_labelMoveSurfaceId == Guid.Empty && _labelGrabScreen != null
                 && ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                 // Below the click threshold a press is a selection click, not a grab.
                 && (ImGui.GetMousePos() - _labelGrabScreen.Value).Length() > UserSettings.Config.ClickThreshold
                 && IsPointOverLabel(screen, child.Name, _labelGrabScreen.Value))
        {
            // The press selected this region through the picker; the held button continues into its move.
            _labelGrabScreen = null;
            phase = CanvasPointHandle.DragPhase.Started;
        }
        else if (_labelMoveSurfaceId == Guid.Empty
                 && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered())
        {
            var (min, max) = CornerPinHandles.GetCenteredLabelRect(screen, child.Name);
            var mouse = ImGui.GetMousePos();
            if (mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y)
                phase = CanvasPointHandle.DragPhase.Started;
        }

        if (phase == CanvasPointHandle.DragPhase.None)
            return;

        RunResizeDrag(phase, child,
                      onDragging: () => ApplyLabelMove(setup, dl, rToView, rToOutput, viewMin, outputToSurface, carrier, carrierMapping, parent, child),
                      onStarted: () =>
                                 {
                                     SurfaceGeometry.ChildBounds(child, out var startMin, out var startMax);
                                     _labelMoveSurfaceId = child.Id;
                                     _childMoveStart = (ToParentSpace(setup, carrier, child, outputToSurface, rToOutput, viewMin), startMin, startMax);
                                     _childMoveAxis = 0;
                                 },
                      onCompleted: () =>
                                   {
                                       _labelMoveSurfaceId = Guid.Empty;
                                       _childMoveStart = null;
                                       _childMoveAxis = 0;
                                   });
    }

    private void ApplyLabelMove(Setup setup, ImDrawListPtr dl, Homography rToView, Homography rToOutput, Vector2 viewMin,
                                Homography outputToSurface, Surface carrier, Surface.OutputMapping carrierMapping,
                                Surface parent, Surface child)
    {
        if (_childMoveStart == null)
            return;

        SurfaceGeometry.TryGetDescendantRect(setup, carrier, child, out _, out _, out var parentOrigin);
        var (origin, startMin, startMax) = _childMoveStart.Value;
        var delta = ToParentSpace(setup, carrier, child, outputToSurface, rToOutput, viewMin) - origin;
        var snapping = !ImGui.GetIO().KeyShift;

        var hasProjection = SurfaceGeometry.TryGetSurfaceToOutput(carrier, carrierMapping, out var surfaceToOutput);
        var halfSize = (startMax - startMin) * 0.5f;
        var thresholds = hasProjection
                             ? SnapThresholds(surfaceToOutput, rToView, viewMin, parent, parentOrigin, startMin + delta + halfSize)
                             : Vector2.Zero;

        // Nearly-straight drags flatten onto the axis — but only within a constant screen-space budget
        // (~1.5× the snap threshold). A plain direction cone widens with drag distance, and on a long
        // drag it captures from 100px+ away, which reads as violent snapping.
        if (snapping && hasProjection)
        {
            var lockX = MathF.Abs(delta.X) > MathF.Abs(delta.Y) * 4 && MathF.Abs(delta.Y) < thresholds.Y * 1.5f;
            var lockY = MathF.Abs(delta.Y) > MathF.Abs(delta.X) * 4 && MathF.Abs(delta.X) < thresholds.X * 1.5f;
            if (lockX)
                delta.Y = 0;
            else if (lockY)
                delta.X = 0;

            _childMoveAxis = lockX ? 1 : lockY ? 2 : 0;
        }
        else
        {
            _childMoveAxis = 0;
        }

        var newMin = startMin + delta;
        var newMax = startMax + delta;

        float? guideX = null;
        float? guideY = null;

        // Align to the parent's and the siblings' edges and centres — which also makes dropping a region into
        // a corner just land there, since the parent's own edges are candidates.
        if (snapping && hasProjection)
        {
            SurfaceGeometry.CollectSnapCandidates(setup, parent, child.Id, _snapXs, _snapYs);

            Span<float> anchorsX = [newMin.X, (newMin.X + newMax.X) * 0.5f, newMax.X];
            if (SurfaceGeometry.TrySnapOffset(_snapXs, anchorsX, thresholds.X, out var offsetX, out var targetX))
            {
                newMin.X += offsetX;
                newMax.X += offsetX;
                guideX = targetX;
            }

            Span<float> anchorsY = [newMin.Y, (newMin.Y + newMax.Y) * 0.5f, newMax.Y];
            if (SurfaceGeometry.TrySnapOffset(_snapYs, anchorsY, thresholds.Y, out var offsetY, out var targetY))
            {
                newMin.Y += offsetY;
                newMax.Y += offsetY;
                guideY = targetY;
            }
        }

        SurfaceGeometry.SetChildBounds(child, newMin, newMax);

        if (!hasProjection)
            return;

        if (guideX.HasValue)
            DrawSnapGuide(dl, surfaceToOutput, rToView, viewMin, parent, true, guideX.Value, parentOrigin);

        if (guideY.HasValue)
            DrawSnapGuide(dl, surfaceToOutput, rToView, viewMin, parent, false, guideY.Value, parentOrigin);

        if (_childMoveAxis == 0)
            return;

        // The locked movement axis, drawn across the parent so it reads as a guide rather than a stub.
        SurfaceGeometry.ChildBounds(child, out var minNow, out var maxNow);
        var mid = (minNow + maxNow) * 0.5f;
        DrawSnapGuide(dl, surfaceToOutput, rToView, viewMin, parent, _childMoveAxis == 2, _childMoveAxis == 1 ? mid.Y : mid.X, parentOrigin);
    }

    private static Vector2[] RectCorners(Vector2 min, Vector2 max)
    {
        return [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)];
    }

    /// <summary>
    /// Snap distances in the parent's own units per axis, from a fixed screen distance. Measured with short
    /// probes around <paramref name="probeInParent"/> (the dragged item), not across the whole parent:
    /// a rectified or keystoned view scales X and Y differently — a width-derived threshold applied to Y can
    /// catch from far more than the intended 7px — and under perspective the scale varies across the surface,
    /// so only a local measurement feels the same everywhere.
    /// </summary>
    private Vector2 SnapThresholds(Homography surfaceToOutput, Homography rToView, Vector2 viewMin, Surface parent,
                                   Vector2 originInCarrier, Vector2 probeInParent)
    {
        var probe = MathF.Max(MathF.Min(parent.SizeInMeters.X, parent.SizeInMeters.Y) * 0.05f, 0.0001f);
        var origin = ProjectParentPoint(surfaceToOutput, rToView, viewMin, originInCarrier, probeInParent);
        var alongX = ProjectParentPoint(surfaceToOutput, rToView, viewMin, originInCarrier, probeInParent + new Vector2(probe, 0));
        var alongY = ProjectParentPoint(surfaceToOutput, rToView, viewMin, originInCarrier, probeInParent + new Vector2(0, probe));

        var wantedPixels = 7 * T3Ui.UiScaleFactor;
        var pixelsX = Vector2.Distance(origin, alongX);
        var pixelsY = Vector2.Distance(origin, alongY);
        return new Vector2(pixelsX > 0.001f ? probe / pixelsX * wantedPixels : 0f,
                           pixelsY > 0.001f ? probe / pixelsY * wantedPixels : 0f);
    }

    private Vector2 ProjectParentPoint(Homography surfaceToOutput, Homography rToView, Vector2 viewMin,
                                       Vector2 originInCarrier, Vector2 pointInParent)
    {
        return _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(originInCarrier + pointInParent)) - viewMin);
    }

    private void DrawSnapGuide(ImDrawListPtr dl, Homography surfaceToOutput, Homography rToView, Vector2 viewMin,
                               Surface parent, bool vertical, float coordinate, Vector2 originInCarrier)
    {
        // Coordinates are in the parent's space; the projection expects the carrier's, so step across. The
        // guide overshoots the parent by its own size on both ends.
        var size = parent.SizeInMeters;
        SurfaceGeometry.LocalBounds(parent, out var parentMin, out var parentMax);
        var from = originInCarrier + (vertical ? new Vector2(coordinate, parentMin.Y - size.Y) : new Vector2(parentMin.X - size.X, coordinate));
        var to = originInCarrier + (vertical ? new Vector2(coordinate, parentMax.Y + size.Y) : new Vector2(parentMax.X + size.X, coordinate));

        var a = _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(from)) - viewMin);
        var b = _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(to)) - viewMin);
        dl.AddLine(a, b, UiColors.StatusAnimated.Fade(0.6f), 1 * T3Ui.UiScaleFactor);
    }

    /// <summary>The mouse in the parent surface's own space — through the view transform, then the parent's pin.</summary>
    /// <summary>
    /// A point (the cursor by default) in the child's <em>immediate parent's</em> space. The inverse only gets
    /// us into the carrier's space, so for a nested region we still have to step down by the parent's origin —
    /// that offset is what makes editing work at any nesting depth.
    /// </summary>
    private Vector2 ToParentSpace(Setup setup, Surface carrier, Surface child, Homography outputToSurface,
                                  Homography rToOutput, Vector2 viewMin, Vector2? viewPoint = null)
    {
        var inView = viewPoint ?? _projection.ScreenToCanvas(ImGui.GetMousePos());
        var inCarrier = outputToSurface.TransformPoint(rToOutput.TransformPoint(inView + viewMin));

        return SurfaceGeometry.TryGetDescendantRect(setup, carrier, child, out _, out _, out var parentOrigin)
                   ? inCarrier - parentOrigin
                   : inCarrier;
    }

    /// <summary>
    /// Centre handle that slides the whole region. Free movement, but nearly-axis-aligned drags snap flat and
    /// draw the axis they locked to — placing a region level with its neighbours is the common case.
    /// </summary>
    /// <summary>
    /// Draws a surface's label chip and registers it as a pick target. The label is the surface's grab area —
    /// hovering lifts it, clicking selects, and for a selected region dragging it moves the region.
    /// </summary>
    /// <summary>Whether the cursor is on a surface's centre label chip — its grab area, which takes priority
    /// over any handle beneath it.</summary>
    private static bool IsMouseOverLabel(ReadOnlySpan<Vector2> screenQuad, string name)
    {
        return IsPointOverLabel(screenQuad, name, ImGui.GetMousePos());
    }

    private static bool IsPointOverLabel(ReadOnlySpan<Vector2> screenQuad, string name, Vector2 screenPoint)
    {
        var (min, max) = CornerPinHandles.GetCenteredLabelRect(screenQuad, name);
        return screenPoint.X >= min.X && screenPoint.X <= max.X && screenPoint.Y >= min.Y && screenPoint.Y <= max.Y;
    }

    /// <summary>
    /// A name chip centred on an entity's quad — a surface, a region, or a slice — doubling as its pick/grab
    /// area. Registers the chip with <see cref="_picker"/> so a click can resolve to this entity, and
    /// styles by selection/hover so the same label reads the same in the tree and on the canvas.
    /// </summary>
    private void DrawEntityLabel(ImDrawListPtr dl, SetupEntitySelection.EntityKind kind, ReadOnlySpan<Vector2> screenQuad, Guid id, string name, bool isSelected,
                                 float emphasis, float pulse = 0f)
    {
        if (string.IsNullOrEmpty(name) || emphasis <= 0.01f)
            return;

        var rect = CornerPinHandles.GetCenteredLabelRect(screenQuad, name);
        _picker.AddTarget(kind, id, rect.Min, rect.Max);

        var alpha = (_picker.IsPicked(id) ? 1f : 0.9f) * emphasis;
        var text = (isSelected ? UiColors.ForegroundFull : UiColors.Text).Fade((isSelected ? 1f : 0.7f) * alpha);
        var background = (isSelected ? UiColors.StatusActivated : UiColors.BackgroundFull).Fade((isSelected ? 1f : 0.6f) * alpha);

        // Pulls the chip toward the selected look while pulsing, so the label answers the hover like the outline.
        text = PulseColor(text, pulse);
        background = PulseColor(background, pulse);
        CornerPinHandles.DrawLabelChip(dl, rect, name, text, background);
    }

    /// <summary>Mixes <paramref name="baseColor"/> toward the selection highlight by the pulse amount (see
    /// <see cref="FrameStats.GetPulse"/>) — the shared way a hovered frame's outline/label/fill light up.</summary>
    private static T3.Core.DataTypes.Vector.Color PulseColor(T3.Core.DataTypes.Vector.Color baseColor, float pulse)
    {
        return pulse <= 0.001f ? baseColor : T3.Core.DataTypes.Vector.Color.Mix(baseColor, UiColors.StatusActivated, pulse);
    }

    /// <summary>
    /// Resolves clicks on the label chips collected this frame. Overlapping labels cycle: each click picks the
    /// one after whatever is currently selected, so a stack of regions can be reached without moving anything.
    /// Also decides which label the next frame draws as hovered.
    /// </summary>
    /// <summary>
    /// One pick pass for every labeled frame on the canvas — surfaces, regions, slices. Left-click selects the
    /// label under the cursor (cycling through a stack on repeated clicks), right-click opens that entity's
    /// context menu, and the picked label pulses. Selection and menu dispatch by the target's kind; everything
    /// else — hit-test, overlap cycling, isolate gating, the right-drag guard — is identical across kinds.
    /// </summary>
    private void ResolvePicking(Setup setup, SetupEntitySelection? selection)
    {
        // Cycle relative to whatever is currently the subject, so clicking a stack walks it. The primary
        // selection is that subject for either kind (the focused surface, or the selected slice).
        var current = selection != null && selection.TryResolve(setup, out _, out var primaryId) ? primaryId : Guid.Empty;
        var hit = _picker.Resolve(current);

        if (hit.HasHit)
        {
            FrameStats.PulseItemWithId(hit.Id);

            // Isolate takes selection off the canvas: only the focused frame's label still acts (so it can move
            // and its menu opens); the others are inert until picked in the sidebar. (Slices aren't isolated.)
            var canPick = !_isolate || hit.Id == _shownSurfaceId;

            // A drag on the selected region's label moves it, so that press mustn't also count as a pick — nor
            // does a Board press that keeps the selection for a group drag.
            if (canPick && hit.LeftClicked && _labelMoveSurfaceId == Guid.Empty && _surfaceMoveId == Guid.Empty && !_boardGrabOnSelected)
            {
                SelectPicked(selection, hit.Kind, hit.Id);

                // Arm the held-grab: if the button stays down on this surface's label, its move starts next
                // frame — select-and-drag in one gesture. Plain presses only; a modifier press is a
                // selection edit, not a grab.
                var io = ImGui.GetIO();
                if (hit.Kind is SetupEntitySelection.EntityKind.Surface or SetupEntitySelection.EntityKind.Patch && !io.KeyCtrl && !io.KeyShift)
                    _labelGrabScreen = ImGui.GetMousePos();
            }

            if (canPick && hit.MenuRequested)
            {
                // Right-click selects too, so the menu always acts on what's under the cursor.
                SelectPicked(selection, hit.Kind, hit.Id);
                _menuKind = hit.Kind;
                _menuId = hit.Id;
                ImGui.OpenPopup(PickMenuId);
            }
        }

        if (ImGui.BeginPopup(PickMenuId))
        {
            DrawPickMenu(setup, selection);
            ImGui.EndPopup();
        }

        // Ctrl+D duplicates the primary selection, matching the menu entry — any duplicable kind, not just surfaces.
        if (selection != null
            && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)
            && ImGui.GetIO().KeyCtrl
            && ImGui.IsKeyPressed(ImGuiKey.D, false)
            && selection.TryResolve(setup, out var duplicateKind, out var duplicateId)
            && SetupActions.CanDuplicate(duplicateKind))
        {
            SetupActions.DuplicateEntity(selection, setup, duplicateKind, duplicateId);
        }
    }

    private void SelectPicked(SetupEntitySelection? selection, SetupEntitySelection.EntityKind kind, Guid id)
    {
        if (selection != null)
        {
            // Same modifiers as the sidebar rows: ctrl toggles, shift adds, plain replaces.
            var io = ImGui.GetIO();
            if (io.KeyCtrl)
                selection.Toggle(kind, id);
            else if (io.KeyShift)
                selection.Add(kind, id);
            else
                selection.Select(kind, id);
        }

    }

    private void DrawPickMenu(Setup setup, SetupEntitySelection? selection)
    {
        if (selection == null)
            return;

        // Same menu as the sidebar row — the shared body keeps the two from drifting apart. Rename opens
        // the sidebar row's inline editor.
        var name = SetupActions.NameForEntity(_menuKind, _menuId);

        _entityItem.DrawContextMenuItems(selection, setup, _menuKind, _menuId, name);
    }

    /// <summary>Runs a child rectangle edit through the same snapshot/undo lifecycle as a surface resize.</summary>
    private void HandleChildEdit(CanvasPointHandle.DragPhase phase, Surface parent, Surface child,
                                 Action applyDrag, Action? onStarted = null, Action? onCompleted = null)
    {
        RunResizeDrag(phase, child, applyDrag, onStarted, onCompleted);
    }

    /// <summary>
    /// The shared skeleton for a rectangle edit that resizes a surface: snapshot it for undo on Started, apply
    /// the drag each frame, and commit a <see cref="ResizeSurfaceCommand"/> on Completed. The snapshot in
    /// <see cref="_resizeOldState"/> (tagged with <see cref="_edgeDragSurfaceId"/>) is also what the morph basis
    /// freezes against mid-drag — which is why both the surface-edge crop and the region edit route through here.
    /// </summary>
    private void RunResizeDrag(CanvasPointHandle.DragPhase phase, Surface target, Action onDragging,
                               Action? onStarted = null, Action? onCompleted = null)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                _resizeOldState = new ResizeSurfaceCommand.State(target);
                _edgeDragSurfaceId = target.Id;
                onStarted?.Invoke();
                break;

            case CanvasPointHandle.DragPhase.Dragging:
                if (_resizeOldState != null)
                    onDragging();

                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_resizeOldState != null)
                {
                    // A click that never moved leaves the state identical — no undo step, no save.
                    var newState = new ResizeSurfaceCommand.State(target);
                    if (ResizeStatesDiffer(_resizeOldState.Value, newState))
                    {
                        UndoRedoStack.Add(new ResizeSurfaceCommand(target.Id, _resizeOldState.Value, newState));
                        OutputSetupHandling.SaveActive();
                    }

                    _resizeOldState = null;
                    _edgeDragSurfaceId = Guid.Empty;
                }

                onCompleted?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Marks the surface's anchor — where the calibration raster's origin sits, and what a resize grows from.
    /// Drawn as a crosshair ring so it can't be confused with the orange top-left corner, which only marks the
    /// quad's winding.
    /// </summary>
    private void DrawAnchorMarker(ImDrawListPtr dl, Surface surface, Surface.OutputMapping mapping,
                                  Homography rToView, Vector2 viewMin, float fade)
    {
        if (fade <= 0.01f || !SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, out var surfaceToOutput))
            return;

        // The anchor is the origin of surface space.
        DrawAnchorGlyph(dl, _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(Vector2.Zero)) - viewMin), fade);
    }

    // Deliberately not the corner marker's orange (StatusAnimated) — an anchor and a winding cue are unrelated.
    private static void DrawAnchorGlyph(ImDrawListPtr dl, Vector2 screen, float fade)
    {
        CanvasDraw.Crosshair(dl, screen, UiColors.StatusControlled.Fade(fade));
    }

    /// <summary>
    /// An edge drag crops the surface's rectangle — moving that edge while the opposite one stays put. Ctrl
    /// stretches instead: same physical rectangle, different area on the projector. The handle is dragged in
    /// view space, so it's carried back through R into projector pixels and then into the surface's own space,
    /// where both are a plain rect edit.
    /// </summary>
    private void HandleEdgeDrag(CanvasPointHandle.DragPhase phase, Surface surface, Surface.OutputMapping mapping,
                                int edge, Vector2 viewPos, Homography rToOutput, Vector2 viewMin)
    {
        RunResizeDrag(phase, surface, () =>
                                      {
                                          // Re-base to the pre-drag rectangle first: the crop rewrites the surface's own frame, so
                                          // an incremental edit would compound frame over frame. From the snapshot the cursor maps to
                                          // one absolute edge position, stable however long the drag runs.
                                          _resizeOldState!.Value.Restore(surface);
                                          if (!SurfaceGeometry.TryGetOutputToSurface(surface, mapping, out var outputToSurface))
                                              return;

                                          var surfacePos = outputToSurface.TransformPoint(rToOutput.TransformPoint(viewPos + viewMin));
                                          SurfaceGeometry.DragEdge(surface, edge, surfacePos, ImGui.GetIO().KeyCtrl);
                                      });
    }

    private void HandleDrag(CanvasPointHandle.DragPhase phase, Setup setup, Guid surfaceId, Guid outputId, Vector2[] liveQuad)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                // The quads still hold their pre-drag values on the activation frame. Snapshot every surface
                // the drag can touch: the grabbed one plus any with corners in the sub-element selection.
                _cornerDragOldQuads.Clear();
                _cornerDragOldQuads[surfaceId] = (Vector2[])liveQuad.Clone();
                for (var i = 0; i < _canvasSelection.Count; i++)
                {
                    var target = _canvasSelection[i];
                    if (target.Part != SubPart.Corner || _cornerDragOldQuads.ContainsKey(target.EntityId))
                        continue;

                    var mapping = setup.FindSurface(target.EntityId)?.OutputMappings.Find(m => m.OutputId == outputId);
                    if (mapping != null)
                        _cornerDragOldQuads[target.EntityId] = (Vector2[])mapping.Quad.Clone();
                }

                _dragSurfaceId = surfaceId;
                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_cornerDragOldQuads.Count > 0)
                {
                    // Values were applied live during the drag; one undo step covers the whole group.
                    _cornerDragCommands.Clear();
                    foreach (var (id, oldQuad) in _cornerDragOldQuads)
                    {
                        var mapping = setup.FindSurface(id)?.OutputMappings.Find(m => m.OutputId == outputId);
                        if (mapping == null || !QuadsDiffer(oldQuad, mapping.Quad))
                            continue;

                        _cornerDragCommands.Add(new ChangeOutputMappingQuadCommand(id, outputId, oldQuad, mapping.Quad));
                    }

                    if (_cornerDragCommands.Count == 1)
                        UndoRedoStack.Add(_cornerDragCommands[0]);
                    else if (_cornerDragCommands.Count > 1)
                        UndoRedoStack.Add(new MacroCommand("Adjust corner pins", _cornerDragCommands));

                    if (_cornerDragCommands.Count > 0)
                        OutputSetupHandling.SaveActive();

                    _cornerDragCommands.Clear();
                    _cornerDragOldQuads.Clear();
                    _dragSurfaceId = Guid.Empty;
                }

                break;
        }
    }

    /// <summary>Moves every other selected corner by the dragged corner's output-space delta — the group drag.</summary>
    private void ApplyGroupCornerDelta(Setup setup, Guid outputId, Guid draggedSurfaceId, int draggedCorner, Vector2 delta)
    {
        if (_canvasSelection.Count < 2 || (delta.X == 0 && delta.Y == 0))
            return;

        for (var i = 0; i < _canvasSelection.Count; i++)
        {
            var target = _canvasSelection[i];
            if (target.Part != SubPart.Corner)
                continue;

            if (target.EntityId == draggedSurfaceId && target.Index == draggedCorner)
                continue;

            var mapping = setup.FindSurface(target.EntityId)?.OutputMappings.Find(m => m.OutputId == outputId);
            if (mapping != null && target.Index >= 0 && target.Index < mapping.Quad.Length)
                mapping.Quad[target.Index] += delta;
        }
    }

    private static bool ResizeStatesDiffer(in ResizeSurfaceCommand.State a, in ResizeSurfaceCommand.State b)
    {
        if (a.Size != b.Size || a.LocalPosition != b.LocalPosition || a.Anchor != b.Anchor)
            return true;

        if (a.Quads.Length != b.Quads.Length)
            return true;

        for (var i = 0; i < a.Quads.Length; i++)
        {
            if (a.Quads[i].OutputId != b.Quads[i].OutputId || QuadsDiffer(a.Quads[i].Quad, b.Quads[i].Quad))
                return true;
        }

        return false;
    }

    private static bool QuadsDiffer(Vector2[] a, Vector2[] b)
    {
        for (var i = 0; i < a.Length && i < b.Length; i++)
        {
            if (a[i] != b[i])
                return true;
        }

        return false;
    }

    /// <summary>
    /// An axis-aligned rect of <paramref name="size"/> placed so its anchor coincides with the same anchor of
    /// the reference box — so resizing extends the rect from the anchor instead of recentring it. The anchor
    /// is signed and Y-up, while canvas Y grows downward. Returns TL, TR, BR, BL.
    /// </summary>
    private static Vector2[] AnchoredRect(Vector2 refMin, Vector2 refMax, Vector2 anchor, Vector2 size)
    {
        var t = (anchor + Vector2.One) * 0.5f;
        var anchorX = refMin.X + t.X * (refMax.X - refMin.X);
        var anchorY = refMax.Y - t.Y * (refMax.Y - refMin.Y);

        var minX = anchorX - t.X * size.X;
        var maxX = minX + size.X;
        var maxY = anchorY + t.Y * size.Y;
        var minY = maxY - size.Y;

        return [new Vector2(minX, minY), new Vector2(maxX, minY), new Vector2(maxX, maxY), new Vector2(minX, maxY)];
    }

    private static void Bounds(Vector2[] points, out Vector2 min, out Vector2 max)
    {
        min = max = points[0];
        for (var i = 1; i < points.Length; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }
    }

    // Straight morph: fraction of the focused surface's straightened size kept as surround margin (context).
    private const float _straightSurroundFactor = 0.4f;
    private static readonly Homography _identity = new() { M11 = 1, M22 = 1, M33 = 1 };

    // View morph timing: eased in so it starts slowly and finishes quickly. The exponent solves 0.75^k = 0.5,
    // i.e. the visual midpoint is reached at 75% of the duration.
    private const float _morphDuration = 0.5f;
    private const float _morphEaseExponent = 2.41f;

    private readonly ScalableCanvas _canvas = new();
    private readonly ScalableCanvasProjection _projection;
    private readonly EntityItem _entityItem;
    private EditMode _editMode = EditMode.Board; // the Board is the home view, so a fresh window opens on it
    private bool _isolate;
    private Guid _shownSurfaceId; // frame-scoped: what the caller passed to this Draw, never read across frames
    private (Guid, EditMode, Vector2) _fitKey;

    // View morph position: 0 = Original (projector space), 1 = Straight (focused surface rectified),
    // 2 = Content (framing tightened onto that surface). One continuous axis, animated.
    private float _viewMorph;
    private float _morphTarget;
    private float _morphFrom;
    private float _morphProgress = 1f; // 1 = settled (no animation running)

    // Canvas scale/offset at the moment the morph started, so the framing eases from the user's view.
    private CanvasScope _morphFromScope;

    // Basis transition: eases the rectify basis (quad/size/anchor) from the previously focused surface to the
    // newly selected one, so switching selection in a rectified view turns the scene rather than snapping.
    private readonly Vector2[] _basisFromQuad = new Vector2[4];
    private readonly Vector2[] _basisLastQuad = new Vector2[4];
    private readonly Vector2[] _basisBlendQuad = new Vector2[4];
    private Vector2 _basisFromSize, _basisLastSize, _basisFromAnchor, _basisLastAnchor;
    private Guid _basisTransitionId;
    private float _basisMorph = 1f; // 1 = settled
    private bool _basisHasLast;
    private bool _basisWasFrozen; // last frame's freeze, so a lifted freeze can ease instead of jumping

    // The settled straight framing, held across edits (null = re-derive on the next rectified frame).
    private Vector2? _frozenFramedMin;
    private Vector2 _frozenFramedMax;
    private bool _easeKeepsFraming; // post-edit settle (same basis): ease R, but hold the framing window

    // Pre-drag quad snapshots for every surface a corner drag can touch — the grabbed one plus any with
    // selected corners. Non-empty = a corner drag is live; also serves the straighten path's pre-drag basis.
    private readonly Dictionary<Guid, Vector2[]> _cornerDragOldQuads = new();
    private Guid _dragSurfaceId;

    // The canvas sub-element plane: selected mapping-quad corners (SelectionTarget.Part == Corner) of the
    // shown output canvas. Deliberately separate from the entity selection — the two planes never mix.
    private readonly SelectionSet<SelectionTarget> _canvasSelection = new();
    private readonly SelectionFence _fence = new();
    private readonly List<(SelectionTarget Target, Vector2 ScreenPos)> _fenceCandidates = new();
    private static readonly List<ICommand> _cornerDragCommands = [];

    // Label chips collected this frame (id + screen rect) and the pick they resolve to — labels double as
    // each surface's click target, and overlapping ones cycle.
    private readonly CanvasItemPicker<SetupEntitySelection.EntityKind> _picker = new();
    private Guid _labelMoveSurfaceId;

    // The held-grab handoff: a plain press on a surface/region label selects it (via the picker, which
    // cycles stacks); if the button is still down next frame, the move machinery starts from this position.
    private Vector2? _labelGrabScreen;

    // Whole-quad move of a top-level surface by its label (regions use _labelMoveSurfaceId instead).
    private Guid _surfaceMoveId;
    private Vector2 _surfaceMoveGrabCanvas;

    // Patch gestures: the quad in view space (reused per patch), the pre-drag snapshot, and whose it is.
    private readonly Vector2[] _patchViewQuad = new Vector2[4];
    private readonly Vector2[] _patchOldQuad = new Vector2[4];
    private Guid _dragPatchId;
    private Guid _patchMoveId;
    private Vector2 _patchMoveGrabCanvas;
    private SetupEntitySelection.EntityKind _menuKind;
    private Guid _menuId;

    // Where the focused target's slice sits once the view has zoomed fully out onto the source, in view units.
    private (Vector2 Min, Vector2 Max)? _sliceRectInView;

    // Slice framing pinned while Content is open, plus the resulting source mapping in view units.
    private Vector4? _sliceViewUv;
    private Vector2 _sliceSourceOrigin;
    private Vector2 _sliceSourceSize;
    private (Vector2 Min, Vector2 Max)? _sliceFramingTarget;
    private T3.Core.DataTypes.Texture2D? _sliceSourceTexture;

    // Snap candidates in the parent's space, rebuilt per drag frame; reused so dragging doesn't allocate.
    private readonly List<float> _snapXs = [];
    private readonly List<float> _snapYs = [];
    private const string PickMenuId = "##canvasPickMenu";

    // Scratch for a Layout child's derived quad; consumed before the next child reuses it.
    private readonly Vector2[] _childQuadBuffer = new Vector2[4];

    // Move-gizmo drag: the cursor and rectangle in the parent's space when it started, plus the axis a
    // nearly-straight drag locked to (0 none, 1 horizontal, 2 vertical) so the guide can be drawn.
    private (Vector2 Origin, Vector2 Min, Vector2 Max)? _childMoveStart;
    private int _childMoveAxis;

    // Pre-drag rectangle snapshot for an edge crop (size + every quad that follows it), and whose it is.
    private bool _framingWasFrozen;
    private ResizeSurfaceCommand.State? _resizeOldState;
    private Guid _edgeDragSurfaceId;
}
