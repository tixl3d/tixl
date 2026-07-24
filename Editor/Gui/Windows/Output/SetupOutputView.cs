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
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Interactive editor for a selected output, in two modes: the Output canvas (corner-pin each surface's
/// quad into the output, over the live composite) and the Content canvas (drag each surface's source
/// slice over the incoming content texture). Both reuse <see cref="CornerPinHandles"/> and the
/// <see cref="ScalableCanvas"/> pan/zoom; drags go through undo commands and persist. One per output window.
/// </summary>
internal sealed partial class SetupOutputView
{
    // Declaration order is the tab order in the segmented control — source-to-sink: lay out content, rectify
    // the surface, view the projector composite, calibrate the projector. The morph axis and every switch key
    // off the enum values, not their order, so this is a purely visual arrangement.
    private enum EditMode
    {
        Content,
        Straight,
        Output,
        Calibrate,
    }

    public SetupOutputView()
    {
        _canvas.FillMode = ScalableCanvas.FillModes.FillAvailableContentRegion;
        _projection = new ScalableCanvasProjection(_canvas);
    }

    public void Draw(Guid outputId, Guid focusedSurfaceId = default, SetupEntitySelection? selection = null)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var output = setup.Outputs.Find(o => o.Id == outputId);
        if (output == null)
            return;

        _focusedSurfaceId = focusedSurfaceId;

        DrawHeader(setup, output, outputId);
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
        var hasFocusBasis = SurfaceGeometry.FindCarrier(setup, _focusedSurfaceId, outputId) != null;

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
        var focusCarrier = SurfaceGeometry.FindCarrier(setup, _focusedSurfaceId, outputId);
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
            var pivot = basis.Placement?.Pivot ?? Vector2.Zero;
            if (_dragOldQuad != null && _dragSurfaceId == basisId)
            {
                basisQuad = _dragOldQuad;
                framingFrozen = true;
            }
            else if (_resizeOldState != null && _edgeDragSurfaceId == basisId)
            {
                framingFrozen = true;
                if (_resizeOldState.Value.TryGetQuad(outputId, out var frozenQuad) && frozenQuad.Length >= 4)
                    basisQuad = frozenQuad;

                // The pivot counter-moves on every crop, and R is built from it — leaving it live feeds that
                // correction straight back into the drag, which runs away when the dragged edge is the anchor's.
                basisSize = _resizeOldState.Value.Size;
                pivot = _resizeOldState.Value.Pivot;
            }

            // Selecting a different surface while rectified moves the basis; ease it so the whole scene turns
            // toward the new selection instead of snapping there.
            basisQuad = BlendBasisTransition(basisId, basisQuad, ref basisSize, ref pivot, framingFrozen);

            Bounds(basisQuad, out var quadMin, out var quadMax);

            // Straightening lands on the surface's real content canvas (metres × px/m) — so Size (m) is what
            // gives the rectangle its aspect. Anchored at the pivot, so changing a dimension extends the rect
            // from there rather than recentring it.
            var straightSize = new Vector2(MathF.Max(basisSize.X, 0.001f),
                                           MathF.Max(basisSize.Y, 0.001f)) * MathF.Max(basis.PixelsPerMeter, 1f);
            var stageTarget = AnchoredRect(quadMin, quadMax, pivot, straightSize);

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
                Bounds(AnchoredRect(straightMin, straightMax, pivot, new Vector2(width, width / aspect)),
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
                var m = (focusMax - focusMin) * _straightSurroundFactor * (1f - toContent);
                var framedMin = focusMin - m;
                var framedMax = focusMax + m;

                // Stage two opens the framing out to the whole source, so the atlas around the slice comes
                // into view while the slice itself stays put.
                if (toContent > 0f && _sliceFramingTarget != null)
                {
                    framedMin = Vector2.Lerp(framedMin, _sliceFramingTarget.Value.Min, toContent);
                    framedMax = Vector2.Lerp(framedMax, _sliceFramingTarget.Value.Max, toContent);
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
                var immediateParent = setup.Surfaces.Find(s => s.Id == surface.ParentId);
                if (carrier == null || carrierMapping == null || immediateParent == null
                    || !SurfaceGeometry.TryGetChildQuad(setup, carrier, surface, carrierMapping, _childQuadBuffer))
                    continue;

                DrawChildRegion(setup, dl, rToView, rToOutput, viewMin, carrier, carrierMapping, immediateParent, surface, editable, handleFade);
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
            var isSelected = surface.Id == _focusedSurfaceId;
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

            var handleActive = (_dragOldQuad != null && _dragSurfaceId == surface.Id)
                               || (_resizeOldState != null && _edgeDragSurfaceId == surface.Id);
            var pointerOverLabel = !handleActive && !string.IsNullOrEmpty(surface.Name)
                                   && IsMouseOverLabel(labelQuad, surface.Name);
            // In isolate only the focused frame is editable; the others are locked (they still snap).
            var lockedByIsolate = _isolate && !isSelected;
            var handlesEditable = editable && !pointerOverLabel && !lockedByIsolate;
            style.Editable = handlesEditable;

            // The label is drawn separately so it can be hit-tested as the surface's pick/grab area.
            style.Label = null;
            var phase = CornerPinHandles.Draw(viewQuad, _projection, style, out _, out var cornerHovered);

            // Map the (possibly edited) view-space quad back to projector space. A no-op at rest / t=0.
            for (var c = 0; c < 4; c++)
                mappingData.Quad[c] = rToOutput.TransformPoint(viewQuad[c] + viewMin);

            HandleDrag(phase, surface.Id, outputId, mappingData.Quad);

            // A handle stands in for its frame: hovering one lights the frame (and its sidebar row), and
            // grabbing one selects it — so you can't edit a frame that isn't the selected item. Isolate mode
            // takes selection off the canvas entirely, so it doesn't fire there.
            if (cornerHovered || phase != CanvasPointHandle.DragPhase.None)
                FrameStats.PulseItemWithId(surface.Id);

            if (phase == CanvasPointHandle.DragPhase.Started && !_isolate)
                selection?.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);

            // Only the selected surface shows its anchor — one origin at a time, or the canvas fills with them.
            if (isSelected)
                DrawAnchorMarker(dl, surface, mappingData, rToView, viewMin, handleFade);

            // Edge handles belong to the focused surface only — they're contextual, and four extra dots on
            // every quad would drown the canvas. A corner moves freely (perspective); an edge crops.
            if (handlesEditable && surface.Id == _focusedSurfaceId)
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

        if (basis != null && basisMapping != null)
            DrawAnnotations(dl, basis, basisMapping, rToView, rToOutput, viewMin, editable, handleFade * straighten);

        DrawSliceEditor(setup, dl, focusCarrierId, viewMin, toContent);
        ResolvePicking(setup, selection);
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

    private void DrawHeader(Setup setup, OutputDefinition output, Guid outputId)
    {
        CustomComponents.StylizedText($"{output.Name} · {output.CanvasResolution.Width}×{output.CanvasResolution.Height}",
                                      Fonts.FontSmall, UiColors.TextMuted);

        ImGui.SameLine();

        // The four modes as one segmented control. Straightening rectifies a single surface, so it is only
        // usable when the focused entity resolves to one mapped to this output (for a Layout child, its
        // parent); calibration only for a projector/display. Those segments show disabled rather than
        // vanishing, so the toolbar keeps its shape.
        var straightCarrier = SurfaceGeometry.FindCarrier(setup, _focusedSurfaceId, outputId);
        var canCalibrate = output.Kind is OutputDefinition.Kinds.Projector or OutputDefinition.Kinds.Display;
        FormInputs.SegmentedButton(ref _editMode,
                                   isItemDisabled: mode => mode switch
                                                               {
                                                                   EditMode.Straight => straightCarrier == null,
                                                                   EditMode.Calibrate => !canCalibrate,
                                                                   _ => false,
                                                               });

        // A disabled segment can't be clicked away, so a mode left selected after its precondition lapses
        // (focus moved off the surface, output kind changed) is reset here instead.
        if (straightCarrier == null && _editMode == EditMode.Straight)
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
                TryStraightenFromLines(straightCarrier, outputId);

            ImGui.EndDisabled();
            if (!canStraighten && ImGui.IsItemHovered())
                ImGui.SetTooltip($"Trace at least {MinLinesToStraighten} reference lines along features that are straight in reality.");

            var canApply = straightCarrier.Annotations.Exists(a => a.LengthInMeters > 0);
            ImGui.SameLine();
            ImGui.BeginDisabled(!canApply);
            if (ImGui.SmallButton("Apply lengths") && canApply)
                TryApplyLengths(setup, straightCarrier);

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
                AddMapping(surface, output, outputId);
                OutputSetupHandling.SaveActive();
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
    private Vector2[] BlendBasisTransition(Guid basisId, Vector2[] targetQuad, ref Vector2 targetSize, ref Vector2 targetPivot, bool frozen)
    {
        if (basisId != _basisTransitionId)
        {
            if (!frozen && _basisTransitionId != Guid.Empty && basisId != Guid.Empty && _basisHasLast)
            {
                for (var i = 0; i < 4; i++)
                    _basisFromQuad[i] = _basisLastQuad[i];

                _basisFromSize = _basisLastSize;
                _basisFromPivot = _basisLastPivot;
                _basisMorph = 0f;
            }
            else
            {
                _basisMorph = 1f;
            }

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
            targetPivot = Vector2.Lerp(_basisFromPivot, targetPivot, t);
            resultQuad = _basisBlendQuad;
        }

        // Remember the resolved basis, so a transition interrupted by another selection chains from here.
        for (var i = 0; i < 4; i++)
            _basisLastQuad[i] = resultQuad[i];

        _basisLastSize = targetSize;
        _basisLastPivot = targetPivot;
        _basisHasLast = true;
        return resultQuad;
    }

    private void FitToArea(Vector2 size, EditMode mode, Guid outputId, bool keepScope = false)
    {
        var key = (outputId, mode, size);

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
    private void DrawChildRegion(Setup setup, ImDrawListPtr dl, Homography rToView, Homography rToOutput, Vector2 viewMin,
                                 Surface carrier, Surface.OutputMapping carrierMapping, Surface parent, Surface child,
                                 bool editable, float fade)
    {
        if (fade <= 0.01f)
            return;

        var isFocused = child.Id == _focusedSurfaceId;

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

        var style = CornerPinHandles.Style.ForSurface(child.Name, editable && isFocused, isFocused, fade);
        var childPulse = isFocused ? 0f : FrameStats.GetPulse(child.Id);
        if (isFocused)
            dl.AddQuadFilled(screen[0], screen[1], screen[2], screen[3], UiColors.StatusActivated.Fade(0.12f * fade));
        else if (childPulse > 0.001f)
            dl.AddQuadFilled(screen[0], screen[1], screen[2], screen[3], UiColors.StatusActivated.Fade(childPulse * 0.2f * fade));

        // The outline carries the hover highlight, same as a top-level surface.
        CanvasDraw.QuadOutline(dl, screen, PulseColor(style.EdgeColor, childPulse), isFocused ? 2f : 1f);

        // A region has its own anchor, in its own space — mapped out through the parent's rectangle and pin.
        if (isFocused
            && SurfaceGeometry.TryGetSurfaceToOutput(carrier, carrierMapping, out var carrierToOutput)
            && SurfaceGeometry.TryGetDescendantRect(setup, carrier, child, out var rectMin, out _, out _))
        {
            var anchorInCarrier = rectMin + SurfaceGeometry.AnchorInSurface(child);
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
            DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, screen, child.Id, child.Name, isFocused, fade, childPulse);
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
                                    if (SurfaceGeometry.TrySnapOffset(horizontal ? _snapXs : _snapYs, anchor,
                                                                      SnapThreshold(parentProjection, rToView, viewMin, parent, edgeParentOrigin),
                                                                      out var offset, out var target))
                                    {
                                        if (horizontal)
                                            pos.X += offset;
                                        else
                                            pos.Y += offset;

                                        guide = target;
                                    }
                                }

                                var rect = SurfaceGeometry.ChildRectInParent(parent, child);
                                var min = rect[0];
                                var max = rect[2];
                                switch (edge)
                                {
                                    case 0: min.Y = MathF.Min(pos.Y, max.Y - SurfaceGeometry.MinSize); break;
                                    case 1: max.X = MathF.Max(pos.X, min.X + SurfaceGeometry.MinSize); break;
                                    case 2: max.Y = MathF.Max(pos.Y, min.Y + SurfaceGeometry.MinSize); break;
                                    default: min.X = MathF.Min(pos.X, max.X - SurfaceGeometry.MinSize); break;
                                }

                                SurfaceGeometry.SetChildRect(parent, child, min, max);

                                if (guide.HasValue && hasProjection)
                                    DrawSnapGuide(dl, parentProjection, rToView, viewMin, parent, horizontal, guide.Value, edgeParentOrigin);
                            });
        }

        ImGui.PopID();

        DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, screen, child.Id, child.Name, isFocused, fade, childPulse);
        HandleLabelMove(setup, dl, rToView, rToOutput, viewMin, outputToSurface, carrier, carrierMapping, parent, child, screen);
    }

    /// <summary>
    /// The label doubles as the region's move handle. Free movement, but a nearly-straight drag snaps flat and
    /// draws the axis it locked to — placing a region level with its neighbours is the common case.
    /// </summary>
    private void HandleLabelMove(Setup setup, ImDrawListPtr dl, Homography rToView, Homography rToOutput, Vector2 viewMin,
                                 Homography outputToSurface, Surface carrier, Surface.OutputMapping carrierMapping,
                                 Surface parent, Surface child, ReadOnlySpan<Vector2> screen)
    {
        if (string.IsNullOrEmpty(child.Name))
            return;

        var isMoving = _labelMoveSurfaceId == child.Id;
        if (!isMoving)
        {
            if (_labelMoveSurfaceId != Guid.Empty || !ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsAnyItemHovered())
                return;

            var (min, max) = CornerPinHandles.GetCenteredLabelRect(screen, child.Name);
            var mouse = ImGui.GetMousePos();
            if (mouse.X < min.X || mouse.X > max.X || mouse.Y < min.Y || mouse.Y > max.Y)
                return;

            var rect = SurfaceGeometry.ChildRectInParent(parent, child);
            _labelMoveSurfaceId = child.Id;
            _childMoveStart = (ToParentSpace(setup, carrier, child, outputToSurface, rToOutput, viewMin), rect[0], rect[2]);
            _resizeOldState = new ResizeSurfaceCommand.State(child);
            _childMoveAxis = 0;
            return;
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (_resizeOldState != null)
            {
                UndoRedoStack.Add(new ResizeSurfaceCommand(child.Id, _resizeOldState.Value, new ResizeSurfaceCommand.State(child)));
                OutputSetupHandling.SaveActive();
                _resizeOldState = null;
            }

            _labelMoveSurfaceId = Guid.Empty;
            _childMoveStart = null;
            _childMoveAxis = 0;
            return;
        }

        if (_childMoveStart == null)
            return;

        SurfaceGeometry.TryGetDescendantRect(setup, carrier, child, out _, out _, out var parentOrigin);
        var (origin, startMin, startMax) = _childMoveStart.Value;
        var delta = ToParentSpace(setup, carrier, child, outputToSurface, rToOutput, viewMin) - origin;
        var snapping = !ImGui.GetIO().KeyShift;

        // ~14° cone around each axis.
        if (snapping)
        {
            var lockX = MathF.Abs(delta.X) > MathF.Abs(delta.Y) * 4;
            var lockY = MathF.Abs(delta.Y) > MathF.Abs(delta.X) * 4;
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

        var hasProjection = SurfaceGeometry.TryGetSurfaceToOutput(carrier, carrierMapping, out var surfaceToOutput);
        float? guideX = null;
        float? guideY = null;

        // Align to the parent's and the siblings' edges and centres — which also makes dropping a region into
        // a corner just land there, since the parent's own edges are candidates.
        if (snapping && hasProjection)
        {
            var threshold = SnapThreshold(surfaceToOutput, rToView, viewMin, parent, parentOrigin);
            SurfaceGeometry.CollectSnapCandidates(setup, parent, child.Id, _snapXs, _snapYs);

            Span<float> anchorsX = [newMin.X, (newMin.X + newMax.X) * 0.5f, newMax.X];
            if (SurfaceGeometry.TrySnapOffset(_snapXs, anchorsX, threshold, out var offsetX, out var targetX))
            {
                newMin.X += offsetX;
                newMax.X += offsetX;
                guideX = targetX;
            }

            Span<float> anchorsY = [newMin.Y, (newMin.Y + newMax.Y) * 0.5f, newMax.Y];
            if (SurfaceGeometry.TrySnapOffset(_snapYs, anchorsY, threshold, out var offsetY, out var targetY))
            {
                newMin.Y += offsetY;
                newMax.Y += offsetY;
                guideY = targetY;
            }
        }

        SurfaceGeometry.SetChildRect(parent, child, newMin, newMax);

        if (!hasProjection)
            return;

        if (guideX.HasValue)
            DrawSnapGuide(dl, surfaceToOutput, rToView, viewMin, parent, true, guideX.Value, parentOrigin);

        if (guideY.HasValue)
            DrawSnapGuide(dl, surfaceToOutput, rToView, viewMin, parent, false, guideY.Value, parentOrigin);

        if (_childMoveAxis == 0)
            return;

        // The locked movement axis, drawn across the parent so it reads as a guide rather than a stub.
        var rectNow = SurfaceGeometry.ChildRectInParent(parent, child);
        var mid = (rectNow[0] + rectNow[2]) * 0.5f;
        DrawSnapGuide(dl, surfaceToOutput, rToView, viewMin, parent, _childMoveAxis == 2, _childMoveAxis == 1 ? mid.Y : mid.X, parentOrigin);
    }

    private static Vector2[] RectCorners(Vector2 min, Vector2 max)
    {
        return [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)];
    }

    /// <summary>Snap distance in the parent's own units, from a fixed screen distance — so it feels the same
    /// however far you're zoomed in, and on a surface of any real size.</summary>
    private float SnapThreshold(Homography surfaceToOutput, Homography rToView, Vector2 viewMin, Surface parent, Vector2 originInCarrier)
    {
        var a = _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(originInCarrier)) - viewMin);
        var b = _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(originInCarrier + new Vector2(parent.SizeInMeters.X, 0))) - viewMin);
        var pixels = Vector2.Distance(a, b);
        return pixels > 1f ? parent.SizeInMeters.X / pixels * 7 * T3Ui.UiScaleFactor : 0f;
    }

    private void DrawSnapGuide(ImDrawListPtr dl, Homography surfaceToOutput, Homography rToView, Vector2 viewMin,
                               Surface parent, bool vertical, float coordinate, Vector2 originInCarrier)
    {
        // Coordinates are in the parent's space; the projection expects the carrier's, so step across.
        var size = parent.SizeInMeters;
        var from = originInCarrier + (vertical ? new Vector2(coordinate, -size.Y) : new Vector2(-size.X, coordinate));
        var to = originInCarrier + (vertical ? new Vector2(coordinate, size.Y * 2) : new Vector2(size.X * 2, coordinate));

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
        var (min, max) = CornerPinHandles.GetCenteredLabelRect(screenQuad, name);
        var mouse = ImGui.GetMousePos();
        return mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
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
            var canPick = !_isolate || hit.Id == _focusedSurfaceId;

            // A drag on the selected region's label moves it, so that press mustn't also count as a pick.
            if (canPick && hit.LeftClicked && _labelMoveSurfaceId == Guid.Empty)
                SelectPicked(selection, hit.Kind, hit.Id);

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

        // Ctrl+D duplicates the selected surface, matching the menu entry.
        if (selection != null
            && _focusedSurfaceId != Guid.Empty
            && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)
            && ImGui.GetIO().KeyCtrl
            && ImGui.IsKeyPressed(ImGuiKey.D, false))
        {
            var focused = setup.Surfaces.Find(s => s.Id == _focusedSurfaceId);
            if (focused != null)
                SetupPanel.DuplicateSurface(selection, setup, focused);
        }
    }

    private void SelectPicked(SetupEntitySelection? selection, SetupEntitySelection.EntityKind kind, Guid id)
    {
        selection?.Select(kind, id);
        // The atlas view tracks its edited slice locally too, so keep it in step with the selection.
        if (kind == SetupEntitySelection.EntityKind.Slice)
            _selectedSliceId = id;
    }

    private void DrawPickMenu(Setup setup, SetupEntitySelection? selection)
    {
        if (selection == null)
            return;

        switch (_menuKind)
        {
            case SetupEntitySelection.EntityKind.Surface:
            {
                var surface = setup.Surfaces.Find(x => x.Id == _menuId);
                if (surface != null)
                    SetupPanel.DrawSurfaceMenuItems(selection, setup, surface, includeDelete: true);

                break;
            }
            case SetupEntitySelection.EntityKind.Slice:
            {
                var slice = setup.Slices.Find(x => x.Id == _menuId);
                if (slice != null)
                    SetupPanel.DrawSliceMenuItems(selection, setup, slice);

                break;
            }
        }
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
                    UndoRedoStack.Add(new ResizeSurfaceCommand(target.Id, _resizeOldState.Value,
                                                               new ResizeSurfaceCommand.State(target)));
                    OutputSetupHandling.SaveActive();
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

        // Pivot is normalized from the surface's bottom-left; surface space runs Y down.
        var size = surface.SizeInMeters;
        var pivot = surface.Placement?.Pivot ?? Vector2.Zero;
        var anchorInSurface = new Vector2(pivot.X * size.X, size.Y - pivot.Y * size.Y);

        DrawAnchorGlyph(dl, _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(anchorInSurface)) - viewMin), fade);
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

    private void HandleDrag(CanvasPointHandle.DragPhase phase, Guid surfaceId, Guid outputId, Vector2[] liveQuad)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                // The quad still holds its pre-drag value on the activation frame.
                _dragOldQuad = (Vector2[])liveQuad.Clone();
                _dragSurfaceId = surfaceId;
                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_dragOldQuad != null)
                {
                    // Value already applied live during the drag.
                    UndoRedoStack.Add(new ChangeOutputMappingQuadCommand(surfaceId, outputId, _dragOldQuad, liveQuad));
                    OutputSetupHandling.SaveActive();
                    _dragOldQuad = null;
                    _dragSurfaceId = Guid.Empty;
                }

                break;
        }
    }

    /// <summary>
    /// An axis-aligned rect of <paramref name="size"/> placed so its anchor coincides with the same anchor of
    /// the reference box — so resizing extends the rect from the anchor instead of recentring it. The pivot is
    /// normalized from the surface's bottom-left, while canvas Y grows downward. Returns TL, TR, BR, BL.
    /// </summary>
    private static Vector2[] AnchoredRect(Vector2 refMin, Vector2 refMax, Vector2 pivot, Vector2 size)
    {
        var anchorX = refMin.X + pivot.X * (refMax.X - refMin.X);
        var anchorY = refMax.Y - pivot.Y * (refMax.Y - refMin.Y);

        var minX = anchorX - pivot.X * size.X;
        var maxX = minX + size.X;
        var maxY = anchorY + pivot.Y * size.Y;
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
    private EditMode _editMode = EditMode.Output;
    private bool _isolate;
    private Guid _focusedSurfaceId;
    private (Guid, EditMode, Vector2) _fitKey;

    // View morph position: 0 = Original (projector space), 1 = Straight (focused surface rectified),
    // 2 = Content (framing tightened onto that surface). One continuous axis, animated.
    private float _viewMorph;
    private float _morphTarget;
    private float _morphFrom;
    private float _morphProgress = 1f; // 1 = settled (no animation running)

    // Canvas scale/offset at the moment the morph started, so the framing eases from the user's view.
    private CanvasScope _morphFromScope;

    // Basis transition: eases the rectify basis (quad/size/pivot) from the previously focused surface to the
    // newly selected one, so switching selection in a rectified view turns the scene rather than snapping.
    private readonly Vector2[] _basisFromQuad = new Vector2[4];
    private readonly Vector2[] _basisLastQuad = new Vector2[4];
    private readonly Vector2[] _basisBlendQuad = new Vector2[4];
    private Vector2 _basisFromSize, _basisLastSize, _basisFromPivot, _basisLastPivot;
    private Guid _basisTransitionId;
    private float _basisMorph = 1f; // 1 = settled
    private bool _basisHasLast;

    // Pre-drag quad snapshot + which surface it belongs to (so R can freeze only when the basis is dragged).
    private Vector2[]? _dragOldQuad;
    private Guid _dragSurfaceId;

    // Label chips collected this frame (id + screen rect) and the pick they resolve to — labels double as
    // each surface's click target, and overlapping ones cycle.
    private readonly CanvasItemPicker<SetupEntitySelection.EntityKind> _picker = new();
    private Guid _labelMoveSurfaceId;
    private SetupEntitySelection.EntityKind _menuKind;
    private Guid _menuId;
    private Guid _selectedSliceId;

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
