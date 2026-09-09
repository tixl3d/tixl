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
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The setup's one canvas: the Board (every entity at its neutral placement, in metres) and the spaces that
/// fold out of it — an output's canvas (corner-pin each surface's quad over the live composite, Straight and
/// Content morphs), a source's texture (lay out its slices), a projector's calibration. A space draws its
/// pixels inside its entity's Board card through <see cref="SpaceProjection"/>, so entering one is a camera
/// move plus the participating entities flying into place while the rest fades. Handles reuse
/// <see cref="CornerPinHandles"/>; drags go through undo commands and persist. One per output window.
/// </summary>
internal sealed partial class SetupOutputView
{
    // Declaration order is the tab order in the segmented control — the Board first, then the two cameras:
    // the surface seen flat, the projector's composite. The morph axis and every switch key off the enum
    // values, not their order, so this is a purely visual arrangement. A content source has no camera of its
    // own: its texture is already flat on its Board card, where its slices are drawn and edited.
    private enum EditMode
    {
        Board,
        Straight,
        Output,
    }

    public SetupOutputView(EntityItem entityItem)
    {
        _entityItem = entityItem;
        _boardProjection = new BoardProjection(_boardCanvas);
        _projection = new SpaceProjection(_boardProjection);
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
        OpenedReferenceImageId = Guid.Empty;

        if (!DeferHeader(HeaderKinds.Modes, outputId))
            DrawHeader(setup, output, outputId);

        // Original (0) → Straight (1) is one continuous axis, not two modes. The composite is the content
        // texture already warped through the corner-pin, so both are the same pixels at different points of one
        // homography chain — a blended rectify plus a framing that tightens onto the focused surface. No
        // cross-fading anywhere. Time-driven with an ease-in power (slow start, fast finish): the visual
        // midpoint lands at 75% of the duration.
        // A Layout child straightens against its parent — that's the space it lives in — so the basis is
        // whichever surface up the chain actually carries the corner pin.
        var hasFocusBasis = SurfaceGeometry.FindCarrier(setup, _shownSurfaceId, outputId) != null;

        // A surface traced on a photo straightens *on that photo*, in place — the projector view stays put.
        var tracedImage = _editMode == EditMode.Straight ? TracedImageOf(setup, _shownSurfaceId) : null;

        var target = !hasFocusBasis || tracedImage != null || _editMode != EditMode.Straight ? 0f : 1f;

        if (target != _morphTarget)
        {
            _morphTarget = target;
            _morphFrom = _viewMorph;
            _morphProgress = 0f;
            CaptureTransitionStart(); // so the pan/zoom eases too, instead of snapping
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
        _boardCanvas.UpdateCanvas(out _);
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(canvasTop, ImGui.GetWindowPos() + ImGui.GetWindowSize(), true);

        // This output's space lives inside its card; the Board layer behind fades as the space comes in.
        SeedBoardPlacements(setup);
        if (tracedImage != null)
            EnterSpace(setup, SetupEntitySelection.EntityKind.ReferenceImage, tracedImage.Id, true);
        else
            EnterSpace(setup, SetupEntitySelection.EntityKind.Output, outputId, _editMode != EditMode.Board);

        DrawBoardLayer(setup, machineConfig, selection);

        if (_spaceBlend > 0.001f)
        {
            if (_spaceKind == SetupEntitySelection.EntityKind.ReferenceImage)
                DrawReferenceSpaceForShown(setup, selection, straighten: tracedImage != null);
            else
                DrawOutputCanvas(setup, output, outputId, selection); // Original / Straight, morphed by _viewMorph
        }

        ResolvePicking(setup, selection);
        dl.PopClipRect();
    }

    /// <summary>
    /// Points the space at its entity's card — the projection's origin and scale — and drives the Board ↔ space
    /// blend toward <paramref name="inSpace"/>. Entering remembers the Board camera; leaving eases back to it.
    /// </summary>
    private void EnterSpace(Setup setup, SetupEntitySelection.EntityKind kind, Guid id, bool inSpace)
    {
        // Leaving keeps the fading space's identity and origin: it is still the one being drawn out.
        if (inSpace)
        {
            // Straight from one space into another (a surface on a different photo): no fold, but the camera
            // still travels — a transition at full blend.
            if ((_spaceKind != kind || _spaceId != id) && _spaceTarget >= 1f && _spaceBlend >= 1f)
            {
                _spaceFrom = 1f;
                _spaceProgress = 0f;
                CaptureTransitionStart();
            }

            _spaceKind = kind;
            _spaceId = id;
        }

        if (TryGetBoardBounds(setup, _spaceKind, _spaceId, out var min, out var max))
        {
            _projection.Origin = new Vector2(min.X, max.Y);
            _projection.PixelsPerMeter = BoardPixelSize(setup, _spaceKind, _spaceId).X / MathF.Max(max.X - min.X, 0.0001f);
        }

        _spaceOrigin = _projection.Origin;
        _spacePixelsPerMeter = _projection.PixelsPerMeter;

        var target = inSpace ? 1f : 0f;
        if (target != _spaceTarget)
        {
            _spaceTarget = target;
            _spaceFrom = _spaceBlend;
            _spaceProgress = 0f;
            CaptureTransitionStart();
            if (inSpace)
                _boardScopeBeforeSpace = _morphFromScope;
        }

        if (_spaceProgress >= 1f)
            return;

        var dt = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.1f);
        _spaceProgress = MathF.Min(1f, _spaceProgress + dt / _morphDuration);
        var eased = MathF.Pow(_spaceProgress, _morphEaseExponent);
        _spaceBlend = _spaceProgress >= 1f ? _spaceTarget : _spaceFrom + (_spaceTarget - _spaceFrom) * eased;

        // Back to the Board: the camera returns to where it was before the space was entered.
        if (!inSpace)
        {
            _boardCanvas.SetScopeInstant(new CanvasScope
                                             {
                                                 Scale = Vector2.Lerp(_morphFromScope.Scale, _boardScopeBeforeSpace.Scale, eased),
                                                 Scroll = Vector2.Lerp(_morphFromScope.Scroll, _boardScopeBeforeSpace.Scroll, eased),
                                             });
        }
    }

