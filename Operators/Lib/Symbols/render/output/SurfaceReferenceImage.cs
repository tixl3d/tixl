#nullable enable
using System.Numerics;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Graphics.Compat;

namespace Lib.render.output;

/// <summary>
/// The straightened reference photo of an output-setup surface or region, cropped to it: the wall as it really
/// looks, upright and in the surface's aspect, as a design guide for content placed on it. A region shows its
/// part of the photo its traced ancestor was put upright on.
/// </summary>
[Guid("6435a961-ef0a-4e1d-bbcd-76e665bd58b6")]
internal sealed class SurfaceReferenceImage : Instance<SurfaceReferenceImage>, IStatusProvider
{
    [Output(Guid = "8ab20f20-5e19-4b4b-b846-4b8abbd82817")]
    public readonly Slot<Texture2D?> Texture = new();

    public SurfaceReferenceImage()
    {
        Texture.UpdateAction = Update;

        // A re-traced or resized surface changes the picture without touching an input.
        ActiveSetup.Changed += OnSetupChanged;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
        {
            ActiveSetup.Changed -= OnSetupChanged;
            OutputCompositor.ReleaseScratchTarget(_targetKey);
        }

        base.Dispose(isDisposing);
    }

    private void OnSetupChanged() => Texture.DirtyFlag.ForceInvalidate();

    private void Update(EvaluationContext context)
    {
        Texture.Value = null;
        var setup = ActiveSetup.Current;
        var surfaceId = Surface.GetValue(context);
        var surround = Math.Clamp(Surround.GetValue(context), 0f, 2f);
        var maxSize = Math.Clamp(MaxSize.GetValue(context), 16, OutputDefinition.MaxResolution);

        if (setup == null || setup.FindSurface(surfaceId) is not { } surface)
        {
            _warning = surfaceId == Guid.Empty ? "Pick a surface or region." : "The surface is not in the active output setup.";
            return;
        }

        var traced = StraightenedPhoto.FindTraceCarrier(setup, surface.Id);
        if (traced?.Trace is not { Quad.Length: >= 4 } trace
            || setup.FindReferenceImage(trace.ImageId) is not { } image
            || string.IsNullOrWhiteSpace(image.FilePath))
        {
            _warning = $"'{surface.Name}' is not traced on a reference photo, nor is a surface it belongs to.";
            return;
        }

        if (!StraightenedPhoto.TryGetWindow(setup, traced, surface, out var window))
        {
            _warning = $"Can't find where '{surface.Name}' sits on '{traced.Name}'.";
            return;
        }

        var photo = GetPhoto(image.FilePath, context);
        if (photo is not { IsDisposed: false })
        {
            _warning = $"Can't load the reference photo '{image.FilePath}'.";
            return;
        }

        StraightenedPhoto.TargetBounds(traced, out var targetMin, out var targetMax);
        var photoSize = new Int2(photo.Description.Width, photo.Description.Height);

        // Drawn from inside another op's render: everything the warp binds is put back afterwards.
        var deviceContext = ResourceManager.Device.ImmediateContext;
        deviceContext.PushState(StateGroups.All);
        StraightenedPhoto.TryRender(photoSize, trace.Quad, targetMin, targetMax, photo, 1f, _targetKey, maxSize, surround, window,
                                    _interpolatedQuad, _warpedPhotoCorners, out var straightened,
                                    out Vector2 bboxMin, out Vector2 bboxMax, out Vector2 regionMin, out Vector2 regionMax);
        deviceContext.PopState();

        Texture.Value = straightened;
        _warning = straightened == null ? "Straightening the photo failed; the trace may be degenerate." : null;
    }

    /** The photo, loaded once per path. */
    private Texture2D? GetPhoto(string filePath, EvaluationContext context)
    {
        if (_photoResource == null || _photoPath != filePath)
        {
            _photoResource = ResourceManager.CreateTextureResource(filePath, this);
            _photoPath = filePath;
        }

        return _photoResource.GetValue(context);
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        _warning == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string? IStatusProvider.GetStatusMessage() => _warning;

    private readonly Guid _targetKey = Guid.NewGuid();
    private readonly Vector2[] _interpolatedQuad = new Vector2[4];
    private readonly Vector2[] _warpedPhotoCorners = new Vector2[4];
    private Resource<Texture2D>? _photoResource;
    private string? _photoPath;
    private string? _warning;

    [Input(Guid = "1a5d05c4-b3e3-4b99-90cb-7c3da6920703")]
    public readonly InputSlot<Guid> Surface = new();

    [Input(Guid = "24ce9a8f-7608-4f98-a7de-228ded5b27dd")]
    public readonly InputSlot<float> Surround = new();

    [Input(Guid = "1afb2fda-0889-41b6-838e-f5bdfbbc3dd0")]
    public readonly InputSlot<int> MaxSize = new();
}
