#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Int2 = T3.Core.DataTypes.Vector.Int2;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Shows a reference photo/plan on a pan/zoom canvas — the base for the reference-image workflow
/// (tracing surfaces, measuring, straightening). Loads the image through the normal resource system
/// (cached per path). One instance per output window.
/// </summary>
internal sealed class ReferenceImageView
{
    public ReferenceImageView()
    {
        _canvas.FillMode = ScalableCanvas.FillModes.FillAvailableContentRegion;
        _projection = new ScalableCanvasProjection(_canvas);
    }

    public void Draw(Guid imageId)
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var image = setup.FindReferenceImage(imageId);
        if (image == null)
            return;

        var activeSurface = GetSelectedTracedSurface(setup, imageId);
        DrawHeader(setup, image, imageId, activeSurface != null);

        // Ease the photo→straight transition (no visual jump).
        _straightenT += (_straightenTarget - _straightenT) * Math.Min(1f, ImGui.GetIO().DeltaTime * 8f);

        _canvas.UpdateCanvas(out _);

        var texture = LoadTexture(image);
        if (texture is not { IsDisposed: false })
        {
            CustomComponents.EmptyWindowMessage("Set a photo path above to load a reference image.");
            return;
        }

        // Keep the stored pixel size in sync — measurements and traces are in these pixels.
        image.Width = texture.Description.Width;
        image.Height = texture.Description.Height;

        var size = new Vector2(Math.Max(1, image.Width), Math.Max(1, image.Height));
        if (_fittedImageId != imageId)
        {
            _canvas.FitAreaOnCanvas(ImRect.RectWithSize(Vector2.Zero, size));
            _canvas.SetScopeInstant(_canvas.GetTargetScope());
            _fittedImageId = imageId;
        }

        var dl = ImGui.GetWindowDrawList();

        var straightening = activeSurface?.Reference != null && _straightenT > 0.001f;
        if (straightening
            && TryRenderStraightened(image, activeSurface!.Reference!, texture,
                                     out var warped, out var bboxMin, out var bboxMax, out var regionMin, out var regionMax)
            && warped is { IsDisposed: false })
        {
            var srv = SrvManager.GetSrvForTexture(warped);
            if (srv is { IsDisposed: false })
            {
                // Show the full warped photo at its true (un-clipped) extent, dimmed; the rectified surface
                // region at full opacity so the focus reads as the straightened surface.
                var sMin = _projection.CanvasToScreen(bboxMin);
                var sMax = _projection.CanvasToScreen(bboxMax);
                dl.AddImage(srv.NativePointer, sMin, sMax, Vector2.Zero, Vector2.One, (uint)UiColors.ForegroundFull.Fade(0.2f));

                var rMin = _projection.CanvasToScreen(regionMin);
                var rMax = _projection.CanvasToScreen(regionMax);
                dl.PushClipRect(rMin, rMax, true);
                dl.AddImage(srv.NativePointer, sMin, sMax);
                dl.PopClipRect();

                dl.AddRect(rMin, rMax, UiColors.ForegroundFull.Fade(0.5f));
            }

            return;
        }