    /// <summary>Whether a Board card is drawn by the current space instead of the Board layer: the space's own
    /// entity, and for an output the surfaces mapped to it (their quads fly into place).</summary>
    private bool IsDrawnBySpace(Setup setup, SetupEntitySelection.EntityKind kind, Guid id)
    {
        if (_spaceBlend <= 0.001f)
            return false;

        if (kind == _spaceKind && id == _spaceId)
            return true;

        return _spaceKind == SetupEntitySelection.EntityKind.Output
               && kind == SetupEntitySelection.EntityKind.Surface
               && SurfaceGeometry.FindCarrier(setup, id, _spaceId) != null;
    }

    /// <summary>A surface's Board card as a view-space quad (TL, TR, BR, BL) — where its mapped quad flies from.</summary>
    private bool TryGetBoardQuadInView(Setup setup, Guid surfaceId, Vector2 viewMin, Span<Vector2> quad)
    {
        if (!TryGetBoardBounds(setup, SetupEntitySelection.EntityKind.Surface, surfaceId, out var min, out var max))
            return false;

        quad[0] = _projection.BoardToCanvas(new Vector2(min.X, max.Y)) - viewMin;
        quad[1] = _projection.BoardToCanvas(max) - viewMin;
        quad[2] = _projection.BoardToCanvas(new Vector2(max.X, min.Y)) - viewMin;
        quad[3] = _projection.BoardToCanvas(min) - viewMin;
        return true;
    }

    // The output canvas carries a global rectify transform R (output px → view space): identity at _viewMorph
    // 0 (Original), and by 1 (Straight) it maps the focused surface's quad onto its own axis-aligned bounding
    // box, carrying the whole composite and every surface with it. From 1 to 2 (Content) R holds and the
    // framing tightens onto that surface. Blending R and the framing is what makes the views morph.
    private void DrawOutputCanvas(Setup setup, OutputDefinition output, Guid outputId, SetupEntitySelection? selection)
    {
        var canvasSize = new Vector2(Math.Max(1, output.CanvasResolution.Width),
                                     Math.Max(1, output.CanvasResolution.Height));

        var straighten = Math.Clamp(_viewMorph, 0f, 1f);

        // Pulled before the transform so the content aspect below reads a live evaluation context.
        var composite = OutputManager.RenderOutput(outputId);

        // Rectify basis = the focused surface. Freeze it while it is the one being dragged, so the transform
        // doesn't chase its own edit; otherwise the live quad keeps R settled and current.
        // Whose space the focus lives in: itself, or the parent when a Layout child is selected.
        var focusCarrier = SurfaceGeometry.FindCarrier(setup, _shownSurfaceId, outputId);
        var focusCarrierId = focusCarrier?.Id ?? Guid.Empty;

        var basis = _viewMorph > 0.0001f ? focusCarrier : null;
        var basisMapping = basis?.FindMapping(outputId);
        var basisId = basis?.Id ?? Guid.Empty;

        var rToView = Homography.Identity;
        var rToOutput = Homography.Identity;
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
            if (_gesture.EditsSurface(basisId) && _gesture.Snapshot is { } frozen)
            {
                framingFrozen = true;
                if (frozen.TryGetQuad(outputId, out var frozenQuad) && frozenQuad.Length >= 4)
                    basisQuad = frozenQuad;

                // The anchor is re-derived on every crop, and R is built from it — leaving it live feeds that
                // correction straight back into the drag, which runs away when the dragged edge is the anchor's.
                basisSize = frozen.Size;
                anchor = frozen.Anchor;
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
            Bounds(stageTarget, out _straightRectMin, out _straightRectMax);

            var interp = _interpQuad;
            for (var c = 0; c < 4; c++)
                interp[c] = Vector2.Lerp(basisQuad[c], stageTarget[c], straighten);

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
                var surround = MathF.Max(focusSpan.X, focusSpan.Y) * _straightSurroundFactor;
                var m = new Vector2(surround);
                var framedMin = focusMin - m;
                var framedMax = focusMax + m;

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

                // Straight is the surface's own space: as the view rectifies it slides from the output's card
                // onto the surface's card, at the surface's true scale — so the wall is looked at head-on
                // where it stands on the Board, not inside the projector's frame.
            }
            else
            {
                rToView = Homography.Identity;
                rToOutput = Homography.Identity;
            }
        }

