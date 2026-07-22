#nullable enable
using ImGuiNET;
using T3.Core.Logging;
using T3.Core.Output;
using T3.Core.Resource;
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
internal sealed class SetupOutputView
{
    private enum EditMode
    {
        Output,
        Straight,
        Content,
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

        _canvas.UpdateCanvas(out _);

        if (_editMode == EditMode.Calibrate)
            DrawCalibrationMarkers(output, outputId);
        else if (_editMode == EditMode.Content && !hasFocusBasis)
            DrawContentCanvas(outputId); // no surface to frame onto — plain content preview
        else
            DrawOutputCanvas(setup, output, outputId, selection); // Original / Straight / Content, morphed by _viewMorph
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
            }
            else if (_resizeOldState != null && _edgeDragSurfaceId == basisId)
            {
                if (_resizeOldState.Value.TryGetQuad(outputId, out var frozenQuad) && frozenQuad.Length >= 4)
                    basisQuad = frozenQuad;

                // The pivot counter-moves on every crop, and R is built from it — leaving it live feeds that
                // correction straight back into the drag, which runs away when the dragged edge is the anchor's.
                basisSize = _resizeOldState.Value.Size;
                pivot = _resizeOldState.Value.Pivot;
            }

            Bounds(basisQuad, out var quadMin, out var quadMax);

            // Straightening lands on the surface's real content canvas (metres × px/m) — so Size (m) is what
            // gives the rectangle its aspect. Anchored at the pivot, so changing a dimension extends the rect
            // from there rather than recentring it.
            var straightSize = new Vector2(MathF.Max(basisSize.X, 0.001f),
                                           MathF.Max(basisSize.Y, 0.001f)) * MathF.Max(basis.PixelsPerMeter, 1f);
            var stageTarget = AnchoredRect(quadMin, quadMax, pivot, straightSize);

