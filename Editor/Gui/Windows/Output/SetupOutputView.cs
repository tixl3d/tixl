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

    public void Draw(Guid outputId, Guid focusedSurfaceId = default)
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
        var hasFocusBasis = _focusedSurfaceId != Guid.Empty
                            && setup.Surfaces.Exists(s => s.Id == _focusedSurfaceId
                                                          && s.OutputMappings.Exists(m => m.OutputId == outputId));

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
            DrawOutputCanvas(setup, output, outputId); // Original / Straight / Content, morphed by _viewMorph
    }

    // The output canvas carries a global rectify transform R (output px → view space): identity at _viewMorph
    // 0 (Original), and by 1 (Straight) it maps the focused surface's quad onto its own axis-aligned bounding
    // box, carrying the whole composite and every surface with it. From 1 to 2 (Content) R holds and the
    // framing tightens onto that surface. Blending R and the framing is what makes the views morph.
    private void DrawOutputCanvas(Setup setup, OutputDefinition output, Guid outputId)
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
        var basis = _viewMorph > 0.0001f ? setup.Surfaces.Find(s => s.Id == _focusedSurfaceId) : null;
        var basisMapping = basis?.OutputMappings.Find(m => m.OutputId == outputId);

        var rToView = _identity;
        var rToOutput = _identity;
        var viewMin = Vector2.Zero;
        var viewSize = canvasSize;

        if (basisMapping != null && basisMapping.Quad.Length >= 4)
        {
            var basisQuad = _dragOldQuad != null && _dragSurfaceId == _focusedSurfaceId ? _dragOldQuad : basisMapping.Quad;
            Bounds(basisQuad, out var quadMin, out var quadMax);
            var pivot = basis!.Placement?.Pivot ?? Vector2.Zero;

            // Straightening lands on the surface's real content canvas (metres × px/m) — so Size (m) is what
            // gives the rectangle its aspect. Anchored at the pivot, so changing a dimension extends the rect
            // from there rather than recentring it.
            var straightSize = new Vector2(MathF.Max(basis.SizeInMeters.X, 0.001f),
                                           MathF.Max(basis.SizeInMeters.Y, 0.001f)) * MathF.Max(basis.PixelsPerMeter, 1f);
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

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            var mappingData = surface.OutputMappings.Find(m => m.OutputId == outputId);
            if (mappingData == null)
                continue;

            // The quad in view space: R applied, then offset into the framed region.
            var viewQuad = new[]
                               {
                                   rToView.TransformPoint(mappingData.Quad[0]) - viewMin,
                                   rToView.TransformPoint(mappingData.Quad[1]) - viewMin,
                                   rToView.TransformPoint(mappingData.Quad[2]) - viewMin,
                                   rToView.TransformPoint(mappingData.Quad[3]) - viewMin,
                               };

            ImGui.PushID(surface.Id.GetHashCode());
            // The checker is only a stand-in; drop it once the real composite fills the surface.
            var style = CornerPinHandles.Style.ForSurface(surface.Name, editable: editable);
            style.DrawChecker = !hasContent;
            if (surface.Id == _focusedSurfaceId)
                style.EdgeColor = UiColors.ForegroundFull;

            if (handleFade < 1f)
            {
                style.EdgeColor = style.EdgeColor.Fade(handleFade);
                style.HandleColor = style.HandleColor.Fade(handleFade);
                style.TopLeftColor = style.TopLeftColor.Fade(handleFade);
                style.LabelColor = style.LabelColor.Fade(handleFade);
                style.CheckerColor = style.CheckerColor.Fade(handleFade);
            }

            var phase = CornerPinHandles.Draw(viewQuad, _projection, style, out _);

            // Map the (possibly edited) view-space quad back to projector space. A no-op at rest / t=0.
            for (var c = 0; c < 4; c++)
                mappingData.Quad[c] = rToOutput.TransformPoint(viewQuad[c] + viewMin);

            HandleDrag(phase, surface.Id, outputId, mappingData.Quad);
            ImGui.PopID();
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

    private void DrawHeader(Setup setup, OutputDefinition output, Guid outputId)
    {
        CustomComponents.StylizedText($"{output.Name} · {output.CanvasResolution.Width}×{output.CanvasResolution.Height}",
                                      Fonts.FontSmall, UiColors.TextMuted);

        ImGui.SameLine();
        if (CustomComponents.StateButton("Output", ModeButtonState(EditMode.Output)))
            _editMode = EditMode.Output;

        // Straightening rectifies a single surface, so only offer it when one mapped to this output is focused.
        if (_focusedSurfaceId != default
            && setup.Surfaces.Exists(s => s.Id == _focusedSurfaceId && s.OutputMappings.Exists(m => m.OutputId == outputId)))
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
}