        if (basisMapping == null)
            _frozenFramedMin = null; // left the rectified context — next entry re-derives the framing

        var rectifying = _viewMorph > 0.0001f;

        // The camera heads for where this view will settle, not for the framing of the frame in flight —
        // easing toward a moving target lags behind the geometry and reads as content sliding into place.
        GetSettledBoardRect(setup, basisId, basis, basisMapping, canvasSize, viewSize, out var settledMin, out var settledMax);
        FitToBoardRect(settledMin, settledMax, EditMode.Output, outputId, keepScope: _framingWasFrozen && !framingFrozen);
        _framingWasFrozen = framingFrozen;

        _probeSurfaceCentre = _projection.CanvasToScreen((_straightRectMin + _straightRectMax) * 0.5f - viewMin);
        SampleTransitionMetrics();

        var dl = ImGui.GetWindowDrawList();
        var frameMin = _projection.CanvasToScreen(Vector2.Zero);
        var frameMax = _projection.CanvasToScreen(viewSize);

        // The projector canvas boundary, carried through R like everything else. Drawing it as an axis-aligned
        // rect around the framing window instead would read as a false edge once the view straightens — the
        // framing is just where we render, not where the projector's coverage actually ends.
        var canvasOutline = _canvasOutline;
        canvasOutline[0] = _projection.CanvasToScreen(rToView.TransformPoint(Vector2.Zero) - viewMin);
        canvasOutline[1] = _projection.CanvasToScreen(rToView.TransformPoint(new Vector2(canvasSize.X, 0)) - viewMin);
        canvasOutline[2] = _projection.CanvasToScreen(rToView.TransformPoint(canvasSize) - viewMin);
        canvasOutline[3] = _projection.CanvasToScreen(rToView.TransformPoint(new Vector2(0, canvasSize.Y)) - viewMin);

        dl.AddQuadFilled(canvasOutline[0], canvasOutline[1], canvasOutline[2], canvasOutline[3], UiColors.BackgroundFull.Fade(0.4f));

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
                var dest = _warpDestQuad;
                dest[0] = (rToView.TransformPoint(new Vector2(0, 0)) - viewMin) * renderScale;
                dest[1] = (rToView.TransformPoint(new Vector2(w, 0)) - viewMin) * renderScale;
                dest[2] = (rToView.TransformPoint(new Vector2(w, h)) - viewMin) * renderScale;
                dest[3] = (rToView.TransformPoint(new Vector2(0, h)) - viewMin) * renderScale;

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
        // the moving transform) and the space is fully entered.
        var editable = _morphProgress >= 1f && _spaceBlend >= 1f;
        var handleFade = 1f;

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
            var mappingData = surface.FindMapping(outputId);