            // Stage two restretches that to the content's own aspect: the composite holds the source already
            // fitted to the surface, so mapping the quad onto this un-squeezes it. Anchored the same way.
            if (toContent > 0f
                && OutputManager.TryGetTargetContentAspect(basis.Id, out var contentAspect)
                && contentAspect > 0.0001f)
            {
                Bounds(stageTarget, out var straightMin, out var straightMax);
                var width = straightMax.X - straightMin.X;
                var contentRect = AnchoredRect(straightMin, straightMax, pivot, new Vector2(width, width / contentAspect));
                stageTarget =
                    [
                        Vector2.Lerp(stageTarget[0], contentRect[0], toContent),
                        Vector2.Lerp(stageTarget[1], contentRect[1], toContent),
                        Vector2.Lerp(stageTarget[2], contentRect[2], toContent),
                        Vector2.Lerp(stageTarget[3], contentRect[3], toContent),
                    ];
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
                viewMin = Vector2.Lerp(Vector2.Zero, focusMin - m, straighten);
                var viewMax = Vector2.Lerp(canvasSize, focusMax + m, straighten);
                viewSize = viewMax - viewMin;
            }
            else
            {
                rToView = _identity;
                rToOutput = _identity;
            }
        }

        var rectifying = _viewMorph > 0.0001f;
        FitToArea(viewSize, EditMode.Output, outputId);

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

        _labelTargets.Clear();
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

                var parent = setup.Surfaces.Find(s => s.Id == surface.ParentId);
                var parentMapping = parent?.OutputMappings.Find(m => m.OutputId == outputId);
                if (parent == null || parentMapping == null
                    || !SurfaceGeometry.TryGetChildQuad(parent, surface, parentMapping, _childQuadBuffer))
                    continue;

                DrawChildRegion(dl, rToView, rToOutput, viewMin, parent, parentMapping, surface, editable, handleFade);
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

            // The label is drawn separately so it can be hit-tested as the surface's pick/grab area.
            style.Label = null;
            var phase = CornerPinHandles.Draw(viewQuad, _projection, style, out _);

            // Map the (possibly edited) view-space quad back to projector space. A no-op at rest / t=0.
            for (var c = 0; c < 4; c++)
                mappingData.Quad[c] = rToOutput.TransformPoint(viewQuad[c] + viewMin);

            HandleDrag(phase, surface.Id, outputId, mappingData.Quad);

            // Only the selected surface shows its anchor — one origin at a time, or the canvas fills with them.
            if (isSelected)
                DrawAnchorMarker(dl, surface, mappingData, rToView, viewMin, handleFade);

            // Edge handles belong to the focused surface only — they're contextual, and four extra dots on
            // every quad would drown the canvas. A corner moves freely (perspective); an edge crops.
            if (editable && surface.Id == _focusedSurfaceId)
            {
                var edgePhase = CornerPinHandles.DrawEdgeHandles(viewQuad, _projection, style, out var edge, out var edgePos);
                if (edge >= 0)
                    HandleEdgeDrag(edgePhase, surface, mappingData, edge, edgePos, rToOutput, viewMin);
            }

            ImGui.PopID();

            for (var c = 0; c < 4; c++)
                labelQuad[c] = _projection.CanvasToScreen(viewQuad[c]);

            DrawSurfaceLabel(dl, labelQuad, surface.Id, surface.Name, isSelected, emphasis);
        }

        ResolveLabelPicking(selection);
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
        if (CustomComponents.StateButton("Output", ModeButtonState(EditMode.Output)))
            _editMode = EditMode.Output;

        // Straightening rectifies a single surface, so only offer it when the focused entity resolves to one
        // mapped to this output — for a Layout child that's its parent.
        if (SurfaceGeometry.FindCarrier(setup, _focusedSurfaceId, outputId) != null)
        {
            ImGui.SameLine();
            if (CustomComponents.StateButton("Straight", ModeButtonState(EditMode.Straight)))
                _editMode = EditMode.Straight;
        }
        else if (_editMode == EditMode.Straight)
        {
            // Focus moved off the surface — fall back so we don't sit in a mode with no button to leave it.
            _editMode = EditMode.Output;
        }

        ImGui.SameLine();
        if (CustomComponents.StateButton("Content", ModeButtonState(EditMode.Content)))
            _editMode = EditMode.Content;

        if (output.Kind is OutputDefinition.Kinds.Projector or OutputDefinition.Kinds.Display)
        {
            ImGui.SameLine();
            if (CustomComponents.StateButton("Calibrate", ModeButtonState(EditMode.Calibrate)))
                _editMode = EditMode.Calibrate;
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

    private CustomComponents.ButtonStates ModeButtonState(EditMode mode)
    {
        return _editMode == mode ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default;
    }

    private void FitToArea(Vector2 size, EditMode mode, Guid outputId)
    {
        var key = (outputId, mode, size);

        // While the view morphs, ease the canvas scope from wherever the user had panned/zoomed it to the fit
        // for the current framing. Snapping straight to the fit (as we do at rest) would throw their view away
        // the instant a transition starts — the scale/offset has to animate along with everything else.
        if (_morphProgress < 1f)
        {
            _canvas.FitAreaOnCanvas(ImRect.RectWithSize(Vector2.Zero, size));
            var fit = _canvas.GetTargetScope();
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

        _canvas.FitAreaOnCanvas(ImRect.RectWithSize(Vector2.Zero, size));
        _canvas.SetScopeInstant(_canvas.GetTargetScope());
        _fitKey = key;
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
    private void DrawChildRegion(ImDrawListPtr dl, Homography rToView, Homography rToOutput, Vector2 viewMin,
                                 Surface parent, Surface.OutputMapping parentMapping, Surface child, bool editable, float fade)
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
        if (isFocused)
            dl.AddQuadFilled(screen[0], screen[1], screen[2], screen[3], UiColors.StatusActivated.Fade(0.12f * fade));

        for (var i = 0; i < 4; i++)
            dl.AddLine(screen[i], screen[(i + 1) % 4], style.EdgeColor, (isFocused ? 2f : 1f) * T3Ui.UiScaleFactor);

        // A region has its own anchor, in its own space — mapped out through the parent's rectangle and pin.
        if (isFocused && SurfaceGeometry.TryGetSurfaceToOutput(parent, parentMapping, out var parentToOutput))
        {
            var anchorInParent = SurfaceGeometry.ChildRectInParent(parent, child)[0] + SurfaceGeometry.AnchorInSurface(child);
            DrawAnchorGlyph(dl, _projection.CanvasToScreen(rToView.TransformPoint(parentToOutput.TransformPoint(anchorInParent)) - viewMin), fade);
        }

        if (!isFocused || !editable)
        {
            if (!string.IsNullOrEmpty(child.Name))
                CornerPinHandles.DrawCenteredLabel(dl, screen, child.Name, style.LabelColor, style.LabelBackgroundColor);

            return;
        }

        // Edited in the parent's space: the child has no projection of its own, so the parent's inverse maps
        // handles back into plain rectangle edits. Nothing here changes the parent, so the transform driving
        // this view stays put and the drag can't feed back on itself.
        if (!SurfaceGeometry.TryGetOutputToSurface(parent, parentMapping, out var outputToSurface))
            return;

        ImGui.PushID(child.Id.GetHashCode());

        var edgePhase = CornerPinHandles.DrawEdgeHandles(viewQuad, _projection, style, out var edge, out var edgePos);
        if (edge >= 0)
        {
            HandleChildEdit(edgePhase, parent, child,
                            () =>
                            {
                                var pos = outputToSurface.TransformPoint(rToOutput.TransformPoint(edgePos + viewMin));
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
                            });
        }

        ImGui.PopID();

        DrawSurfaceLabel(dl, screen, child.Id, child.Name, isFocused, fade);
        HandleLabelMove(dl, rToView, rToOutput, viewMin, outputToSurface, parent, parentMapping, child, screen);
    }

    /// <summary>
    /// The label doubles as the region's move handle. Free movement, but a nearly-straight drag snaps flat and
    /// draws the axis it locked to — placing a region level with its neighbours is the common case.
    /// </summary>
    private void HandleLabelMove(ImDrawListPtr dl, Homography rToView, Homography rToOutput, Vector2 viewMin,
                                 Homography outputToSurface, Surface parent, Surface.OutputMapping parentMapping,
                                 Surface child, ReadOnlySpan<Vector2> screen)
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
            _childMoveStart = (ToParentSpace(outputToSurface, rToOutput, viewMin), rect[0], rect[2]);
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

        var (origin, startMin, startMax) = _childMoveStart.Value;
        var delta = ToParentSpace(outputToSurface, rToOutput, viewMin) - origin;

        // ~14° cone around each axis.
        var snapX = MathF.Abs(delta.X) > MathF.Abs(delta.Y) * 4;
        var snapY = MathF.Abs(delta.Y) > MathF.Abs(delta.X) * 4;
        if (snapX)
            delta.Y = 0;
        else if (snapY)
            delta.X = 0;

        _childMoveAxis = snapX ? 1 : snapY ? 2 : 0;
        SurfaceGeometry.SetChildRect(parent, child, startMin + delta, startMax + delta);

        if (_childMoveAxis == 0 || !SurfaceGeometry.TryGetSurfaceToOutput(parent, parentMapping, out var surfaceToOutput))
            return;

        // The locked axis, drawn across the parent so it reads as a guide rather than a stub.
        var rectNow = SurfaceGeometry.ChildRectInParent(parent, child);
        var mid = (rectNow[0] + rectNow[2]) * 0.5f;
        var parentSize = parent.SizeInMeters;
        var from = _childMoveAxis == 1 ? new Vector2(-parentSize.X, mid.Y) : new Vector2(mid.X, -parentSize.Y);
        var to = _childMoveAxis == 1 ? new Vector2(parentSize.X * 2, mid.Y) : new Vector2(mid.X, parentSize.Y * 2);

        var a = _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(from)) - viewMin);
        var b = _projection.CanvasToScreen(rToView.TransformPoint(surfaceToOutput.TransformPoint(to)) - viewMin);
        dl.AddLine(a, b, UiColors.StatusAnimated.Fade(0.5f), 1 * T3Ui.UiScaleFactor);
    }

    /// <summary>The mouse in the parent surface's own space — through the view transform, then the parent's pin.</summary>
    private Vector2 ToParentSpace(Homography outputToSurface, Homography rToOutput, Vector2 viewMin)
    {
        var inView = _projection.ScreenToCanvas(ImGui.GetMousePos());
        return outputToSurface.TransformPoint(rToOutput.TransformPoint(inView + viewMin));
    }

    /// <summary>
    /// Centre handle that slides the whole region. Free movement, but nearly-axis-aligned drags snap flat and
    /// draw the axis they locked to — placing a region level with its neighbours is the common case.
    /// </summary>
    /// <summary>
    /// Draws a surface's label chip and registers it as a pick target. The label is the surface's grab area —
    /// hovering lifts it, clicking selects, and for a selected region dragging it moves the region.
    /// </summary>
    private void DrawSurfaceLabel(ImDrawListPtr dl, ReadOnlySpan<Vector2> screenQuad, Guid id, string name, bool isSelected, float emphasis)
    {
        if (string.IsNullOrEmpty(name) || emphasis <= 0.01f)
            return;

        var rect = CornerPinHandles.GetCenteredLabelRect(screenQuad, name);
        _labelTargets.Add((id, rect.Min, rect.Max));

        var alpha = (id == _labelPickId ? 1f : 0.9f) * emphasis;
        var text = (isSelected ? UiColors.ForegroundFull : UiColors.Text).Fade((isSelected ? 1f : 0.7f) * alpha);
        var background = (isSelected ? UiColors.StatusActivated : UiColors.BackgroundFull).Fade((isSelected ? 1f : 0.6f) * alpha);
        CornerPinHandles.DrawLabelChip(dl, rect, name, text, background);
    }

    /// <summary>
    /// Resolves clicks on the label chips collected this frame. Overlapping labels cycle: each click picks the
    /// one after whatever is currently selected, so a stack of regions can be reached without moving anything.
    /// Also decides which label the next frame draws as hovered.
    /// </summary>
    private void ResolveLabelPicking(SetupEntitySelection? selection)
    {
        _labelsUnderMouse.Clear();
        var mouse = ImGui.GetMousePos();
        foreach (var (id, min, max) in _labelTargets)
        {
            if (mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y)
                _labelsUnderMouse.Add(id);
        }

        if (_labelsUnderMouse.Count == 0 || ImGui.IsAnyItemHovered())
        {
            _labelPickId = Guid.Empty;
            return;
        }

        var next = _labelsUnderMouse[(_labelsUnderMouse.IndexOf(_focusedSurfaceId) + 1) % _labelsUnderMouse.Count];
        _labelPickId = next;

        // A drag on the selected region's label moves it, so that press mustn't also count as a pick.
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _labelMoveSurfaceId == Guid.Empty)
            selection?.Select(SetupEntitySelection.EntityKind.Surface, next);
    }

    /// <summary>Runs a child rectangle edit through the same snapshot/undo lifecycle as a surface resize.</summary>
    private void HandleChildEdit(CanvasPointHandle.DragPhase phase, Surface parent, Surface child,
                                 Action applyDrag, Action? onStarted = null, Action? onCompleted = null)
    {
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                _resizeOldState = new ResizeSurfaceCommand.State(child);
                _edgeDragSurfaceId = child.Id;
                onStarted?.Invoke();
                break;

            case CanvasPointHandle.DragPhase.Dragging:
                applyDrag();
                break;

            case CanvasPointHandle.DragPhase.Completed:
                if (_resizeOldState != null)
                {
                    UndoRedoStack.Add(new ResizeSurfaceCommand(child.Id, _resizeOldState.Value,
                                                               new ResizeSurfaceCommand.State(child)));
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

    private static void DrawAnchorGlyph(ImDrawListPtr dl, Vector2 screen, float fade)
    {
        // Deliberately not the corner marker's orange (StatusAnimated) — these are unrelated things.
        var color = UiColors.StatusControlled.Fade(fade);
        var radius = 7 * T3Ui.UiScaleFactor;
        var arm = radius * 1.9f;

        dl.AddCircle(screen, radius, color, 0, 1.5f * T3Ui.UiScaleFactor);
        dl.AddLine(screen - new Vector2(arm, 0), screen + new Vector2(arm, 0), color, 1.5f * T3Ui.UiScaleFactor);
        dl.AddLine(screen - new Vector2(0, arm), screen + new Vector2(0, arm), color, 1.5f * T3Ui.UiScaleFactor);
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
        switch (phase)
        {
            case CanvasPointHandle.DragPhase.Started:
                _resizeOldState = new ResizeSurfaceCommand.State(surface);
                _edgeDragSurfaceId = surface.Id;
                break;

            case CanvasPointHandle.DragPhase.Dragging:
            {
                if (_resizeOldState == null)
                    return;

                // Re-base to the pre-drag rectangle first: the crop rewrites the surface's own frame, so an
                // incremental edit would compound frame over frame. From the snapshot the cursor maps to one
                // absolute edge position, which is stable however long the drag runs.
                _resizeOldState.Value.Restore(surface);
                if (!SurfaceGeometry.TryGetOutputToSurface(surface, mapping, out var outputToSurface))
                    return;

                var surfacePos = outputToSurface.TransformPoint(rToOutput.TransformPoint(viewPos + viewMin));
                SurfaceGeometry.DragEdge(surface, edge, surfacePos, ImGui.GetIO().KeyCtrl);
                break;
            }

            case CanvasPointHandle.DragPhase.Completed:
                if (_resizeOldState != null)
                {
                    // Value already applied live during the drag.
                    UndoRedoStack.Add(new ResizeSurfaceCommand(surface.Id, _resizeOldState.Value,
                                                               new ResizeSurfaceCommand.State(surface)));
                    OutputSetupHandling.SaveActive();
                    _resizeOldState = null;
                    _edgeDragSurfaceId = Guid.Empty;
                }

                break;
        }
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

    // Pre-drag quad snapshot + which surface it belongs to (so R can freeze only when the basis is dragged).
    private Vector2[]? _dragOldQuad;
    private Guid _dragSurfaceId;

    // Label chips collected this frame (id + screen rect) and the pick they resolve to — labels double as
    // each surface's click target, and overlapping ones cycle.
    private readonly List<(Guid Id, Vector2 Min, Vector2 Max)> _labelTargets = [];
    private readonly List<Guid> _labelsUnderMouse = [];
    private Guid _labelPickId;
    private Guid _labelMoveSurfaceId;

    // Scratch for a Layout child's derived quad; consumed before the next child reuses it.
    private readonly Vector2[] _childQuadBuffer = new Vector2[4];

    // Move-gizmo drag: the cursor and rectangle in the parent's space when it started, plus the axis a
    // nearly-straight drag locked to (0 none, 1 horizontal, 2 vertical) so the guide can be drawn.
    private (Vector2 Origin, Vector2 Min, Vector2 Max)? _childMoveStart;
    private int _childMoveAxis;

    // Pre-drag rectangle snapshot for an edge crop (size + every quad that follows it), and whose it is.
    private ResizeSurfaceCommand.State? _resizeOldState;
    private Guid _edgeDragSurfaceId;
}