        var min = _projection.CanvasToScreen(Vector2.Zero);
        var max = _projection.CanvasToScreen(size);
        dl.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.4f));

        var photoSrv = SrvManager.GetSrvForTexture(texture);
        if (photoSrv is { IsDisposed: false })
            dl.AddImage(photoSrv.NativePointer, min, max);

        dl.AddRect(min, max, UiColors.ForegroundFull.Fade(0.25f));

        // Traced surface outlines (draggable); the active surface is highlighted.
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            var binding = surface.Reference;
            if (binding == null || binding.ImageId != imageId || binding.Quad.Length < 4)
                continue;

            ImGui.PushID(surface.Id.GetHashCode());
            var style = CornerPinHandles.Style.ForSurface(surface.Name, editable: true);
            style.DrawChecker = false;
            if (surface.Id == _selectedSurfaceId)
                style.EdgeColor = UiColors.ForegroundFull;

            var phase = CornerPinHandles.Draw(binding.Quad, _projection, style, out _);
            if (phase == CanvasPointHandle.DragPhase.Completed)
                OutputSetupHandling.SaveActive();

            ImGui.PopID();
        }
    }

    // The traced surface the straighten targets: the selected one if valid, else the first traced (which it selects).
    private Surface? GetSelectedTracedSurface(Setup setup, Guid imageId)
    {
        Surface? selected = null;
        Surface? first = null;
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            if (surface.Reference?.ImageId != imageId)
                continue;

            first ??= surface;
            if (surface.Id == _selectedSurfaceId)
                selected = surface;
        }

        if (selected == null && first != null)
            _selectedSurfaceId = first.Id;

        return selected ?? first;
    }

    // Warps the photo so the surface's traced quad rectifies (to its bbox), interpolated by the transition.
    // Out: the warped texture, the warped-photo extent, and the surface-region extent — all in photo pixels.
    private bool TryRenderStraightened(ReferenceImage image, Surface.ReferenceBinding binding, Texture2D texture,
                                       out Texture2D? warped, out Vector2 bboxMin, out Vector2 bboxMax, out Vector2 regionMin, out Vector2 regionMax)
    {
        warped = null;
        bboxMin = bboxMax = regionMin = regionMax = Vector2.Zero;
        if (binding.Quad.Length < 4)
            return false;

        var w = Math.Max(1, image.Width);
        var h = Math.Max(1, image.Height);
        var targetRect = BoundingBoxQuad(binding.Quad);

        Span<Vector2> interp = stackalloc Vector2[4];
        for (var i = 0; i < 4; i++)
            interp[i] = Vector2.Lerp(binding.Quad[i], targetRect[i], _straightenT);

        if (!Homography.TryComputeQuadToQuad(binding.Quad, interp, out var homography))
            return false;

        Span<Vector2> dest = stackalloc Vector2[4];
        dest[0] = homography.TransformPoint(new Vector2(0, 0));
        dest[1] = homography.TransformPoint(new Vector2(w, 0));
        dest[2] = homography.TransformPoint(new Vector2(w, h));
        dest[3] = homography.TransformPoint(new Vector2(0, h));

        Bbox(interp, out regionMin, out regionMax);

        // Extent = the rectified surface plus a margin of surround (so it isn't clipped to the photo rect).
        // Bounding to the region — not the whole warped photo — is essential: a steep rectification sends the
        // far photo corners toward infinity, so sizing to the full warp would blow the RT up and leave the
        // surface sub-pixel (it vanishes). The runaway surround is simply cropped to this window.
        var regionSize = regionMax - regionMin;
        bboxMin = regionMin - regionSize;
        bboxMax = regionMax + regionSize;

        var bboxSize = bboxMax - bboxMin;
        var maxDim = Math.Max(bboxSize.X, bboxSize.Y);
        var scale = maxDim > 4096f ? 4096f / maxDim : 1f;
        var rtSize = new Int2(Math.Max(1, (int)(bboxSize.X * scale)), Math.Max(1, (int)(bboxSize.Y * scale)));

        var destLocal = new[]
                            {
                                (dest[0] - bboxMin) * scale,
                                (dest[1] - bboxMin) * scale,
                                (dest[2] - bboxMin) * scale,
                                (dest[3] - bboxMin) * scale,
                            };

        warped = OutputManager.RenderWarpedTexture(texture, destLocal, rtSize);
        return warped is { IsDisposed: false };
    }

    private static void Bbox(ReadOnlySpan<Vector2> points, out Vector2 min, out Vector2 max)
    {
        min = max = points[0];
        for (var i = 1; i < points.Length; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }
    }

    private static Vector2[] BoundingBoxQuad(Vector2[] quad)
    {
        float minX = quad[0].X, maxX = quad[0].X, minY = quad[0].Y, maxY = quad[0].Y;
        for (var i = 1; i < 4; i++)
        {
            minX = Math.Min(minX, quad[i].X);
            maxX = Math.Max(maxX, quad[i].X);
            minY = Math.Min(minY, quad[i].Y);
            maxY = Math.Max(maxY, quad[i].Y);
        }

        return [new Vector2(minX, minY), new Vector2(maxX, minY), new Vector2(maxX, maxY), new Vector2(minX, maxY)];
    }

    private void DrawHeader(Setup setup, ReferenceImage image, Guid imageId, bool hasTraced)
    {
        CustomComponents.StylizedText($"{image.Name} · {image.Kind}", Fonts.FontSmall, UiColors.TextMuted);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##path", "image file path", ref image.FilePath, 1024);
        if (ImGui.IsItemDeactivatedAfterEdit())
            OutputSetupHandling.SaveActive();

        if (hasTraced)
        {
            if (CustomComponents.StateButton("Photo", _straightenTarget < 0.5f ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
                _straightenTarget = 0f;

            ImGui.SameLine();
            if (CustomComponents.StateButton("Straight", _straightenTarget >= 0.5f ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
                _straightenTarget = 1f;
        }

        // Traced surfaces on this image — selectable (the selected one is highlighted and straightened).
        var drewButton = false;
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            if (surface.Reference?.ImageId != imageId)
                continue;

            if (drewButton)
                ImGui.SameLine();

            drewButton = true;
            ImGui.PushID(surface.Id.GetHashCode());
            var label = string.IsNullOrEmpty(surface.Name) ? "untitled" : surface.Name;
            var state = surface.Id == _selectedSurfaceId ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default;
            if (CustomComponents.StateButton(label, state))
                _selectedSurfaceId = surface.Id;

            ImGui.PopID();
        }

        // Untraced surfaces — trace them onto this image.
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            if (surface.Reference != null)
                continue;

            if (drewButton)
                ImGui.SameLine();

            drewButton = true;
            ImGui.PushID(surface.Id.GetHashCode());
            var label = string.IsNullOrEmpty(surface.Name) ? "untitled" : surface.Name;
            if (ImGui.SmallButton("+ Trace " + label))
            {
                surface.Reference = new Surface.ReferenceBinding { ImageId = imageId, Quad = DefaultReferenceQuad(image) };
                _selectedSurfaceId = surface.Id;
                OutputSetupHandling.SaveActive();
            }

            ImGui.PopID();
        }
    }

    private static Vector2[] DefaultReferenceQuad(ReferenceImage image)
    {
        float w = Math.Max(1, image.Width);
        float h = Math.Max(1, image.Height);
        float x0 = w * 0.25f, x1 = w * 0.75f, y0 = h * 0.25f, y1 = h * 0.75f;
        return [new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)];
    }

    private Texture2D? LoadTexture(ReferenceImage image)
    {
        if (string.IsNullOrWhiteSpace(image.FilePath))
            return null;

        if (_textureResource == null || _loadedPath != image.FilePath)
        {
            _textureResource = ResourceManager.CreateTextureResource(image.FilePath, null);
            _loadedPath = image.FilePath;
            _mipsGenerated = false;
        }

        _context ??= new EvaluationContext();
        var texture = _textureResource.GetValue(_context);

        // The bitmap loader allocates a full mip chain but fills only level 0 — the coarse levels are garbage
        // until regenerated (LoadImage does the same after loading). Without this, the oblique straighten warp
        // minifies into the corrupt mips and bands. Once per load.
        if (!_mipsGenerated && texture is { IsDisposed: false })
        {
            var srv = SrvManager.GetSrvForTexture(texture);
            if (srv is { IsDisposed: false })
            {
                ResourceManager.Device.ImmediateContext.GenerateMips(srv);
                _mipsGenerated = true;
            }
        }

        return texture;
    }

    private readonly ScalableCanvas _canvas = new();
    private readonly ScalableCanvasProjection _projection;
    private Guid _fittedImageId;
    private Guid _selectedSurfaceId;
    private float _straightenTarget;
    private float _straightenT;
    private string _loadedPath = string.Empty;
    private bool _mipsGenerated;
    private Resource<Texture2D>? _textureResource;
    private EvaluationContext? _context;
}