            // No pin of its own on this output: a Layout child's quad is derived from its carrier's, so it
            // follows the parent automatically. (A region *with* a mapping took the branch above and is edited
            // like any pinned surface — see Surface.OutputMappings.)
            if (mappingData == null)
            {
                if (surface.Kind != Surface.SurfaceKinds.Layout || surface.ParentId == Guid.Empty)
                    continue;

                // Regions ride their parent, which is still flying in — they appear once the space has settled.
                if (_spaceBlend < 1f)
                    continue;

                // The pin lives on some ancestor (possibly several levels up); edits live in the immediate
                // parent's space, so both are needed.
                var carrier = SurfaceGeometry.FindCarrier(setup, surface.Id, outputId);
                var carrierMapping = carrier?.FindMapping(outputId);
                var immediateParent = setup.FindSurface(surface.ParentId);
                if (carrier == null || carrierMapping == null || immediateParent == null
                    || !SurfaceGeometry.TryGetChildQuad(setup, carrier, surface, carrierMapping, _childQuadBuffer))
                    continue;

                DrawChildRegion(setup, selection, dl, rToView, rToOutput, viewMin, carrier, carrierMapping, immediateParent, surface, editable, handleFade);
                continue;
            }

            // The quad in view space: R applied, then offset into the framed region. One buffer for every
            // surface — nothing below keeps it past this iteration.
            var viewQuad = _viewQuad;
            for (var c = 0; c < 4; c++)
                viewQuad[c] = rToView.TransformPoint(mappingData.Quad[c]) - viewMin;

