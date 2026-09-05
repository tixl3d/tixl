#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;
using Int2 = T3.Core.DataTypes.Vector.Int2;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The reference image's space: the photo or plan inside its Board card, with the surfaces traced on it as
/// corner-pin quads (in image pixels) and the Photo ↔ Straight morph that rectifies the photo around the
/// selected traced surface. Entered from the image card by double-click, left through the Board button; the
/// Board itself always shows the traced quads on the card, read-only.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>The image's space, folded out of the Board like an output's canvas.</summary>
    public void DrawReferenceCanvas(Guid imageId, SetupEntitySelection? selection)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
            return;

        var image = setup.FindReferenceImage(imageId);
        if (image == null)
            return;

        var subject = FindStraightenSubject(setup, imageId, selection);
        DrawReferenceHeader(image, subject);

        var canvasTop = ImGui.GetCursorScreenPos();
        _boardCanvas.UpdateCanvas(out _);
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(canvasTop, ImGui.GetWindowPos() + ImGui.GetWindowSize(), true);

        SeedBoardPlacements(setup);
        var texture = TryGetReferenceTexture(image);
        EnterSpace(setup, SetupEntitySelection.EntityKind.ReferenceImage, imageId, texture != null);
        DrawBoardLayer(setup, machineConfig, selection);

        if (texture == null)
            CustomComponents.EmptyWindowMessage("No image yet — pick one in the Parameter window,\nor drop a photo onto the Board.");
        else if (_spaceBlend > 0.001f)
            DrawReferenceSpace(setup, image, texture, subject, selection);

        ResolvePicking(setup, selection);
        dl.PopClipRect();
    }

    /// <summary>The image a surface is traced on, if any.</summary>
    private static ReferenceImage? TracedImageOf(Setup setup, Guid surfaceId)
    {
        var binding = setup.FindSurface(surfaceId)?.Reference;
        return binding == null ? null : setup.FindReferenceImage(binding.ImageId);
    }

    /// <summary>
    /// The image space entered through the header's Straight tab (the shown surface is the subject) or
    /// fading out after it — as opposed to <see cref="DrawReferenceCanvas"/>, the image's own entry point.
    /// </summary>
    private void DrawReferenceSpaceForShown(Setup setup, SetupEntitySelection? selection, bool straighten)
    {
        var image = setup.FindReferenceImage(_spaceId);
        if (image == null)
            return;

        var texture = TryGetReferenceTexture(image);
        if (texture == null)
        {
            CustomComponents.EmptyWindowMessage($"{image.Name} has no image to straighten on — pick one in the Parameter window.");
            return;
        }

        var subject = setup.FindSurface(_shownSurfaceId);
        if (subject?.Reference?.ImageId != image.Id)
            subject = null;

        SetReferenceStraightenTarget(straighten && subject != null ? 1f : 0f);
        DrawReferenceSpace(setup, image, texture, subject, selection);
    }

    /// <summary>Starts the Photo ↔ Straight transition (camera included) when the target changes.</summary>
    private void SetReferenceStraightenTarget(float target)
    {
        if (target == _referenceStraightenTarget)
            return;

        _referenceStraightenTarget = target;
        _referenceStraightenFrom = _referenceStraighten;
        _referenceProgress = 0f;
        CaptureTransitionStart();
    }

    /// <summary>Board button · name · kind, and the Photo / Straight toggle once a traced surface is the subject.</summary>
    private void DrawReferenceHeader(ReferenceImage image, Surface? subject)
    {
        DrawBoardReturnHeader($"{image.Name} · {image.Kind}");

        if (subject == null)
        {
            SetReferenceStraightenTarget(0f);
            return;
        }

        ImGui.SameLine(0, 12 * T3Ui.UiScaleFactor);
        if (CustomComponents.StateButton("Photo", _referenceStraightenTarget < 0.5f ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
            SetReferenceStraightenTarget(0f);

        ImGui.SameLine();
        if (CustomComponents.StateButton("Straight", _referenceStraightenTarget >= 0.5f ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
            SetReferenceStraightenTarget(1f);

        ImGui.SameLine(0, 12 * T3Ui.UiScaleFactor);
        ImGui.AlignTextToFramePadding();
        CustomComponents.StylizedText(subject.Name, Fonts.FontSmall, UiColors.TextMuted);
    }

    /// <summary>
    /// The photo at its pixel size with the traced quads and their handles — or, while straightening, the photo
    /// warped in place so the subject's corners approach an upright rectangle: its outline follows the corners,
    /// its label and the photo's own frame fade, and the camera settles on the rectified region.
    /// </summary>
    private void DrawReferenceSpace(Setup setup, ReferenceImage image, Texture2D texture, Surface? subject, SetupEntitySelection? selection)
    {
        var scale = T3Ui.UiScaleFactor;
        var size = new Vector2(Math.Max(1, image.Width), Math.Max(1, image.Height));

        if (_referenceProgress < 1f)
        {
            var dt = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.1f);
            _referenceProgress = MathF.Min(1f, _referenceProgress + dt / _morphDuration);
            var eased = MathF.Pow(_referenceProgress, _morphEaseExponent);
            _referenceStraighten = _referenceProgress >= 1f
                                       ? _referenceStraightenTarget
                                       : _referenceStraightenFrom + (_referenceStraightenTarget - _referenceStraightenFrom) * eased;
        }

        // The subject's traced quad and its target rectangle — eased from the previous subject's when the
        // selection moves between surfaces on this photo while straightened, so the scene turns rather than jumps.
        var hasSubject = subject?.Reference != null && subject.Reference.Quad.Length >= 4;
        Vector2 targetMin = Vector2.Zero, targetMax = Vector2.Zero;
        if (hasSubject)
            ResolveStraightSubject(subject!, out targetMin, out targetMax);

        // The camera settles on the rectified region with its surround, or on the whole photo.
        var settledMin = Vector2.Zero;
        var settledMax = size;
        if (hasSubject && _referenceStraightenTarget >= 0.5f)
        {
            var span = targetMax - targetMin;
            var surround = new Vector2(MathF.Max(span.X, span.Y) * _straightSurroundFactor);
            settledMin = targetMin - surround;
            settledMax = targetMax + surround;
        }

        var topLeft = _projection.CanvasToBoard(settledMin);
        var bottomRight = _projection.CanvasToBoard(settledMax);
        FitToBoardRect(new Vector2(topLeft.X, bottomRight.Y), new Vector2(bottomRight.X, topLeft.Y), EditMode.Straight, image.Id);

        var dl = ImGui.GetWindowDrawList();
        var t = _referenceStraighten;
        if (hasSubject && t > 0.001f
            && TryRenderStraightened(image, _referenceSubjectQuad, targetMin, targetMax, texture, t,
                                     image.Id, 4096f,
                                     out var warped, out var bboxMin, out var bboxMax, out var regionMin, out var regionMax)
            && warped is { IsDisposed: false })
        {
            var srv = SrvManager.GetSrvForTexture(warped);
            if (srv is { IsDisposed: false })
            {
                // The full warped photo at its true (un-clipped) extent, dimming as it rectifies; the surface
                // region at full opacity so the focus reads as the straightened wall.
                var sMin = _projection.CanvasToScreen(bboxMin);
                var sMax = _projection.CanvasToScreen(bboxMax);
                dl.AddImage(srv.NativePointer, sMin, sMax, Vector2.Zero, Vector2.One, UiColors.ForegroundFull.Fade(1f - 0.8f * t));

                var rMin = _projection.CanvasToScreen(regionMin);
                var rMax = _projection.CanvasToScreen(regionMax);
                dl.PushClipRect(rMin, rMax, true);
                dl.AddImage(srv.NativePointer, sMin, sMax);
                dl.PopClipRect();
            }

            // The photo's own frame, its corners pushed outward by the warp, fading with it.
            Span<Vector2> screenQuad = stackalloc Vector2[4];
            for (var c = 0; c < 4; c++)
                screenQuad[c] = _projection.CanvasToScreen(_referencePhotoQuad[c]);

            dl.AddQuad(screenQuad[0], screenQuad[1], screenQuad[2], screenQuad[3], UiColors.ForegroundFull.Fade(0.25f * (1f - t)), 1 * scale);

            // The subject's corners on their way to an upright rectangle; the label fades out with the photo.
            for (var c = 0; c < 4; c++)
                screenQuad[c] = _projection.CanvasToScreen(_referenceInterpQuad[c]);

            dl.AddQuad(screenQuad[0], screenQuad[1], screenQuad[2], screenQuad[3], SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface), 2 * scale);
            DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, screenQuad, subject!.Id, subject.Name, true, 1f - t);

            _probeSurfaceCentre = (screenQuad[0] + screenQuad[2]) * 0.5f;
            SampleTransitionMetrics();

            // Settled: the rectified rect is editable (corners and edges refine the trace through the frozen
            // rectification), and the measuring lines live on it.
            var settled = _referenceProgress >= 1f && _referenceSubjectProgress >= 1f && _spaceBlend >= 1f && t >= 0.999f;
            if (settled || _referenceEditActive)
                DrawStraightEdits(setup, dl, subject!, targetMin, targetMax);

            return;
        }

        var min = _projection.CanvasToScreen(Vector2.Zero);
        var max = _projection.CanvasToScreen(size);
        dl.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.4f));
        var photoSrv = SrvManager.GetSrvForTexture(texture);
        if (photoSrv is { IsDisposed: false })
            dl.AddImage(photoSrv.NativePointer, min, max);

        dl.AddRect(min, max, UiColors.ForegroundFull.Fade(0.25f));

        DrawTracedQuads(setup, image, selection, dl, _spaceBlend >= 1f && _referenceProgress >= 1f, 1f);
    }

    /// <summary>
    /// The surfaces traced on an image as corner-pin quads in photo pixels, through the current projection
    /// (the image's space, or its card on the Board). Editable quads get live handles; a drag is one undo
    /// step through the setup snapshot, like every Board gesture, and selects its surface.
    /// </summary>
    private void DrawTracedQuads(Setup setup, ReferenceImage image, SetupEntitySelection? selection, ImDrawListPtr dl, bool editable, float fade)
    {
        var imageSelected = selection?.IsSelected(SetupEntitySelection.EntityKind.ReferenceImage, image.Id) ?? false;
        Span<Vector2> screenQuad = stackalloc Vector2[4];
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            var binding = surface.Reference;
            if (binding == null || binding.ImageId != image.Id || binding.Quad.Length < 4)
                continue;

            var isSelected = selection?.IsSelected(SetupEntitySelection.EntityKind.Surface, surface.Id) ?? false;
            var pulse = isSelected ? 0f : FrameStats.GetPulse(surface.Id);

            // What the surface shows, laid into its trace — the wall with its content, as it will be. At the
            // preview opacity, so the photo stays the reference; per-triangle, which is close enough for a preview.
            var preview = UserSettings.Config.OutputSetupContentPreview;
            if (preview > 0.01f && OutputManager.TryGetSurfaceSlice(surface.Id, out _, out var content, out var uv) && content is { IsDisposed: false })
            {
                var contentSrv = SrvManager.GetSrvForTexture(content);
                if (contentSrv is { IsDisposed: false })
                {
                    for (var c = 0; c < 4; c++)
                        screenQuad[c] = _projection.CanvasToScreen(binding.Quad[c]);

                    dl.AddImageQuad(contentSrv.NativePointer, screenQuad[0], screenQuad[1], screenQuad[2], screenQuad[3],
                                    new Vector2(uv.X, uv.Y), new Vector2(uv.Z, uv.Y), new Vector2(uv.Z, uv.W), new Vector2(uv.X, uv.W),
                                    UiColors.ForegroundFull.Fade(preview * fade));
                }
            }

            ImGui.PushID(surface.Id.GetHashCode());
            var style = CornerPinHandles.Style.ForSurface(null, editable && (isSelected || imageSelected), isSelected, fade);
            style.DrawChecker = false;
            style.EdgeColor = PulseColor(SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface).Fade(isSelected ? 1f : 0.7f), pulse).Fade(fade);

            var phase = CornerPinHandles.Draw(binding.Quad, _projection, style, out _);
            if (phase == CanvasPointHandle.DragPhase.Started)
            {
                _boardGestureOldJson = setup.ToJsonString();
                selection?.Select(SetupEntitySelection.EntityKind.Surface, surface.Id);
            }
            else if (phase == CanvasPointHandle.DragPhase.Completed)
            {
                CommitBoardGesture(setup, "Trace surface");
            }

            ImGui.PopID();

            for (var c = 0; c < 4; c++)
                screenQuad[c] = _projection.CanvasToScreen(binding.Quad[c]);

            DrawEntityLabel(dl, SetupEntitySelection.EntityKind.Surface, screenQuad, surface.Id, surface.Name, isSelected, fade, pulse);
        }
    }

    /// <summary>
    /// The traced quads on an image card on the Board, through a projection pointed at the card. On the settled
    /// Board they are as editable as in the image's space; while a space fades the layer they are only drawn.
    /// </summary>
    private void DrawBoardTraces(Setup setup, SetupEntitySelection? selection, ImDrawListPtr dl, ReferenceImage image, Vector2 min, Vector2 max)
    {
        var fade = _boardLayerFade;
        var pixelSize = BoardPixelSize(setup, SetupEntitySelection.EntityKind.ReferenceImage, image.Id);
        _projection.Origin = new Vector2(min.X, max.Y);
        _projection.PixelsPerMeter = pixelSize.X / MathF.Max(max.X - min.X, 0.0001f);
        DrawTracedQuads(setup, image, selection, dl, fade >= 0.999f, fade);
    }

    /// <summary>The surface the Straight toggle rectifies around: the primary selection, when it is traced on this image.</summary>
    private static Surface? FindStraightenSubject(Setup setup, Guid imageId, SetupEntitySelection? selection)
    {
        if (selection == null || !selection.TryResolve(setup, out var kind, out var id) || kind != SetupEntitySelection.EntityKind.Surface)
            return null;

        var surface = setup.FindSurface(id);
        return surface?.Reference?.ImageId == imageId ? surface : null;
    }

    /// <summary>
    /// Warps the photo so the surface's traced quad rectifies (to its bounding box), interpolated by
    /// <paramref name="t"/>. Out: the warped texture, the warped-photo extent, and the surface-region extent —
    /// all in photo pixels. Also leaves the interpolated quad in <see cref="_referenceInterpQuad"/> and the
    /// photo's warped corners in <see cref="_referencePhotoQuad"/>.
    /// </summary>
    private static bool TryRenderStraightened(ReferenceImage image, Vector2[] quad, Vector2 targetMin, Vector2 targetMax, Texture2D texture, float t,
                                              Guid targetKey, float maxDimension,
                                              out Texture2D? warped, out Vector2 bboxMin, out Vector2 bboxMax, out Vector2 regionMin, out Vector2 regionMax)
    {
        warped = null;
        bboxMin = bboxMax = regionMin = regionMax = Vector2.Zero;
        var w = Math.Max(1, image.Width);
        var h = Math.Max(1, image.Height);
        var targetRect = RectCorners(targetMin, targetMax);

        var interp = _referenceInterpQuad;
        for (var i = 0; i < 4; i++)
            interp[i] = Vector2.Lerp(quad[i], targetRect[i], t);

        if (!Homography.TryComputeQuadToQuad(quad, interp, out var homography))
            return false;

        var dest = _referencePhotoQuad;
        dest[0] = homography.TransformPoint(new Vector2(0, 0));
        dest[1] = homography.TransformPoint(new Vector2(w, 0));
        dest[2] = homography.TransformPoint(new Vector2(w, h));
        dest[3] = homography.TransformPoint(new Vector2(0, h));

        regionMin = regionMax = interp[0];
        for (var i = 1; i < 4; i++)
        {
            regionMin = Vector2.Min(regionMin, interp[i]);
            regionMax = Vector2.Max(regionMax, interp[i]);
        }

        // Extent = the rectified surface plus a margin of surround, so it isn't clipped to the photo rect.
        // Bounding to the region — not the whole warped photo — is essential: a steep rectification sends the
        // far photo corners toward infinity, so sizing to the full warp would blow the RT up and leave the
        // surface sub-pixel (it vanishes). The runaway surround is simply cropped to this window.
        var regionSize = regionMax - regionMin;
        bboxMin = regionMin - regionSize;
        bboxMax = regionMax + regionSize;

        var bboxSize = bboxMax - bboxMin;
        var maxDim = Math.Max(bboxSize.X, bboxSize.Y);
        var renderScale = maxDim > maxDimension ? maxDimension / maxDim : 1f;
        var rtSize = new Int2(Math.Max(1, (int)(bboxSize.X * renderScale)), Math.Max(1, (int)(bboxSize.Y * renderScale)));

        _referenceWarpDest[0] = (dest[0] - bboxMin) * renderScale;
        _referenceWarpDest[1] = (dest[1] - bboxMin) * renderScale;
        _referenceWarpDest[2] = (dest[2] - bboxMin) * renderScale;
        _referenceWarpDest[3] = (dest[3] - bboxMin) * renderScale;

        warped = OutputManager.RenderWarpedTexture(texture, _referenceWarpDest, rtSize, targetKey);
        return warped is { IsDisposed: false };
    }

    /// <summary>
    /// The quad and target the straighten works on this frame. A change of subject while straightened eases
    /// both from the previous subject's (as last rendered) to the new one's, camera included; the first subject,
    /// or one picked while on the photo, snaps — there is nothing to turn from.
    /// </summary>
    private void ResolveStraightSubject(Surface subject, out Vector2 targetMin, out Vector2 targetMax)
    {
        var quad = subject.Reference!.Quad;

        // The rect is where the wall was first put upright; refining the trace must not move or re-centre it.
        // It is re-derived only for a new subject or a changed physical size (which changes its aspect).
        if (subject.Id != _referenceSubjectId || subject.SizeInMeters != _referenceStickySize)
        {
            StraightTargetBounds(subject, out _referenceStickyMin, out _referenceStickyMax);
            _referenceStickySize = subject.SizeInMeters;
        }

        targetMin = _referenceStickyMin;
        targetMax = _referenceStickyMax;

        if (subject.Id != _referenceSubjectId)
        {
            if (_referenceSubjectId != Guid.Empty && _referenceStraighten > 0.001f)
            {
                Array.Copy(_referenceSubjectQuad, _referenceSubjectFromQuad, 4);
                _referenceSubjectFromMin = _referenceSubjectLastMin;
                _referenceSubjectFromMax = _referenceSubjectLastMax;
                _referenceSubjectProgress = 0f;
                CaptureTransitionStart();
            }
            else
            {
                _referenceSubjectProgress = 1f;
            }

            _referenceSubjectId = subject.Id;
        }

        if (_referenceSubjectProgress < 1f)
        {
            var dt = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.1f);
            _referenceSubjectProgress = MathF.Min(1f, _referenceSubjectProgress + dt / _morphDuration);
            var eased = MathF.Pow(_referenceSubjectProgress, _morphEaseExponent);
            for (var i = 0; i < 4; i++)
                _referenceSubjectQuad[i] = Vector2.Lerp(_referenceSubjectFromQuad[i], quad[i], eased);

            targetMin = Vector2.Lerp(_referenceSubjectFromMin, targetMin, eased);
            targetMax = Vector2.Lerp(_referenceSubjectFromMax, targetMax, eased);
        }
        else
        {
            Array.Copy(quad, _referenceSubjectQuad, 4);
        }

        _referenceSubjectLastMin = targetMin;
        _referenceSubjectLastMax = targetMax;
    }

    /// <summary>
    /// Handles on the rectified rect. The rect stays fixed and upright; dragging a corner or an edge moves the
    /// traced quad live so the photo re-warps under it — you pull the wall's corner (or edge) into the frame.
    /// The mapping from handle to photo is the rectification at press time, so the drag can't chase its own
    /// re-warp; on release nothing moves. One undo step per drag. The surface's measuring lines are drawn and
    /// edited here too, mapped from surface metres onto the rect.
    /// </summary>
    private void DrawStraightEdits(Setup setup, ImDrawListPtr dl, Surface subject, Vector2 targetMin, Vector2 targetMax)
    {
        var binding = subject.Reference!;
        var rect = RectCorners(targetMin, targetMax);
        Array.Copy(rect, _referenceRectQuad, 4);
        if (!_referenceEditActive && !Homography.TryComputeQuadToQuad(rect, binding.Quad, out _referenceEditToPhoto))
            return;

        ImGui.PushID("straightEdit");
        var style = CornerPinHandles.Style.ForSurface(null, editable: true, selected: true);
        style.DrawChecker = false;
        style.EdgeColor = SetupColors.ForKind(SetupEntitySelection.EntityKind.Surface);
        var cornerPhase = CornerPinHandles.Draw(_referenceRectQuad, _projection, style, out var draggedCorner);
        var edgePhase = CanvasPointHandle.DragPhase.None;
        var edge = -1;
        var edgePos = Vector2.Zero;
        if (cornerPhase == CanvasPointHandle.DragPhase.None)
            edgePhase = CornerPinHandles.DrawEdgeHandles(_referenceRectQuad, _projection, style, out edge, out edgePos);

        ImGui.PopID();

        // An edge moves along its normal only: a crop of the trace, axis-aligned on the rectified wall.
        if (edge >= 0 && edgePhase != CanvasPointHandle.DragPhase.None)
        {
            switch (edge)
            {
                case 0: _referenceRectQuad[0].Y = _referenceRectQuad[1].Y = edgePos.Y; break;
                case 1: _referenceRectQuad[1].X = _referenceRectQuad[2].X = edgePos.X; break;
                case 2: _referenceRectQuad[2].Y = _referenceRectQuad[3].Y = edgePos.Y; break;
                default: _referenceRectQuad[3].X = _referenceRectQuad[0].X = edgePos.X; break;
            }
        }

        var phase = cornerPhase != CanvasPointHandle.DragPhase.None ? cornerPhase : edgePhase;
        if (phase == CanvasPointHandle.DragPhase.Started)
        {
            _referenceEditActive = true;
            _boardGestureOldJson = setup.ToJsonString();
        }

        // Only a live phase carries a handle position; on the release frame the handles already sit back on the
        // rect's corners, so applying then would undo the whole drag.
        if (phase is CanvasPointHandle.DragPhase.Started or CanvasPointHandle.DragPhase.Dragging && _referenceEditActive)
        {
            // The handle's position through the press-time rectification is where that corner lies in the photo.
            if (draggedCorner >= 0)
                binding.Quad[draggedCorner] = _referenceEditToPhoto.TransformPoint(_referenceRectQuad[draggedCorner]);
            else if (edge >= 0)
            {
                var a = edge;
                var b = (edge + 1) % 4;
                binding.Quad[a] = _referenceEditToPhoto.TransformPoint(_referenceRectQuad[a]);
                binding.Quad[b] = _referenceEditToPhoto.TransformPoint(_referenceRectQuad[b]);
            }
        }

        if (phase == CanvasPointHandle.DragPhase.Completed)
        {
            _referenceEditActive = false;
            CommitBoardGesture(setup, "Refine trace");
        }

        // Measuring lines: surface metres ↔ the rectified rect, a plain scale (Y up in metres, down in px).
        if (!_referenceEditActive
            && Homography.TryComputeQuadToQuad(SurfaceGeometry.LocalRect(subject), rect, out var surfaceToRect)
            && Homography.TryComputeQuadToQuad(rect, SurfaceGeometry.LocalRect(subject), out var rectToSurface))
        {
            DrawAnnotations(dl, subject, surfaceToRect, rectToSurface, Vector2.Zero, editable: true, fade: 1f, projected: false);
        }
    }

    /// <summary>
    /// The straightened crop of the photo a traced surface stands for, for its Board card: the warp rendered
    /// small into the surface's own target, and the uv window of the rectified region inside it.
    /// </summary>
    private bool TryGetTracedFragment(Setup setup, Surface surface, out SharpDX.Direct3D11.ShaderResourceView? srv, out Vector2 uvMin, out Vector2 uvMax)
    {
        srv = null;
        uvMin = Vector2.Zero;
        uvMax = Vector2.One;
        var binding = surface.Reference;
        if (binding == null || binding.Quad.Length < 4)
            return false;

        var image = setup.FindReferenceImage(binding.ImageId);
        var texture = image == null ? null : TryGetReferenceTexture(image);
        if (image == null || texture == null)
            return false;

        StraightTargetBounds(surface, out var targetMin, out var targetMax);
        if (!TryRenderStraightened(image, binding.Quad, targetMin, targetMax, texture, 1f, surface.Id, 1024f,
                                   out var warped, out var bboxMin, out var bboxMax, out var regionMin, out var regionMax)
            || warped is not { IsDisposed: false })
            return false;

        srv = SrvManager.GetSrvForTexture(warped);
        if (srv is not { IsDisposed: false })
            return false;

        var bboxSize = Vector2.Max(bboxMax - bboxMin, new Vector2(0.001f));
        uvMin = (regionMin - bboxMin) / bboxSize;
        uvMax = (regionMax - bboxMin) / bboxSize;
        return true;
    }

    /// <summary>
    /// The rectangle a traced quad straightens to, in photo px: the quad's bounding box's width and centre,
    /// with the surface's own aspect — the wall is as wide as it was traced, and as tall as it really is.
    /// </summary>
    private static void StraightTargetBounds(Surface surface, out Vector2 min, out Vector2 max)
    {
        Bounds(surface.Reference!.Quad, out var quadMin, out var quadMax);
        var width = MathF.Max(quadMax.X - quadMin.X, 1f);
        var aspect = surface.SizeInMeters.X / MathF.Max(surface.SizeInMeters.Y, 0.0001f);
        var height = width / MathF.Max(aspect, 0.0001f);
        var centre = (quadMin + quadMax) * 0.5f;
        min = centre - new Vector2(width, height) * 0.5f;
        max = centre + new Vector2(width, height) * 0.5f;
    }

    /// <summary>The loaded image texture (cached per path), its pixel size synced onto the entity.</summary>
    private Texture2D? TryGetReferenceTexture(ReferenceImage image)
    {
        if (string.IsNullOrWhiteSpace(image.FilePath))
            return null;

        if (!_boardRefTextures.TryGetValue(image.Id, out var entry) || entry.Path != image.FilePath)
        {
            entry = new ReferenceTextureEntry(image.FilePath, ResourceManager.CreateTextureResource(image.FilePath, null));
            _boardRefTextures[image.Id] = entry;
        }

        _boardContext ??= new EvaluationContext();
        var texture = entry.Resource.GetValue(_boardContext);
        if (texture is not { IsDisposed: false })
        {
            // Once per path: a reference that doesn't resolve is worth a line, not a line per frame.
            if (!entry.WarnedMissing)
            {
                entry.WarnedMissing = true;
                T3.Core.Logging.Log.Warning($"Reference image '{image.Name}' can't load '{image.FilePath}'.");
            }

            return null;
        }

        // The bitmap loader allocates a full mip chain but fills only level 0 — the coarse levels are garbage
        // until regenerated. Without this, the oblique straighten warp minifies into the corrupt mips and
        // bands. Once per load.
        if (!entry.MipsGenerated)
        {
            var srv = SrvManager.GetSrvForTexture(texture);
            if (srv is { IsDisposed: false })
            {
                ResourceManager.Device.ImmediateContext.GenerateMips(srv);
                entry.MipsGenerated = true;
            }
        }

        // The stored pixel size is what traces and measurements are in, and what the card and its metadata
        // show — keep it in step with the loaded texture (persisted with the next save).
        if (image.Width != texture.Description.Width || image.Height != texture.Description.Height)
        {
            image.Width = texture.Description.Width;
            image.Height = texture.Description.Height;
            _boardMetaVersion = -1;
        }

        return texture;
    }

    private SharpDX.Direct3D11.ShaderResourceView? TryGetReferenceSrv(ReferenceImage image)
    {
        var texture = TryGetReferenceTexture(image);
        return texture == null ? null : SrvManager.GetSrvForTexture(texture);
    }

    private sealed class ReferenceTextureEntry(string path, Resource<Texture2D> resource)
    {
        public readonly string Path = path;
        public readonly Resource<Texture2D> Resource = resource;
        public bool MipsGenerated;
        public bool WarnedMissing;
    }

    // Photo ↔ Straight morph of the reference space (0 = photo, 1 = rectified around the subject), eased like
    // the view morph; the camera transition follows _referenceProgress.
    private float _referenceStraightenTarget;
    private float _referenceStraighten;
    private float _referenceStraightenFrom;
    private float _referenceProgress = 1f;

    // A live handle drag on the rectified rect: the press-time rect→photo mapping, and the handle positions.
    private bool _referenceEditActive;
    private Homography _referenceEditToPhoto;
    private readonly Vector2[] _referenceRectQuad = new Vector2[4];

    // The rect the subject is put upright into — sticky across trace edits (see ResolveStraightSubject).
    private Vector2 _referenceStickyMin, _referenceStickyMax, _referenceStickySize;

    // Subject transition: the quad/target in use (eased between surfaces), where the ease started, and its progress.
    private Guid _referenceSubjectId;
    private float _referenceSubjectProgress = 1f;
    private readonly Vector2[] _referenceSubjectQuad = new Vector2[4];
    private readonly Vector2[] _referenceSubjectFromQuad = new Vector2[4];
    private Vector2 _referenceSubjectFromMin, _referenceSubjectFromMax, _referenceSubjectLastMin, _referenceSubjectLastMax;
    private static readonly Vector2[] _referenceWarpDest = new Vector2[4];
    private static readonly Vector2[] _referenceInterpQuad = new Vector2[4];
    private static readonly Vector2[] _referencePhotoQuad = new Vector2[4];
    private readonly Dictionary<Guid, ReferenceTextureEntry> _boardRefTextures = new();
}
