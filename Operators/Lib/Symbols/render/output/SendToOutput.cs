#nullable enable
using System.Numerics;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using T3.Core.Output.Rendering;

namespace Lib.render.output;

/// <summary>
/// Supplies a texture to the project's active output setup. Routing lives in the setup, not here: the setup
/// holds a ContentSource standing 1:1 with this op, slices cut rectangles from it, and surfaces name the slice
/// they show. So this op only says "here are the pixels" — it registers with the <see cref="ContentSupplierRegistry"/>
/// and the host's output manager pulls from it. A pure supplier: no output slot.
/// </summary>
[Guid("0b8f2d4e-6a1c-47d3-9f5e-8c2a1b7d4e60")]
internal sealed class SendToOutput : Instance<SendToOutput>, IContentSupplier, IStatusProvider
{
    public SendToOutput()
    {
        ContentSupplierRegistry.Register(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ContentSupplierRegistry.Unregister(this);

        base.Dispose(disposing);
    }

    Vector4 IContentSupplier.GetColor(EvaluationContext context) => Color.GetValue(context);

    /// <summary>
    /// Pulls the content, rendered at <see cref="Resolution"/> when one is set, or at the size of the surface
    /// showing it when <see cref="SizeFrom"/> asks for that. Left at 0×0 the host's requested resolution stands —
    /// the canvas of the output this content is routed to — so an auto-sized render target upstream follows the
    /// projector rather than needing a size of its own. A derived size is scaled by <see cref="ResolutionFactor"/>.
    /// </summary>
    T3.Core.DataTypes.Texture2D? IContentSupplier.GetContent(EvaluationContext context)
    {
        var inherited = context.RequestedResolution;
        var requested = GetPinnedResolution(context);
        if (requested.Width <= 0 || requested.Height <= 0)
        {
            var factor = GetResolutionFactor(context);
            if (factor == 1f)
                return Texture.GetValue(context);

            requested = Scale(inherited, factor);
        }

        context.RequestedResolution = requested;
        var texture = Texture.GetValue(context);
        context.RequestedResolution = inherited;
        return texture;
    }
    void IContentSupplier.InvalidateContent() => Texture.InvalidateGraph();
    bool IContentSupplier.GetUpdateEnabled(EvaluationContext context) => Update.GetValue(context);
    IInputSlot IContentSupplier.UpdateInput => Update;
    Int2 IContentSupplier.GetResolution(EvaluationContext context) => GetPinnedResolution(context);

    /** The size this send insists on, or 0×0 to take what the output asks for. */
    private Int2 GetPinnedResolution(EvaluationContext context)
    {
        var sizeFrom = (SizeSources)SizeFrom.GetValue(context);
        var resolution = OutputDefinition.ClampResolution(Resolution.GetValue(context));
        if (sizeFrom == SizeSources.Resolution || ActiveSetup.Current is not { } setup)
            return resolution;

        if (sizeFrom == SizeSources.ProjectedSurface
            && OutputContentResolver.TryGetSurfaceRequest(setup, SymbolChildId, out var projected, out var projectedConflict))
        {
            return Scale(projected, GetResolutionFactor(context));
        }

        // The height it would render at anyway, the width from the surface: stable while the surface is aligned.
        if (sizeFrom == SizeSources.SurfaceAspect
            && OutputContentResolver.TryGetSurfaceAspect(setup, SymbolChildId, out var aspect, out var aspectConflict))
        {
            var height = resolution.Height > 0 ? resolution.Height : context.RequestedResolution.Height;
            if (height > 0)
                return Scale(new Int2(Math.Max(1, (int)MathF.Round(height * aspect)), height), GetResolutionFactor(context));
        }

        return resolution;
    }

    private float GetResolutionFactor(EvaluationContext context)
    {
        return Math.Clamp(ResolutionFactor.GetValue(context), MinResolutionFactor, MaxResolutionFactor);
    }

    private static Int2 Scale(Int2 resolution, float factor)
    {
        return new Int2(Math.Clamp((int)MathF.Round(resolution.Width * factor), 1, OutputDefinition.MaxResolution),
                        Math.Clamp((int)MathF.Round(resolution.Height * factor), 1, OutputDefinition.MaxResolution));
    }

    private const float MinResolutionFactor = 0.25f;
    private const float MaxResolutionFactor = 4f;
    IInputSlot IContentSupplier.ResolutionInput => Resolution;

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        // Named rather than discarded: this package has a namespace called `_`, which `out _` binds to.
        return GetSurfaceSizeWarning() != null || OutputContentStats.TryGetSizeConflict(SymbolChildId, out var rendered, out var other)
                   ? IStatusProvider.StatusLevel.Warning
                   : IStatusProvider.StatusLevel.Success;
    }

    string? IStatusProvider.GetStatusMessage()
    {
        if (GetSurfaceSizeWarning() is { } surfaceWarning)
            return surfaceWarning;

        if (!OutputContentStats.TryGetSizeConflict(SymbolChildId, out var rendered, out var other))
            return null;

        return $"Routed to outputs of different sizes ({rendered.Width}×{rendered.Height} and {other.Width}×{other.Height}).\n"
               + "The content renders once per frame, at the first of them, so the other shows that size.\n"
               + "Set this op's Resolution to pick one deliberately, or give each output its own send.";
    }

    [Input(Guid = "8a4dd1b3-2e6f-4c25-9d0a-7f3b61c8e942")]
    public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new();

    [Input(Guid = "9a1c4f7e-2d38-4b6a-8e50-1f7c3d9b0a24")]
    public readonly InputSlot<bool> Update = new();

    [Input(Guid = "1d83a6f2-49c0-4e17-8b5d-c72e90fa4b36")]
    public readonly InputSlot<Vector4> Color = new();

    /** Why Size From Surface can't do what it says, or null. Read from the last value, as status is drawn outside evaluation. */
    private string? GetSurfaceSizeWarning()
    {
        var sizeFrom = (SizeSources)SizeFrom.Value;
        if (sizeFrom == SizeSources.Resolution)
            return null;

        if (ActiveSetup.Current is not { } setup
            || !OutputContentResolver.TryGetSurfaceAspect(setup, SymbolChildId, out var aspect, out var hasConflict))
        {
            return "Size From takes the size from a surface, but no surface shows this content; it renders at the Resolution or the output's size.";
        }

        return hasConflict
                   ? $"Shown on surfaces of different aspects; it renders at {aspect:0.###}:1 for the first of them."
                   : null;
    }

    /// <summary>Where the render size comes from.</summary>
    private enum SizeSources
    {
        /// <summary>The Resolution input, or the output's size when it is 0×0.</summary>
        Resolution,

        /// <summary>The surface showing this content: its aspect, at a pixel per output pixel where it is projected.
        /// Follows the alignment, so the size changes while points are aligned.</summary>
        ProjectedSurface,

        /// <summary>The surface's aspect at the height it would render at anyway: the Resolution's, or the output's when
        /// that is 0×0. Doesn't change while the surface is aligned.</summary>
        SurfaceAspect,
    }

    [Input(Guid = "3125e84e-8644-46a9-8fae-e88ed06731f4", MappedType = typeof(SizeSources))]
    public readonly InputSlot<int> SizeFrom = new();

    [Input(Guid = "3c7f1e58-0a94-4d62-b8f3-2e5d9a0c4b17")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "fd8d094d-dd51-4659-8aab-ca9a028bee33")]
    public readonly InputSlot<float> ResolutionFactor = new();

    [Input(Guid = ContentSupplierSearch.ExposeToParentsInputGuid)]
    public readonly InputSlot<bool> ExposeToParents = new();
}