            // While the space comes in, the quad flies from the surface's Board card to its mapped place.
            if (_spaceBlend < 1f && TryGetBoardQuadInView(setup, surface.Id, viewMin, _boardFlyQuad))
            {
                for (var c = 0; c < 4; c++)
                    viewQuad[c] = Vector2.Lerp(_boardFlyQuad[c], viewQuad[c], _spaceBlend);
            }

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
            var style = CornerPinHandles.Style.ForSurface(surface.Name, editable, isSelected, emphasis, hue: SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface));
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
                                 SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface).Fade(surfacePulse * 0.15f * handleFade));

            style.EdgeColor = PulseColor(style.EdgeColor, surfacePulse);

            var handleActive = _gesture.EditsSurface(surface.Id);
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
                if (_gesture.Is(GestureKinds.SurfaceMove, surface.Id))
                {
                    movePhase = ImGui.IsMouseDown(ImGuiMouseButton.Left)
                                    ? CanvasPointHandle.DragPhase.Dragging
                                    : CanvasPointHandle.DragPhase.Completed;
                }
                else if (!_gesture.IsLive && _labelGrabScreen != null
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
                    movePhase = CanvasPointHandle.DragPhase.Started;
                }

                if (movePhase == CanvasPointHandle.DragPhase.Started)
                {
                    BeginGesture(setup, GestureKinds.SurfaceMove, "Move surface", surface.Id, surface, _projection.ScreenToCanvas(ImGui.GetMousePos()));
                }
                else if (movePhase == CanvasPointHandle.DragPhase.Dragging
                         && _gesture.Snapshot is { } moveSnapshot && moveSnapshot.TryGetQuad(outputId, out var preMoveQuad))
                {
                    // Rigid in view space; carried through R per corner, so in a rectified view the quad
                    // warps exactly as if each corner had been dragged by the same screen offset.
                    var moveDelta = _projection.ScreenToCanvas(ImGui.GetMousePos()) - _gesture.GrabPoint;
                    for (var c = 0; c < 4; c++)
                        mappingData.Quad[c] = rToOutput.TransformPoint(rToView.TransformPoint(preMoveQuad[c]) + moveDelta);
                }
                else if (movePhase == CanvasPointHandle.DragPhase.Completed)
                {
                    EndGesture(setup);
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
                    HandleEdgeDrag(edgePhase, setup, surface, mappingData, edge, edgePos, rToOutput, viewMin);
            }

            ImGui.PopID();

            // Under isolate the other frames' labels recede further, so the focused one clearly owns the canvas.
            var labelEmphasis = lockedByIsolate ? emphasis * 0.4f : emphasis;
            DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, labelQuad, surface.Id, surface.Name, isSelected, labelEmphasis, surfacePulse);
        }

        // Marquee over corner handles — plain output view only for now, and never while another canvas
        // drag is live (label moves and slice/annotation drags are manual, so the fence can't see them
        // through IsAnyItemActive alone).
        if (_editMode == EditMode.Output && editable && !_gesture.IsLive && !ImGui.IsAnyItemActive())
        {
            UpdateCornerFence();
        }
        else
        {
            _fence.Reset();
        }

        if (basis != null && basisMapping != null
            && SurfaceGeometry.TryGetSurfaceToOutput(basis, basisMapping, out var basisToOutput)
            && SurfaceGeometry.TryGetOutputToSurface(basis, basisMapping, out var outputToBasis))
        {
            DrawAnnotations(dl, basis, Homography.Multiply(rToView, basisToOutput), Homography.Multiply(outputToBasis, rToOutput),
                            viewMin, editable, handleFade * straighten);
        }

        // The reference points on the plain projector canvas, where they can be walked onto the wall.
        var pinMapping = focusCarrier?.FindMapping(outputId);
        if (_editMode == EditMode.Output && !rectifying && focusCarrier != null && pinMapping != null && pinMapping.Quad.Length >= 4
            && SetupActions.CountPoints(focusCarrier) > 0)
        {
            DrawReferencePointPins(setup, dl, focusCarrier, pinMapping, outputId, canvasSize, editable, handleFade);
        }
    }

    /// <summary>
    /// The focused surface's reference points as handles on the projector canvas. An idle point rides the pin
    /// (it shows where the photo's feature currently lands); dragging it activates it — its target is where it
    /// was dropped, in output pixels, and it never moves again on its own. The pin is re-solved from the
    /// activated targets alone: exactly as free as they allow (one shifts, two turn and scale, three shear,
    /// four keystone; beyond four the solve averages and the header reports the miss). Double-click resets a
    /// point to idle. One undo step per drag or reset.
    /// </summary>
    private void DrawReferencePointPins(Setup setup, ImDrawListPtr dl, Surface surface, Surface.OutputMapping mapping, Guid outputId,
                                        Vector2 canvasSize, bool editable, float fade)
    {
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, out var surfaceToOutput))
            return;

        // The discs are always on the canvas — they are what says which feature a point marks; the toggle
        // only decides whether they also go to the wall.
        DrawCanvasPhotoDiscs(setup, dl, surface, mapping, surfaceToOutput, canvasSize, fade);

        var green = SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface);
        var idleStyle = CanvasPointHandle.Style.Default(UiColors.ForegroundFull.Fade(0.6f * fade), CanvasPointHandle.Shape.Circle, editable);
        idleStyle.OutlineColor = UiColors.ForegroundFull.Fade(0.4f * fade);
        idleStyle.Radius = 6;
        var activeStyle = idleStyle;
        activeStyle.Color = UiColors.ForegroundFull.Fade(fade);
        activeStyle.OutlineColor = green.Fade(fade);
        activeStyle.Radius = 7;

        var ordinal = 0;
        for (var i = 0; i < surface.Annotations.Count; i++)
        {
            var point = surface.Annotations[i];
            if (!point.IsPoint)
                continue;

            ordinal++;
            var isActivated = mapping.PointTargets.TryGetValue(point.Id, out var px);
            if (!isActivated)
                px = surfaceToOutput.TransformPoint(point.P1);

            ImGui.PushID(i);
            var phase = CanvasPointHandle.Draw(ref px, _projection, isActivated ? activeStyle : idleStyle);
            var hovered = ImGui.IsItemHovered();
            ImGui.PopID();

            if (editable && hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                // Back to idle: the pin keeps its shape, the point just stops constraining it.
                if (isActivated)
                    SetupActions.RunUndoable("Reset reference point", setup, () => mapping.PointTargets.Remove(point.Id));

                CancelGesture(); // the press that became this double-click must not also commit a drag
            }
            else if (phase == CanvasPointHandle.DragPhase.Started)
            {
                BeginGesture(setup, GestureKinds.AimPoint, "Aim reference point", surface.Id);
            }
            else if (phase == CanvasPointHandle.DragPhase.Dragging && _gesture.Is(GestureKinds.AimPoint, surface.Id))
            {
                mapping.PointTargets[point.Id] = px;
                SolvePinFromTargets(surface, mapping);
            }
            else if (phase == CanvasPointHandle.DragPhase.Completed)
            {
                EndGesture(setup);
            }

            if (hovered || phase != CanvasPointHandle.DragPhase.None)
                OutputManager.EmphasizeAnnotation(surface.Id, i);

            var screen = _projection.CanvasToScreen(px);
            var markColor = isActivated ? green.Fade(0.9f * fade) : UiColors.ForegroundFull.Fade(0.5f * fade);
            CanvasDraw.Crosshair(dl, screen, markColor, 9f, 1f);
            DrawPointLabel(dl, screen, string.IsNullOrEmpty(point.Name) ? $"P{ordinal}" : point.Name, markColor);
        }
    }

    /// <summary>
    /// The photo discs as the projector shows them, but on the canvas and for every point — including those
    /// outside the projector's frame, which is exactly where an idle point tends to be: the disc says which
    /// feature it marks while it is dragged into the frame. The photo is warped through the pin once, then
    /// each disc is a round cut-out of that.
    /// </summary>
    private void DrawCanvasPhotoDiscs(Setup setup, ImDrawListPtr dl, Surface surface, Surface.OutputMapping mapping,
                                      in Homography surfaceToOutput, Vector2 canvasSize, float fade)
    {
        if (!TryGetTracedFragment(setup, surface, out _, out var photo, out var uvMin, out var uvMax))
            return;

        Bounds(mapping.Quad, out var bboxMin, out var bboxMax);
        var bboxSize = Vector2.Max(bboxMax - bboxMin, new Vector2(1f));
        var scale = MathF.Min(1f, 2048f / MathF.Max(bboxSize.X, bboxSize.Y));
        for (var c = 0; c < 4; c++)
            _canvasDiscQuad[c] = (mapping.Quad[c] - bboxMin) * scale;

        var size = new T3.Core.DataTypes.Vector.Int2(Math.Max(1, (int)(bboxSize.X * scale)), Math.Max(1, (int)(bboxSize.Y * scale)));
        var warped = OutputManager.RenderWarpedTexture(photo, _canvasDiscQuad, size, _canvasDiscKey, new Vector4(uvMin.X, uvMin.Y, uvMax.X, uvMax.Y));
        var srv = warped is { IsDisposed: false } ? SrvManager.GetSrvForTexture(warped) : null;
        if (srv is not { IsDisposed: false })
            return;

        var radius = canvasSize.Y * UserSettings.Config.OutputSetupPhotoDiscRadius;
        var tint = UiColors.ForegroundFull.Fade(fade);
        foreach (var point in surface.Annotations)
        {
            if (!point.IsPoint)
                continue;

            var centre = surfaceToOutput.TransformPoint(point.P1);
            var min = centre - new Vector2(radius);
            var max = centre + new Vector2(radius);
            var uv0 = (min - bboxMin) / bboxSize;
            var uv1 = (max - bboxMin) / bboxSize;
            var screenMin = _projection.CanvasToScreen(min);
            var screenMax = _projection.CanvasToScreen(max);
            dl.AddImageRounded(srv.NativePointer, screenMin, screenMax, uv0, uv1, tint, (screenMax.X - screenMin.X) * 0.5f, ImDrawFlags.RoundCornersAll);
            dl.AddCircle((screenMin + screenMax) * 0.5f, (screenMax.X - screenMin.X) * 0.5f, UiColors.BackgroundFull.Fade(0.5f * fade), 0, 1f);
        }
    }

    /// <summary>
    /// Re-solves the pin so every activated point projects to its target. Up to three targets the solve is
    /// incremental — the transform taking the current projections to the targets, applied to the pin; from
    /// four on it is the (least-squares) homography from surface metres straight to the targets.
    /// </summary>
    private void SolvePinFromTargets(Surface surface, Surface.OutputMapping mapping)
    {
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, out var surfaceToOutput))
            return;

        _pinFrom.Clear();
        _pinTargets.Clear();
        _pinSurfacePositions.Clear();
        foreach (var point in surface.Annotations)
        {
            if (!point.IsPoint || !mapping.PointTargets.TryGetValue(point.Id, out var target))
                continue;

            _pinFrom.Add(surfaceToOutput.TransformPoint(point.P1));
            _pinTargets.Add(target);
            _pinSurfacePositions.Add(point.P1);
        }

        _pinResidualPx = 0;
        if (_pinTargets.Count == 0)
            return;

        Span<Vector2> quad = stackalloc Vector2[4];
        if (_pinTargets.Count >= 4)
        {
            if (!Homography.TryComputeLeastSquares(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinSurfacePositions),
                                                   System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinTargets), out var surfaceToTargets))
                return;

            var rect = SurfaceGeometry.LocalRect(surface);
            for (var c = 0; c < 4; c++)
            {
                quad[c] = surfaceToTargets.TransformPoint(rect[c]);
                if (!float.IsFinite(quad[c].X) || !float.IsFinite(quad[c].Y))
                    return;
            }

            for (var i = 0; i < _pinTargets.Count; i++)
                _pinResidualPx = MathF.Max(_pinResidualPx, (surfaceToTargets.TransformPoint(_pinSurfacePositions[i]) - _pinTargets[i]).Length());
        }
        else
        {
            mapping.Quad.AsSpan(0, 4).CopyTo(quad);
            if (!PointPinSolver.TrySolve(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinFrom),
                                         System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pinTargets), quad, out _))
                return;
        }

        for (var c = 0; c < 4; c++)
            mapping.Quad[c] = quad[c];
    }

    /// <summary>Whether the pointer is over the canvas area this view draws into (not the strip below it).</summary>
    private bool IsMouseOverCanvas()
    {
        return ImGui.IsMouseHoveringRect(_boardCanvas.WindowPos, _boardCanvas.WindowPos + _boardCanvas.WindowSize);
    }

    private void UpdateCornerFence()
    {
        // Same rule as the Board fence: a press on the outliner strip below is not a press on this canvas.
        if (_fence.State == SelectionFence.States.Inactive && !IsMouseOverCanvas())
            return;

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

    // Fold metric, read through the debug bridge (getLogTail, "[fold] metrics"): the rectified surface's centre
    // on screen, sampled per transition frame. A good fold moves it in a straight line — reported as mean
    // distance from the window centre, path length over the chord (1 = straight) and the largest deviation
    // from the chord. Cheap (one Vector2 per frame while a fold runs), so it stays in.
    private Vector2 _probeSurfaceCentre;
    private readonly List<Vector2> _probeCentreSamples = [];

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
    /// An edge drag crops the surface's rectangle — moving that edge while the opposite one stays put, the
    /// content's pixels staying where they are on the wall. Ctrl stretches instead: same physical rectangle,
    /// different area on the projector, content re-fitted. The handle is dragged in view space, so it's carried
    /// back through R into projector pixels and then into the surface's own space, where both are a plain
    /// rect edit. The mode is read at the press and held for the drag.
    /// </summary>
    private void HandleEdgeDrag(CanvasPointHandle.DragPhase phase, Setup setup, Surface surface, Surface.OutputMapping mapping,
                                int edge, Vector2 viewPos, Homography rToOutput, Vector2 viewMin)
    {
        RunGesture(phase, setup, GestureKinds.SurfaceResize, _edgeStretch ? "Stretch surface" : "Crop surface", surface,
                   onStarted: () =>
                              {
                                  _edgeStretch = ImGui.GetIO().KeyCtrl;
                                  if (!_edgeStretch)
                                      BeginContentEdit(setup, surface);
                              },
                   onDragging: () =>
                               {
                                   // Re-base to the pre-drag rectangle first: the crop rewrites the surface's own frame, so
                                   // an incremental edit would compound frame over frame. From the snapshot the cursor maps to
                                   // one absolute edge position, stable however long the drag runs.
                                   _gesture.Snapshot!.Value.Restore(surface);
                                   if (!SurfaceGeometry.TryGetOutputToSurface(surface, mapping, out var outputToSurface))
                                       return;

                                   SurfaceGeometry.LocalBounds(surface, out var oldMin, out var oldMax);
                                   var surfacePos = outputToSurface.TransformPoint(rToOutput.TransformPoint(viewPos + viewMin));
                                   SurfaceGeometry.DragEdge(surface, edge, surfacePos, _edgeStretch);
                                   SurfaceGeometry.LocalBounds(surface, out var newMin, out var newMax);
                                   ApplyCropHandling(setup, oldMin, oldMax, newMin, newMax);
                               });
    }

    // Whether the live edge drag stretches (Ctrl at the press) rather than crops — held for the drag.
    private bool _edgeStretch;

    /// <summary>A corner drag (the grabbed surface, plus any with selected corners riding along) as one gesture.</summary>
    private void HandleDrag(CanvasPointHandle.DragPhase phase, Setup setup, Guid surfaceId, Guid outputId, Vector2[] liveQuad)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                BeginGesture(setup, GestureKinds.CornerPin, "Adjust corner pin", surfaceId, setup.FindSurface(surfaceId));
                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_gesture.Is(GestureKinds.CornerPin, surfaceId))
                    EndGesture(setup);

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

            var mapping = setup.FindSurface(target.EntityId)?.FindMapping(outputId);
            if (mapping != null && target.Index >= 0 && target.Index < mapping.Quad.Length)
                mapping.Quad[target.Index] += delta;
        }
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
    // Per-frame quad scratch: the rectify interpolation, the projector outline, the warp target, the surface in view.
    private readonly Vector2[] _interpQuad = new Vector2[4];
    private readonly Vector2[] _canvasOutline = new Vector2[4];
    private readonly Vector2[] _warpDestQuad = new Vector2[4];
    private readonly Vector2[] _viewQuad = new Vector2[4];

    // View morph timing: eased in so it starts slowly and finishes quickly. The exponent solves 0.75^k = 0.5,
    // i.e. the visual midpoint is reached at 75% of the duration.
    private const float _morphDuration = 0.5f;
    private const float _morphEaseExponent = 2.41f;

    private readonly SpaceProjection _projection;
    private readonly Vector2[] _boardFlyQuad = new Vector2[4];
    private readonly EntityItem _entityItem;
    private EditMode _editMode = EditMode.Board; // the Board is the home view, so a fresh window opens on it

    /// <summary>Set by the window: the outliner strip is shown and hosts the toolbar (see <see cref="DeferHeader"/>).</summary>
    public bool HeaderHostedByStrip;

    private enum HeaderKinds { None, Modes, Return, Reference }

    private HeaderKinds _pendingHeaderKind;
    private Guid _pendingHeaderOutputId;
    private string _pendingHeaderTitle = string.Empty;
    private Guid _pendingHeaderImageId;
    private Guid _pendingHeaderSubjectId;

    // Calibrating a pin by its reference points: the projected photo discs, and the solve's scratch lists.
    private bool _projectPhoto;
    private float _pinResidualPx;
    private readonly List<Vector2> _pinTargets = [];
    private readonly List<Vector2> _pinFrom = [];
    private readonly List<Vector2> _pinSurfacePositions = [];
    private readonly Vector2[] _canvasDiscQuad = new Vector2[4];
    private static readonly Guid _canvasDiscKey = new("6a1f0c2e-7b3d-4e8f-9a0b-1c2d3e4f5a6b");
    private bool _isolate;
    private Guid _shownSurfaceId; // frame-scoped: what the caller passed to this Draw, never read across frames
    private (Guid, EditMode, Vector2) _fitKey;

    // View morph position: 0 = Original (projector space), 1 = Straight (focused surface rectified),
    // 2 = Content (framing tightened onto that surface). One continuous axis, animated.
    private float _viewMorph;
    private float _morphTarget;
    private float _morphFrom;
    private float _morphProgress = 1f; // 1 = settled (no animation running)

    // Camera at the moment a morph or a space transition started, and the board rect it showed, so the
    // framing eases from the user's view.
    private CanvasScope _morphFromScope;
    private Vector2 _morphFromMin, _morphFromMax;

    // Board ↔ space blend: 0 = the Board, 1 = the current space (an output's canvas, a source's texture),
    // eased like the view morph; the camera returns to the pre-space Board view on the way back.
    private float _spaceBlend;
    private float _spaceTarget;
    private float _spaceFrom;
    private float _spaceProgress = 1f;
    private CanvasScope _boardScopeBeforeSpace;
    private SetupEntitySelection.EntityKind _spaceKind;
    private Guid _spaceId;
    private Vector2 _spaceOrigin; // the space's card top-left and scale, as EnterSpace set them
    private float _spacePixelsPerMeter;
    private Vector2 _straightRectMin, _straightRectMax; // the rectified surface's rect in output px, per frame

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

    // The canvas sub-element plane: selected mapping-quad corners (SelectionTarget.Part == Corner) of the
    // shown output canvas. Deliberately separate from the entity selection — the two planes never mix.
    private readonly SelectionSet<SelectionTarget> _canvasSelection = new();
    private readonly SelectionFence _fence = new();
    private readonly List<(SelectionTarget Target, Vector2 ScreenPos)> _fenceCandidates = new();

    // Label chips collected this frame (id + screen rect) and the pick they resolve to — labels double as
    // each surface's click target, and overlapping ones cycle.
    private readonly CanvasItemPicker<SetupEntitySelection.EntityKind> _picker = new();

    // The held-grab handoff: a plain press on a surface/region label selects it (via the picker, which
    // cycles stacks); if the button is still down next frame, the move machinery starts from this position.
    private Vector2? _labelGrabScreen;

    // Patch gestures: the quad in view space (reused per patch) and the pre-drag quad a re-based edit starts from.
    private readonly Vector2[] _patchViewQuad = new Vector2[4];
    private readonly Vector2[] _patchOldQuad = new Vector2[4];
    private SetupEntitySelection.EntityKind _menuKind;
    private Guid _menuId;

    // Snap candidates in the parent's space, rebuilt per drag frame; reused so dragging doesn't allocate.
    private readonly List<float> _snapXs = [];
    private readonly List<float> _snapYs = [];
    private const string PickMenuId = "##canvasPickMenu";
    private const string BoardMenuId = "##boardMenu";

    // Scratch for a Layout child's derived quad; consumed before the next child reuses it.
    private readonly Vector2[] _childQuadBuffer = new Vector2[4];

    // The axis a nearly-straight region move locked to (0 none, 1 horizontal, 2 vertical), so the guide can be drawn.
    private int _childMoveAxis;
    private bool _framingWasFrozen;
}
