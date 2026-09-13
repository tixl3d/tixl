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
/// fold out of it — an output's canvas (corner-pin each surface's quad over the live composite, with the
/// Straight morph), a source's texture (lay out its slices), a projector's calibration. A space draws its
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
    private enum EditModes
    {
        Board,
        Straight,
        Output,
    }

    public SetupOutputView()
    {
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

        DrawFrame(setup, machineConfig, output, outputId, shownSurfaceId, selection);
    }

    /// <summary>
    /// The Board with no output focused — what the window shows while nothing else claims it. A shown surface
    /// traced on a photo can still take the Straight tab: it straightens on that photo, in place.
    /// </summary>
    public void DrawBoardStandalone(SetupEntitySelection? selection, Guid shownSurfaceId = default)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
            return;

        DrawFrame(setup, machineConfig, null, Guid.Empty, shownSurfaceId, selection);
    }

    /// <summary>
    /// One frame of the canvas: the header, the Board layer, and the space the mode asks for — an output's
    /// canvas (<paramref name="output"/> given) or the shown surface's photo — folding in or out over it.
    /// </summary>
    private void DrawFrame(Setup setup, MachineConfig machineConfig, OutputDefinition? output, Guid outputId, Guid shownSurfaceId,
                           SetupEntitySelection? selection)
    {
        _shownSurfaceId = shownSurfaceId;
        OpenedReferenceImageId = Guid.Empty;
        ResolveEditMode(setup, output, outputId);

        if (!DeferHeader(HeaderKinds.Modes, outputId))
            DrawHeader(setup, output, outputId);

        // A surface traced on a photo straightens *on that photo*, in place — the projector view stays put.
        var tracedImage = _editMode == EditModes.Straight ? TracedImageOf(setup, _shownSurfaceId) : null;

        if (output != null)
        {
            // Original (0) → Straight (1) is one continuous axis, not two modes. The composite is the content
            // texture already warped through the corner-pin, so both are the same pixels at different points of
            // one homography chain — a blended rectify plus a framing that tightens onto the focused surface. No
            // cross-fading anywhere. Time-driven with an ease-in power (slow start, fast finish): the visual
            // midpoint lands at 75% of the duration.
            // A Layout child straightens against its parent — that's the space it lives in — so the basis is
            // whichever surface up the chain actually carries the corner pin.
            var hasFocusBasis = SurfaceGeometry.FindMappingCarrier(setup, _shownSurfaceId, outputId) != null;
            var target = !hasFocusBasis || tracedImage != null || _editMode != EditModes.Straight ? 0f : 1f;
            if (_viewMorph.Retarget(target))
                CaptureTransitionStart(); // so the pan/zoom eases too, instead of snapping

            _viewMorph.Advance(FrameDeltaSec(), MorphDurationSec, MorphEaseExponent);
        }

        // Clip the canvas to the region below the toolbar: it draws straight to the window draw list, so
        // without this the pan/zoom transform lets content spill up over the header.
        var canvasTop = ImGui.GetCursorScreenPos();
        _boardCanvas.UpdateCanvas(out _);
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(canvasTop, ImGui.GetWindowPos() + ImGui.GetWindowSize(), true);

        // A space lives inside its entity's card; the Board layer behind fades as the space comes in.
        SeedBoardPlacements(setup);
        if (tracedImage != null)
            EnterSpace(setup, SetupEntityKinds.ReferenceImage, tracedImage.Id);
        else if (output != null && _editMode != EditModes.Board)
            EnterSpace(setup, SetupEntityKinds.Output, outputId);
        else
            LeaveSpace(setup); // whatever was open fades back into its card

        DrawBoardLayer(setup, machineConfig, selection);

        if (_spaceBlend.Value > 0.001f)
        {
            if (_spaceKind == SetupEntityKinds.ReferenceImage)
                DrawReferenceSpaceForShown(setup, selection, straighten: tracedImage != null);
            else if (output != null)
                DrawOutputCanvas(setup, output, outputId, selection); // Original / Straight, morphed by _viewMorph
        }

        ResolvePicking(setup, selection);
        dl.PopClipRect();
    }

    /// <summary>
    /// Corrects the mode against what this frame can actually show. Every writer (the header's tabs, a card's
    /// double-click, the debug bridge) writes the mode raw; this runs first each frame so a mode whose
    /// precondition lapsed — the selection no longer reaches a surface or an output — falls back instead of
    /// sticking on a tab that can't be clicked away.
    /// </summary>
    private void ResolveEditMode(Setup setup, OutputDefinition? output, Guid outputId)
    {
        var canStraight = SurfaceGeometry.FindMappingCarrier(setup, _shownSurfaceId, outputId) != null
                          || TracedImageOf(setup, _shownSurfaceId) != null;
        var canOutput = output != null;

        if (!canStraight && _editMode == EditModes.Straight)
        {
            _editMode = canOutput ? EditModes.Output : EditModes.Board;
        }
        else if (!canOutput && _editMode != EditModes.Straight)
        {
            _editMode = EditModes.Board;
        }
    }

    /// <summary>
    /// Points the space at its entity's card — the projection's origin and scale — and drives the Board ↔ space
    /// blend in. Entering remembers the Board camera, so <see cref="LeaveSpace"/> can ease back to it.
    /// </summary>
    private void EnterSpace(Setup setup, SetupEntityKinds kind, Guid id)
    {
        // Straight from one space into another (a surface on a different photo): no fold, but the camera
        // still travels — a transition at full blend.
        if ((_spaceKind != kind || _spaceId != id) && _spaceBlend.Target >= 1f && _spaceBlend.Value >= 1f)
        {
            _spaceBlend.From = 1f;
            _spaceBlend.Progress = 0f;
            CaptureTransitionStart();
        }

        _spaceKind = kind;
        _spaceId = id;
        PointProjectionAtSpace(setup);

        if (_spaceBlend.Retarget(1f))
        {
            CaptureTransitionStart();
            _boardScopeBeforeSpace = _morphFromScope;
        }

        AdvanceSpaceBlend();
    }

    /// <summary>Drives the blend back to the Board. The fading space keeps its identity and origin: it is still the one being drawn out.</summary>
    private void LeaveSpace(Setup setup)
    {
        PointProjectionAtSpace(setup);
        if (_spaceBlend.Retarget(0f))
            CaptureTransitionStart();

        AdvanceSpaceBlend();
    }

    private void PointProjectionAtSpace(Setup setup)
    {
        // The photo space's own eases (Photo ↔ Straight, and turning from one traced subject to another) only
        // advance while that space is drawn. Leaving it mid-flight — clicking an output while the photo is still
        // turning — would strand them below 1, and they gate the framing: every later frame would then re-set the
        // camera to a half-eased fit, which pins the view and takes pan and zoom down with it. Nothing is showing
        // them any more, so they land where they were heading.
        if (_spaceKind != SetupEntityKinds.ReferenceImage)
        {
            _referenceStraighten.Settle();
            _referenceSubjectEase.Settle();
        }

        if (TryGetBoardBounds(setup, _spaceKind, _spaceId, out var min, out var max))
        {
            _projection.Origin = new Vector2(min.X, max.Y);
            _projection.PixelsPerMeter = BoardPixelSize(setup, _spaceKind, _spaceId).X / MathF.Max(max.X - min.X, 0.0001f);
        }

        _spaceOrigin = _projection.Origin;
        _spacePixelsPerMeter = _projection.PixelsPerMeter;
    }

    /// <summary>One step of the Board ↔ space blend; on the way back the camera returns to where it was before the space was entered.</summary>
    private void AdvanceSpaceBlend()
    {
        if (_spaceBlend.IsSettled)
            return;

        _spaceBlend.Advance(FrameDeltaSec(), MorphDurationSec, MorphEaseExponent);

        if (_spaceBlend.Target < 0.5f)
        {
            var eased = MathF.Pow(_spaceBlend.Progress, MorphEaseExponent);
            _boardCanvas.SetScopeInstant(new CanvasScope
                                             {
                                                 Scale = Vector2.Lerp(_morphFromScope.Scale, _boardScopeBeforeSpace.Scale, eased),
                                                 Scroll = Vector2.Lerp(_morphFromScope.Scroll, _boardScopeBeforeSpace.Scroll, eased),
                                             });
        }
    }

    /// <summary>The frame's time step, capped so a stall (a load, a dropped window) can't skip a transition to its end.</summary>
    private static float FrameDeltaSec()
    {
        return Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.1f);
    }

    /// <summary>Whether a Board card is drawn by the current space instead of the Board layer: the space's own
    /// entity, and for an output the surfaces mapped to it (their quads fly into place).</summary>
    private bool IsDrawnBySpace(Setup setup, SetupEntityKinds kind, Guid id)
    {
        if (_spaceBlend.Value <= 0.001f)
            return false;

        if (kind == _spaceKind && id == _spaceId)
            return true;

        return _spaceKind == SetupEntityKinds.Output
               && kind == SetupEntityKinds.Surface
               && SurfaceGeometry.FindMappingCarrier(setup, id, _spaceId) != null;
    }

    /// <summary>A surface's Board card as a view-space quad (TL, TR, BR, BL) — where its mapped quad flies from.</summary>
    private bool TryGetBoardQuadInView(Setup setup, Guid surfaceId, Vector2 viewMin, Span<Vector2> quad)
    {
        if (!TryGetBoardBounds(setup, SetupEntityKinds.Surface, surfaceId, out var min, out var max))
            return false;

        quad[0] = _projection.BoardToCanvas(new Vector2(min.X, max.Y)) - viewMin;
        quad[1] = _projection.BoardToCanvas(max) - viewMin;
        quad[2] = _projection.BoardToCanvas(new Vector2(max.X, min.Y)) - viewMin;
        quad[3] = _projection.BoardToCanvas(min) - viewMin;
        return true;
    }

    // The output canvas carries a global rectify transform R (output px → view space): identity at _viewMorph
    // 0 (Original), and by 1 (Straight) it maps the focused surface's quad onto its own axis-aligned bounding
    // box, carrying the whole composite and every surface with it. Blending R and the framing is what makes
    // the views morph.
    private void DrawOutputCanvas(Setup setup, OutputDefinition output, Guid outputId, SetupEntitySelection? selection)
    {
        // This canvas works in the output's pixels — framing, grids, snap thresholds and handles are all tuned
        // to that. Mappings and patches are *stored* as ratios of the canvas, so they are scaled into these
        // pixels on the way in and divided back out on the way to the model.
        var canvasSize = output.CanvasSize;

        var straighten = _viewMorph.Value;

        // Pulled before the transform so the content aspect below reads a live evaluation context.
        var composite = OutputCompositor.RenderOutput(outputId);

        // Rectify basis = the focused surface. Freeze it while it is the one being dragged, so the transform
        // doesn't chase its own edit; otherwise the live quad keeps R settled and current.
        // Whose space the focus lives in: itself, or the parent when a Layout child is selected.
        var focusCarrier = SurfaceGeometry.FindMappingCarrier(setup, _shownSurfaceId, outputId);
        var focusCarrierId = focusCarrier?.Id ?? Guid.Empty;

        var rectifying = straighten > 0.0001f;
        var basis = rectifying ? focusCarrier : null;
        var basisMapping = basis?.FindMapping(outputId);
        var basisId = basis?.Id ?? Guid.Empty;

        // The framing is held still for the duration of an edit, so the size it is derived from only catches
        // up on release. Refitting to that is a jump the user never asked for, so the frame the freeze lifts
        // adopts the new framing without moving the view.
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

            // Straight is the surface's own space: as the view rectifies it slides from the output's card onto
            // the surface's card, at the surface's true scale — so the wall is looked at head-on where it
            // stands on the Board, not inside the projector's frame.
            _framing.Compute(basisQuad, basisSize, anchor, basis.PixelsPerMeter, canvasSize, straighten,
                             viewSettled: _viewMorph.IsSettled, basisSettled: _basisEase.IsSettled, ref _hold);
        }
        else
        {
            _framing.Reset(canvasSize);
        }

        // Left the rectified context — the next entry re-derives the framing.
        if (basisMapping == null)
            _hold.FrozenMin = null;

        var rectifiedToView = _framing.RectifiedToView;
        var rectifiedToOutput = _framing.RectifiedToOutput;
        var viewMin = _framing.ViewMin;
        var viewSize = _framing.ViewSize;

        // The camera heads for where this view will settle, not for the framing of the frame in flight —
        // easing toward a moving target lags behind the geometry and reads as content sliding into place.
        GetSettledBoardRect(basis, basisMapping, canvasSize, out var settledMin, out var settledMax);
        FitToBoardRect(settledMin, settledMax, EditModes.Output, outputId, keepScope: _hold.WasFrozen && !framingFrozen);
        _hold.WasFrozen = framingFrozen;

        var dl = ImGui.GetWindowDrawList();
        var frameMin = _projection.CanvasToScreen(Vector2.Zero);
        var frameMax = _projection.CanvasToScreen(viewSize);

        // The projector canvas boundary, carried through R like everything else. Drawing it as an axis-aligned
        // rect around the framing window instead would read as a false edge once the view straightens — the
        // framing is just where we render, not where the projector's coverage actually ends.
        var canvasOutline = _canvasOutline;
        canvasOutline[0] = _projection.CanvasToScreen(rectifiedToView.TransformPoint(Vector2.Zero) - viewMin);
        canvasOutline[1] = _projection.CanvasToScreen(rectifiedToView.TransformPoint(new Vector2(canvasSize.X, 0)) - viewMin);
        canvasOutline[2] = _projection.CanvasToScreen(rectifiedToView.TransformPoint(canvasSize) - viewMin);
        canvasOutline[3] = _projection.CanvasToScreen(rectifiedToView.TransformPoint(new Vector2(0, canvasSize.Y)) - viewMin);

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
                dest[0] = (rectifiedToView.TransformPoint(new Vector2(0, 0)) - viewMin) * renderScale;
                dest[1] = (rectifiedToView.TransformPoint(new Vector2(w, 0)) - viewMin) * renderScale;
                dest[2] = (rectifiedToView.TransformPoint(new Vector2(w, h)) - viewMin) * renderScale;
                dest[3] = (rectifiedToView.TransformPoint(new Vector2(0, h)) - viewMin) * renderScale;

                var warped = OutputCompositor.RenderWarpedTexture(composite, dest, rtSize);
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

        // On this canvas the output *is* the frame — the Board's card stands in for it everywhere else, which is
        // why it had no pick target here and its context menu never opened. Registered as a background target,
        // so a patch, surface or label under the cursor still wins.
        if (_editMode == EditModes.Output && !_isolatesFocusedSurface)
        {
            CanvasDraw.Bounds(canvasOutline, out var outlineMin, out var outlineMax);
            _picker.AddTarget(SetupEntityKinds.Output, outputId, outlineMin, outlineMax, isBackground: true);
        }

        // Corner-pin handles are editable only when the morph has settled (so a mid-animation drag can't fight
        // the moving transform) and the space is fully entered.
        var editable = _viewMorph.IsSettled && _spaceBlend.Value >= 1f;
        var handleFade = 1f;

        // Patches sit under the surfaces in the composite, so their frames go first and the surfaces draw over them.
        DrawPatches(setup, output, selection, dl, rectifiedToView, rectifiedToOutput, viewMin, canvasSize, editable, handleFade, hasContent);

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            var mappingData = surface.FindMapping(outputId);

            // No pin of its own on this output: a Layout child's quad is derived from its carrier's, so it
            // follows the parent automatically. (A region *with* a mapping took the branch above and is edited
            // like any pinned surface — see Surface.OutputMappings.)
            if (mappingData == null)
            {
                if (surface.Kind != Surface.Kinds.Layout || surface.ParentId == Guid.Empty)
                    continue;

                // Regions ride their parent, which is still flying in — they appear once the space has settled.
                if (_spaceBlend.Value < 1f)
                    continue;

                // The pin lives on some ancestor (possibly several levels up); edits live in the immediate
                // parent's space, so both are needed.
                var carrier = SurfaceGeometry.FindMappingCarrier(setup, surface.Id, outputId);
                var carrierMapping = carrier?.FindMapping(outputId);
                var immediateParent = setup.FindSurface(surface.ParentId);
                if (carrier == null || carrierMapping == null || immediateParent == null)
                    continue;

                DrawRegionOnOutput(setup, selection, dl, rectifiedToView, rectifiedToOutput, viewMin, carrier, carrierMapping, immediateParent, surface, editable, handleFade);
                continue;
            }

            DrawMappedSurface(setup, selection, dl, surface, mappingData, outputId, canvasSize, _framing, focusCarrierId, editable, handleFade, hasContent);
        }

        // Marquee over corner handles — plain output view only for now, and never while another canvas
        // drag is live (label moves and slice/annotation drags are manual, so the fence can't see them
        // through IsAnyItemActive alone).
        if (_editMode == EditModes.Output && editable && !_gesture.IsLive && !ImGui.IsAnyItemActive())
        {
            UpdateCornerFence();
        }
        else
        {
            _fence.Reset();
        }

        if (basis != null && basisMapping != null
            && SurfaceGeometry.TryGetSurfaceToOutput(basis, basisMapping, SurfaceGeometry.CanvasSizeOf(setup, basisMapping.OutputId), out var basisToOutput)
            && SurfaceGeometry.TryGetOutputToSurface(basis, basisMapping, SurfaceGeometry.CanvasSizeOf(setup, basisMapping.OutputId), out var outputToBasis))
        {
            DrawAnnotations(dl, basis, Homography.Multiply(rectifiedToView, basisToOutput), Homography.Multiply(outputToBasis, rectifiedToOutput),
                            viewMin, editable, handleFade * straighten);
        }

        // The reference points on the plain projector canvas, where they can be walked onto the wall.
        var pinMapping = focusCarrier?.FindMapping(outputId);
        if (_editMode == EditModes.Output && !rectifying && focusCarrier != null && pinMapping != null && pinMapping.Quad.Length >= 4
            && SurfaceMetrics.CountPoints(focusCarrier) > 0)
        {
            DrawReferencePointPins(setup, dl, focusCarrier, pinMapping, outputId, canvasSize, editable, handleFade);
        }
    }

    /// <summary>
    /// One mapped surface on the output canvas: its quad carried through R (flying in from its Board card while
    /// the space enters), the corner-pin handles with the group drag, the label as move grip, and — for the
    /// focused surface — the anchor and the edge handles.
    /// </summary>
    private void DrawMappedSurface(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, Surface surface, Surface.OutputMapping mappingData,
                                   Guid outputId, Vector2 canvasSize, in RectifiedFraming framing, Guid focusCarrierId, bool editable, float handleFade,
                                   bool hasContent)
    {
        var rectifiedToView = framing.RectifiedToView;
        var rectifiedToOutput = framing.RectifiedToOutput;
        var viewMin = framing.ViewMin;
        Span<Vector2> labelQuad = stackalloc Vector2[4];
        // The quad in view space: R applied, then offset into the framed region. One buffer for every
        // surface — nothing below keeps it past this iteration.
        // Into the canvas' pixels first: the quad is stored as a fraction of it, and R and this canvas
        // both work in pixels — the same conversion the patches and the child regions make.
        var viewQuad = _viewQuad;
        for (var c = 0; c < 4; c++)
            viewQuad[c] = rectifiedToView.TransformPoint(mappingData.Quad[c] * canvasSize) - viewMin;

        // While the space comes in, the quad flies from the surface's Board card to its mapped place.
        if (_spaceBlend.Value < 1f && TryGetBoardQuadInView(setup, surface.Id, viewMin, _boardFlyQuad))
        {
            for (var c = 0; c < 4; c++)
                viewQuad[c] = Vector2.Lerp(_boardFlyQuad[c], viewQuad[c], _spaceBlend.Value);
        }

        ImGui.PushID(surface.Id.GetHashCode());

        // A parent recedes while one of its children is the subject, so the child's handles read first.
        // Selection styling covers the whole multi-selection; the *focused* (primary) surface keeps the
        // exclusive affordances below (edge handles, anchor, isolate).
        var isFocused = surface.Id == _shownSurfaceId;
        var isSelected = isFocused
                         || (selection?.IsSelected(SetupEntityKinds.Surface, surface.Id) ?? false);
        var emphasis = handleFade * (!isSelected && surface.Id == focusCarrierId ? 0.45f : 1f);

        // Still draggable when unselected — the canvas has no click-to-select yet, so gating edits on
        // selection would strand every surface but the one picked in the sidebar.
        var style = CornerPinHandles.Style.ForSurface(surface.Name, editable, isSelected, emphasis, hue: SetupColors.ForKind(SetupEntityKinds.Surface));
        style.ShowsChecker = !hasContent;

        // The label doubles as the surface's grab area, and it sits over the middle where an edge or corner
        // handle can land under it. Grabbing the label was the intent, so while the pointer rests on it the
        // handles go non-interactive — unless a handle drag is already live, which must not be dropped just
        // because the cursor passed over the label.
        for (var c = 0; c < 4; c++)
            labelQuad[c] = _projection.CanvasToScreen(viewQuad[c]);

        // Hovered from the sidebar or a handle (not itself the subject): highlight the frame so "which
        // frame is that row?" answers itself. The outline carries it (it reads first); the fill is only a
        // faint wash behind, and the label picks it up below.
        var surfacePulse = isSelected ? 0 : FrameStats.CrossHighlightAmount(surface.Id);
        if (surfacePulse > 0.001f)
            dl.AddQuadFilled(labelQuad[0], labelQuad[1], labelQuad[2], labelQuad[3],
                             SetupColors.ForKind(SetupEntityKinds.Surface).Fade(surfacePulse * 0.15f * handleFade));

        style.EdgeColor = PulseColor(style.EdgeColor, surfacePulse);

        var handleActive = _gesture.EditsSurface(surface.Id);
        var pointerOverLabel = !handleActive && !string.IsNullOrEmpty(surface.Name)
                               && IsMouseOverLabel(labelQuad, surface.Name);
        // In isolate only the focused frame is editable; the others are locked (they still snap).
        var lockedByIsolate = _isolatesFocusedSurface && !isFocused;
        var handlesEditable = editable && !pointerOverLabel && !lockedByIsolate;
        style.IsEditable = handlesEditable;

        // Selected corners render marked, and every editable corner is a fence-select candidate.
        var selectedMask = 0;
        for (var c = 0; c < 4; c++)
        {
            var cornerTarget = new SelectionTarget(SetupEntityKinds.Surface, surface.Id, SubParts.Corner, c);
            if (_canvasSelection.Contains(cornerTarget))
                selectedMask |= 1 << c;

            if (handlesEditable)
                _fenceCandidates.Add((cornerTarget, labelQuad[c]));
        }

        // The label is drawn separately so it can be hit-tested as the surface's pick/grab area.
        style.Label = null;
        var phase = CornerPinHandles.Draw(viewQuad, _projection, style, out var draggedCorner, out var cornerHovered, selectedMask);

        if (phase != CanvasPointHandle.DragPhases.None)
        {
            // Grabbing a corner selects it in the sub-element plane: ctrl toggles, shift adds, plain replaces —
            // unless the corner is already selected, which keeps the set so the grab starts a group drag.
            if (phase == CanvasPointHandle.DragPhases.Started && draggedCorner >= 0)
            {
                var target = new SelectionTarget(SetupEntityKinds.Surface, surface.Id, SubParts.Corner, draggedCorner);
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
                mappingData.Quad[c] = rectifiedToOutput.TransformPoint(viewQuad[c] + viewMin) / canvasSize;

            // Group drag: the dragged corner's output-space delta rides onto every other selected corner.
            if (phase == CanvasPointHandle.DragPhases.Dragging && draggedCorner >= 0)
                ApplyGroupCornerDelta(setup, outputId, surface.Id, draggedCorner,
                                      mappingData.Quad[draggedCorner] - previousDraggedCorner);
        }

        RunCornerPinGesture(phase, setup, surface.Id);

        // The label doubles as the surface's move handle: the press selects it (through the picker, so
        // stacked labels still cycle), and holding on continues into a whole-quad move — one gesture,
        // no select-first click. The move rides the corner-drag lifecycle, so undo and the straighten
        // freeze come along for free.
        if (phase == CanvasPointHandle.DragPhases.None)
        {
            var movePhase = CanvasPointHandle.DragPhases.None;
            if (_gesture.Is(GestureKinds.SurfaceMove, surface.Id))
            {
                movePhase = ImGui.IsMouseDown(ImGuiMouseButton.Left)
                                ? CanvasPointHandle.DragPhases.Dragging
                                : CanvasPointHandle.DragPhases.Completed;
            }
            else if (surface.Id == _shownSurfaceId && editable && !lockedByIsolate && TryTakeLabelGrab(labelQuad, surface.Name))
            {
                movePhase = CanvasPointHandle.DragPhases.Started;
            }

            if (movePhase == CanvasPointHandle.DragPhases.Started)
            {
                BeginGesture(setup, GestureKinds.SurfaceMove, "Move surface", surface.Id, surface, _projection.ScreenToCanvas(ImGui.GetMousePos()));
            }
            else if (movePhase == CanvasPointHandle.DragPhases.Dragging
                     && _gesture.Snapshot is { } moveSnapshot && moveSnapshot.TryGetQuad(outputId, out var preMoveQuad))
            {
                // Rigid in view space; carried through R per corner, so in a rectified view the quad
                // warps exactly as if each corner had been dragged by the same screen offset.
                var moveDelta = _projection.ScreenToCanvas(ImGui.GetMousePos()) - _gesture.GrabPoint;
                for (var c = 0; c < 4; c++)
                {
                    var moved = rectifiedToView.TransformPoint(preMoveQuad[c] * canvasSize) + moveDelta;
                    mappingData.Quad[c] = rectifiedToOutput.TransformPoint(moved) / canvasSize;
                }
            }
            else if (movePhase == CanvasPointHandle.DragPhases.Completed)
            {
                EndGesture(setup);
            }
        }

        // A handle stands in for its frame: hovering one lights the frame (and its sidebar row), and
        // grabbing one selects it — so you can't edit a frame that isn't the selected item. Isolate mode
        // takes selection off the canvas entirely, so it doesn't fire there.
        if (cornerHovered || phase != CanvasPointHandle.DragPhases.None)
            FrameStats.RequestCrossHighlight(surface.Id);

        if (phase == CanvasPointHandle.DragPhases.Started && !_isolatesFocusedSurface)
            selection?.Select(SetupEntityKinds.Surface, surface.Id);

        // Only the focused surface shows its anchor — one origin at a time, or the canvas fills with them.
        if (isFocused)
            DrawAnchorMarker(dl, surface, mappingData, rectifiedToView, viewMin, canvasSize, handleFade);

        // Edge handles belong to the focused surface only — they're contextual, and four extra dots on
        // every quad would drown the canvas. A corner moves freely (perspective); an edge crops the
        // footprint, or stretches it with Ctrl.
        if (handlesEditable && surface.Id == _shownSurfaceId)
        {
            style.EdgeHandleShape = EdgeDragStretches(surface.Id)
                                        ? CanvasPointHandle.Shapes.Circle
                                        : CanvasPointHandle.Shapes.Square;
            var edgePhase = CornerPinHandles.DrawEdgeHandles(viewQuad, _projection, style, out var edge, out var edgePos);
            if (edge >= 0)
                HandleEdgeDrag(edgePhase, setup, surface, mappingData, edge, edgePos, rectifiedToOutput, viewMin);
        }

        ImGui.PopID();

        // Under isolate the other frames' labels recede further, so the focused one clearly owns the canvas.
        var labelEmphasis = lockedByIsolate ? emphasis * 0.4f : emphasis;
        DrawEntityLabel(dl, SetupEntityKinds.Surface, labelQuad, surface.Id, surface.Name, isSelected, labelEmphasis, surfacePulse);
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

    /// <summary>
    /// Marks the surface's anchor — where the calibration raster's origin sits, and what a resize grows from.
    /// Drawn as a crosshair ring so it can't be confused with the orange top-left corner, which only marks the
    /// quad's winding.
    /// </summary>
    private void DrawAnchorMarker(ImDrawListPtr dl, Surface surface, Surface.OutputMapping mapping,
                                  Homography rectifiedToView, Vector2 viewMin, Vector2 canvasSize, float fade)
    {
        if (fade <= 0.01f || !SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, canvasSize, out var surfaceToOutput))
            return;

        // The anchor is the origin of surface space.
        DrawAnchorGlyph(dl, _projection.CanvasToScreen(rectifiedToView.TransformPoint(surfaceToOutput.TransformPoint(Vector2.Zero)) - viewMin), fade);
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
    private void HandleEdgeDrag(CanvasPointHandle.DragPhases phase, Setup setup, Surface surface, Surface.OutputMapping mapping,
                                int edge, Vector2 viewPos, Homography rectifiedToOutput, Vector2 viewMin)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                _edgeDragStretches = ImGui.GetIO().KeyCtrl;
                BeginGesture(setup, GestureKinds.SurfaceResize, _edgeDragStretches ? "Stretch surface" : "Crop surface", surface.Id, surface);
                if (!_edgeDragStretches)
                    BeginContentEdit(setup, surface);

                break;

            case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(GestureKinds.SurfaceResize, surface.Id):
            {
                // Re-base to the pre-drag rectangle first: the crop rewrites the surface's own frame, so an
                // incremental edit would compound frame over frame. From the snapshot the cursor maps to one
                // absolute edge position, stable however long the drag runs.
                _gesture.Snapshot!.Restore(surface);
                if (!SurfaceGeometry.TryGetOutputToSurface(surface, mapping, SurfaceGeometry.CanvasSizeOf(setup, mapping.OutputId), out var outputToSurface))
                    break;

                SurfaceGeometry.LocalBounds(surface, out var oldMin, out var oldMax);
                var surfacePos = outputToSurface.TransformPoint(rectifiedToOutput.TransformPoint(viewPos + viewMin));
                SurfaceGeometry.DragEdge(surface, edge, surfacePos, _edgeDragStretches);
                SurfaceGeometry.LocalBounds(surface, out var newMin, out var newMax);
                KeepContentInPlace(setup, oldMin, oldMax, newMin, newMax);
                break;
            }

            case CanvasPointHandle.DragPhases.Completed:
                if (_gesture.Is(GestureKinds.SurfaceResize, surface.Id))
                    EndGesture(setup);

                break;
        }
    }

    /// <summary>
    /// Whether an edge drag on this surface would stretch rather than crop: Ctrl at the press, then the mode
    /// the live gesture started in. Also picks the edge handles' shape, so the choice is visible before
    /// committing to it — a circle stretches, a square crops, the same signal the Board's edges give.
    /// </summary>
    private bool EdgeDragStretches(Guid surfaceId)
    {
        return _gesture.Is(GestureKinds.SurfaceResize, surfaceId) ? _edgeDragStretches : ImGui.GetIO().KeyCtrl;
    }

    /// <summary>A corner drag (the grabbed surface, plus any with selected corners riding along) as one gesture.</summary>
    private void RunCornerPinGesture(CanvasPointHandle.DragPhases phase, Setup setup, Guid surfaceId)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhases.Started:
                BeginGesture(setup, GestureKinds.CornerPin, "Adjust corner pin", surfaceId, setup.FindSurface(surfaceId));
                break;

            case CanvasPointHandle.DragPhases.Completed:
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
            if (target.Part != SubParts.Corner)
                continue;

            if (target.EntityId == draggedSurfaceId && target.Index == draggedCorner)
                continue;

            var mapping = setup.FindSurface(target.EntityId)?.FindMapping(outputId);
            if (mapping != null && target.Index >= 0 && target.Index < mapping.Quad.Length)
                mapping.Quad[target.Index] += delta;
        }
    }

    // Morph timing: eased in so it starts slowly and finishes quickly. The exponent solves 0.75^k = 0.5,
    // i.e. the visual midpoint is reached at 75% of the duration.
    private const float MorphDurationSec = 0.5f;
    private const float MorphEaseExponent = 2.41f;

    // Mode and framed subject: the edit mode (the Board is the home view, so a fresh window opens on it), the
    // surface this frame shows, and the key of the last fit.
    private EditModes _editMode = EditModes.Board;
    private bool _isolatesFocusedSurface;
    private Guid _shownSurfaceId; // frame-scoped: what the caller passed to this Draw, never read across frames
    private (Guid OutputId, EditModes Mode, Vector2 Size) _fitKey;

    // View morph: 0 = Original (projector space), 1 = Straight (focused surface rectified), one continuous
    // axis; the camera at a transition's start, so the framing eases from the user's view.
    private EasedValue _viewMorph = EasedValue.Settled(0f);
    private CanvasScope _morphFromScope;

    // Board ↔ space blend: 0 = the Board, 1 = the current space (an output's canvas, a source's texture); the
    // camera returns to the pre-space Board view on the way back.
    private readonly SpaceProjection _projection;
    private EasedValue _spaceBlend = EasedValue.Settled(0f);
    private CanvasScope _boardScopeBeforeSpace;
    private SetupEntityKinds _spaceKind;
    private Guid _spaceId;
    private Vector2 _spaceOrigin; // the space's card top-left and scale, as EnterSpace set them
    private float _spacePixelsPerMeter;

    // Output canvas scratch, reused every frame: the projector outline, the warp target, a surface's quad in
    // view, and where that quad flies in from.
    private readonly Vector2[] _canvasOutline = new Vector2[4];
    private readonly Vector2[] _warpDestQuad = new Vector2[4];
    private readonly Vector2[] _viewQuad = new Vector2[4];
    private readonly Vector2[] _boardFlyQuad = new Vector2[4];

    // The canvas sub-element plane: selected mapping-quad corners of the shown output canvas and the marquee
    // over them. Deliberately separate from the entity selection — the two planes never mix.
    private readonly SelectionSet<SelectionTarget> _canvasSelection = new();
    private readonly SelectionFence _fence = new();
    private readonly List<(SelectionTarget Target, Vector2 ScreenPos)> _fenceCandidates = new();

    // Surface edit gestures: whether the live edge drag stretches (Ctrl at the press) rather than crops, and
    // the snap candidates every editor rebuilds per drag frame into one instance, so dragging doesn't allocate.
    private bool _edgeDragStretches;
    private readonly RectSnapping _snapping = new();
}
