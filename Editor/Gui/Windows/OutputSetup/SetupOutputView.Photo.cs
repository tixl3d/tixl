#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Int2 = T3.Core.DataTypes.Vector.Int2;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// The reference image's space: the photo or plan inside its Board card, and the Photo ↔ Straight morph that
/// rectifies the photo around the selected traced surface — its subject transition, its warp render, and the
/// texture cache behind every photo card. Entered from the image card by double-click, left through the
/// Board button. The traced quads and their edits are <c>SetupOutputView.Trace.cs</c>.
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
        if (!DeferHeader(HeaderKinds.Reference, imageId: imageId, subjectId: subject?.Id ?? Guid.Empty))
            DrawReferenceHeader(image, subject);

        var canvasTop = ImGui.GetCursorScreenPos();
        _boardCanvas.UpdateCanvas(out _);
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(canvasTop, ImGui.GetWindowPos() + ImGui.GetWindowSize(), true);

        SeedBoardPlacements(setup);
        var texture = TryGetReferenceTexture(image);
        if (texture != null)
            EnterSpace(setup, SetupEntityKinds.ReferenceImage, imageId);
        else
            LeaveSpace(setup);

        DrawBoardLayer(setup, machineConfig, selection);

        if (texture == null)
            CustomComponents.EmptyWindowMessage("No image yet — pick one in the Parameter window,\nor drop a photo onto the Board.");
        else if (_spaceBlend.Value > 0.001f)
            DrawReferenceSpace(setup, image, texture, subject, selection);

        ResolvePicking(setup, selection);
        dl.PopClipRect();
    }

    /// <summary>The image a surface is traced on, if any — a region through the traced ancestor it lives in.</summary>
    private static ReferenceImage? TracedImageOf(Setup setup, Guid surfaceId)
    {
        var binding = FindTraceCarrier(setup, surfaceId)?.Trace;
        return binding == null ? null : setup.FindReferenceImage(binding.ImageId);
    }

    /// <summary>The surface itself when traced, else the nearest traced ancestor (a region rides its parent's photo).</summary>
    private static Surface? FindTraceCarrier(Setup setup, Guid surfaceId)
    {
        var surface = setup.FindSurface(surfaceId);
        for (var guard = 0; surface != null && guard < 16; guard++)
        {
            if (surface.Trace != null)
                return surface;

            if (surface.ParentId == Guid.Empty)
                break;

            var parentId = surface.ParentId;
            surface = setup.FindSurface(parentId);
        }

        return null;
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

        var subject = FindTraceCarrier(setup, _shownSurfaceId);
        if (subject?.Trace?.ImageId != image.Id)
            subject = null;

        SetReferenceStraightenTarget(straighten && subject != null ? 1f : 0f);
        DrawReferenceSpace(setup, image, texture, subject, selection);
    }

    /// <summary>Starts the Photo ↔ Straight transition (camera included) when the target changes.</summary>
    private void SetReferenceStraightenTarget(float target)
    {
        if (_referenceStraighten.Retarget(target))
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
        if (CustomComponents.StateButton("Photo", _referenceStraighten.Target < 0.5f ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
            SetReferenceStraightenTarget(0f);

        ImGui.SameLine();
        if (CustomComponents.StateButton("Straight", _referenceStraighten.Target >= 0.5f ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
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

        _referenceStraighten.Advance(FrameDeltaSec(), MorphDurationSec, MorphEaseExponent);

        // The subject's traced quad and its target rectangle — eased from the previous subject's when the
        // selection moves between surfaces on this photo while straightened, so the scene turns rather than jumps.
        var hasSubject = subject?.Trace != null && subject.Trace.Quad.Length >= 4;
        Vector2 targetMin = Vector2.Zero, targetMax = Vector2.Zero;
        if (hasSubject)
            ResolveStraightSubject(subject!, out targetMin, out targetMax);

        // The camera settles on the rectified region with its surround, or on the whole photo.
        var settledMin = Vector2.Zero;
        var settledMax = size;
        if (hasSubject && _referenceStraighten.Target >= 0.5f)
        {
            var span = targetMax - targetMin;
            var surround = new Vector2(MathF.Max(span.X, span.Y) * RectifiedFraming.StraightSurroundFactor);
            settledMin = targetMin - surround;
            settledMax = targetMax + surround;
        }

        var topLeft = _projection.CanvasToBoard(settledMin);
        var bottomRight = _projection.CanvasToBoard(settledMax);
        FitToBoardRect(new Vector2(topLeft.X, bottomRight.Y), new Vector2(bottomRight.X, topLeft.Y), EditModes.Straight, image.Id);

        var dl = ImGui.GetWindowDrawList();
        var t = _referenceStraighten.Value;
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

            dl.AddQuad(screenQuad[0], screenQuad[1], screenQuad[2], screenQuad[3], SetupColors.ForKind(SetupEntityKinds.Surface), 2 * scale);
            DrawEntityLabel(dl, SetupEntityKinds.Surface, screenQuad, subject!.Id, subject.Name, true, 1f - t);

            // Settled: the rectified rect is editable (corners and edges refine the trace through the frozen
            // rectification), and the measuring lines live on it.
            var settled = _referenceStraighten.IsSettled && _referenceSubjectEase.IsSettled && _spaceBlend.Value >= 1f && t >= 0.999f;
            if (settled || _gesture.Kind == GestureKinds.TraceRefine)
                DrawStraightEdits(setup, dl, subject!, targetMin, targetMax, selection);

            return;
        }

        var min = _projection.CanvasToScreen(Vector2.Zero);
        var max = _projection.CanvasToScreen(size);
        dl.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.4f));
        var photoSrv = SrvManager.GetSrvForTexture(texture);
        if (photoSrv is { IsDisposed: false })
            dl.AddImage(photoSrv.NativePointer, min, max);

        dl.AddRect(min, max, UiColors.ForegroundFull.Fade(0.25f));

        DrawTracedQuads(setup, image, selection, dl, _spaceBlend.Value >= 1f && _referenceStraighten.IsSettled, 1f);
    }

    /// <summary>The surface the Straight toggle rectifies around: the primary selection, when it is traced on this image.</summary>
    private static Surface? FindStraightenSubject(Setup setup, Guid imageId, SetupEntitySelection? selection)
    {
        if (selection == null || !selection.TryResolve(setup, out var kind, out var id) || kind != SetupEntityKinds.Surface)
            return null;

        var surface = setup.FindSurface(id);
        return surface?.Trace?.ImageId == imageId ? surface : null;
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
        Span<Vector2> targetRect = stackalloc Vector2[4];
        SurfaceGeometry.WriteRectCorners(targetMin, targetMax, targetRect, yUp: false);

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

        CanvasDraw.Bounds(interp, out regionMin, out regionMax);

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

        warped = OutputCompositor.RenderWarpedTexture(texture, _referenceWarpDest, rtSize, targetKey);
        return warped is { IsDisposed: false };
    }

    /// <summary>
    /// The quad and target the straighten works on this frame. A change of subject while straightened eases
    /// both from the previous subject's (as last rendered) to the new one's, camera included; the first subject,
    /// or one picked while on the photo, snaps — there is nothing to turn from.
    /// </summary>
    private void ResolveStraightSubject(Surface subject, out Vector2 targetMin, out Vector2 targetMax)
    {
        var quad = subject.Trace!.Quad;

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
            // Only surfaces on the same photo can turn into each other: their quads share a pixel space. Across
            // photos the new subject snaps (the camera still travels, see EnterSpace).
            var sameImage = _referenceSubjectImageId == subject.Trace!.ImageId;
            _referenceSubjectImageId = subject.Trace.ImageId;
            if (_referenceSubjectId != Guid.Empty && _referenceStraighten.Value > 0.001f && sameImage)
            {
                Array.Copy(_referenceSubjectQuad, _referenceSubjectFromQuad, 4);
                _referenceSubjectFromMin = _referenceSubjectLastMin;
                _referenceSubjectFromMax = _referenceSubjectLastMax;
                _referenceSubjectEase.Restart();
                CaptureTransitionStart();
            }
            else
            {
                _referenceSubjectEase.Settle();
            }

            _referenceSubjectId = subject.Id;
        }

        if (!_referenceSubjectEase.IsSettled)
        {
            _referenceSubjectEase.Advance(FrameDeltaSec(), MorphDurationSec, MorphEaseExponent);
            var eased = _referenceSubjectEase.Value;
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
    /// The straightened crop of the photo a traced surface stands for, for its Board card: the warp rendered
    /// small into the surface's own target, and the uv window of the rectified region inside it.
    /// </summary>
    private bool TryGetTracedFragment(Setup setup, Surface surface, out SharpDX.Direct3D11.ShaderResourceView? srv, out Vector2 uvMin, out Vector2 uvMax)
    {
        return TryGetTracedFragment(setup, surface, out srv, out _, out uvMin, out uvMax);
    }

    private bool TryGetTracedFragment(Setup setup, Surface surface, out SharpDX.Direct3D11.ShaderResourceView? srv, out Texture2D? warpedTexture,
                                      out Vector2 uvMin, out Vector2 uvMax)
    {
        srv = null;
        warpedTexture = null;
        uvMin = Vector2.Zero;
        uvMax = Vector2.One;
        var binding = surface.Trace;
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

        warpedTexture = warped;
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
        CanvasDraw.Bounds(surface.Trace!.Quad, out var quadMin, out var quadMax);
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
        // until regenerated, and the oblique straighten warp minifies into them (ghosts of other content).
        // The resource can swap its texture object after loading, so this is keyed on the object, not the path.
        if (!ReferenceEquals(entry.MipsGeneratedFor, texture))
        {
            var srv = SrvManager.GetSrvForTexture(texture);
            if (srv is { IsDisposed: false })
            {
                ResourceManager.Device.ImmediateContext.GenerateMips(srv);
                entry.MipsGeneratedFor = texture;
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
        public Texture2D? MipsGeneratedFor; // the texture object whose mip chain was filled (held only as long as the resource)
        public bool WarnedMissing;
    }

    // Photo ↔ Straight morph of the reference space (0 = photo, 1 = rectified around the subject), eased like
    // the view morph; the camera transition follows its progress.
    private EasedValue _referenceStraighten = EasedValue.Settled(0f);

    // The rect the subject is put upright into — sticky across trace edits (see ResolveStraightSubject).
    private Vector2 _referenceStickyMin, _referenceStickyMax, _referenceStickySize;

    // Subject transition: the quad/target in use (eased between surfaces), where the ease started, and its progress.
    private Guid _referenceSubjectId;
    private Guid _referenceSubjectImageId;
    private EasedValue _referenceSubjectEase = EasedValue.Settled(1f);
    private readonly Vector2[] _referenceSubjectQuad = new Vector2[4];
    private readonly Vector2[] _referenceSubjectFromQuad = new Vector2[4];
    private Vector2 _referenceSubjectFromMin, _referenceSubjectFromMax, _referenceSubjectLastMin, _referenceSubjectLastMax;

    // Photo warp scratch, reused every frame.
    private static readonly Vector2[] _referenceWarpDest = new Vector2[4];
    private static readonly Vector2[] _referenceInterpQuad = new Vector2[4];
    private static readonly Vector2[] _referencePhotoQuad = new Vector2[4];

    // Loaded photo textures by image id, and the context that resolves their resources.
    private readonly Dictionary<Guid, ReferenceTextureEntry> _boardRefTextures = new();
    private EvaluationContext? _boardContext; // resolves the image resources; created on first use
}
