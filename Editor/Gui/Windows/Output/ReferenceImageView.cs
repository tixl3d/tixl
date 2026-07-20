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

        var image = setup.ReferenceImages.Find(r => r.Id == imageId);
        if (image == null)
            return;

        var tracedSurface = FindTracedSurface(setup, imageId);
        DrawHeader(setup, image, imageId, tracedSurface != null);

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
        var min = _projection.CanvasToScreen(Vector2.Zero);
        var max = _projection.CanvasToScreen(size);
        dl.AddRectFilled(min, max, UiColors.BackgroundFull.Fade(0.4f));

        // While straightening, show the photo warped so the traced surface rectifies; otherwise the raw photo.
        var straightening = tracedSurface?.Reference != null && _straightenT > 0.001f;
        var shownTexture = straightening ? RenderStraightened(image, tracedSurface!.Reference!, texture) : texture;
        var srv = SrvManager.GetSrvForTexture(shownTexture is { IsDisposed: false } ? shownTexture : texture);
        if (srv is { IsDisposed: false })
            dl.AddImage(srv.NativePointer, min, max);

        dl.AddRect(min, max, UiColors.ForegroundFull.Fade(0.25f));

        // Trace handles only make sense on the un-straightened photo.
        if (straightening)
            return;

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            var surface = setup.Surfaces[i];
            var binding = surface.Reference;
            if (binding == null || binding.ImageId != imageId || binding.Quad.Length < 4)
                continue;

            ImGui.PushID(surface.Id.GetHashCode());
            var style = CornerPinHandles.Style.ForSurface(surface.Name, editable: true);
            style.DrawChecker = false;
            var phase = CornerPinHandles.Draw(binding.Quad, _projection, style, out _);
            if (phase == CanvasPointHandle.DragPhase.Completed)
                OutputSetupHandling.SaveActive();

            ImGui.PopID();
        }
    }

    private static Surface? FindTracedSurface(Setup setup, Guid imageId)
    {
        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (setup.Surfaces[i].Reference?.ImageId == imageId)
                return setup.Surfaces[i];
        }

        return null;
    }

    // Warps the photo so the traced quad rectifies to its bounding box, interpolated by the transition.
    private Texture2D? RenderStraightened(ReferenceImage image, Surface.ReferenceBinding binding, Texture2D texture)
    {
        if (binding.Quad.Length < 4)
            return null;

        var w = Math.Max(1, image.Width);
        var h = Math.Max(1, image.Height);
        var targetRect = BoundingBoxQuad(binding.Quad);

        Span<Vector2> interp = stackalloc Vector2[4];
        for (var i = 0; i < 4; i++)
            interp[i] = Vector2.Lerp(binding.Quad[i], targetRect[i], _straightenT);

        if (!Homography.TryComputeQuadToQuad(binding.Quad, interp, out var homography))
            return null;

        var destQuad = new[]
                           {
                               homography.TransformPoint(new Vector2(0, 0)),
                               homography.TransformPoint(new Vector2(w, 0)),
                               homography.TransformPoint(new Vector2(w, h)),
                               homography.TransformPoint(new Vector2(0, h)),
                           };

        return OutputManager.RenderWarpedTexture(texture, destQuad, new Int2(w, h));
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

        // Trace untraced surfaces onto this image.
        var drewButton = false;
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
        }

        _context ??= new EvaluationContext();
        return _textureResource.GetValue(_context);
    }

    private readonly ScalableCanvas _canvas = new();
    private readonly ScalableCanvasProjection _projection;
    private Guid _fittedImageId;
    private float _straightenTarget;
    private float _straightenT;
    private string _loadedPath = string.Empty;
    private Resource<Texture2D>? _textureResource;
    private EvaluationContext? _context;
}
