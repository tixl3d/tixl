using T3.Core.Utils;
using T3.Graphics.Compat;

namespace Lib.render._dx11.fxsetup;

[Guid("fd9bffd3-5c57-462f-8761-85f94c5a629b")]
internal sealed class PickBlendMode : Instance<PickBlendMode>
{
    [Output(Guid = "a42dd1c5-886c-4fa9-bf69-8b6321a48930")]
    public readonly Slot<BlendState> Result2 = new();

    public PickBlendMode()
    {
        Result2.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _blendStates ??= CreateBlendStates();
        var index = BlendMode.GetValue(context);
        Result2.Value = _blendStates[index.Mod(_blendStates.Length)];
    }

    /// <summary>
    /// Target 0 gets the mode's blend. The extra targets (normal, velocity, material) hold surface data that
    /// can't be blended: opaque and alpha draws overwrite it, all other modes leave it untouched.
    /// </summary>
    private static BlendState[] CreateBlendStates()
    {
        var modes = new BlendState[_colorTargetBlends.Length];
        for (var modeIndex = 0; modeIndex < modes.Length; modeIndex++)
        {
            var mode = (SharedEnums.BlendModes)modeIndex;
            var writesSurfaceData = mode is SharedEnums.BlendModes.Normal or SharedEnums.BlendModes.None;
            var surfaceDataTarget = writesSurfaceData
                                        ? new RenderTargetBlendDescription()
                                        : new RenderTargetBlendDescription { RenderTargetWriteMask = 0 };

            var description = new BlendStateDescription { IndependentBlendEnable = true };
            description.RenderTarget[0] = _colorTargetBlends[modeIndex];
            for (var targetIndex = 1; targetIndex < BlendStateDescription.MaxRenderTargets; targetIndex++)
            {
                description.RenderTarget[targetIndex] = surfaceDataTarget;
            }

            modes[modeIndex] = new BlendState(ResourceManager.Device, description);
        }

        return modes;
    }

    /** Indexed by <see cref="SharedEnums.BlendModes"/>. */
    private static readonly RenderTargetBlendDescription[] _colorTargetBlends =
        [
            // Normal
            new(true, BlendOption.SourceAlpha, BlendOption.InverseSourceAlpha, BlendOperation.Add,
                BlendOption.One, BlendOption.InverseSourceAlpha, BlendOperation.Add, ColorWriteMaskFlags.All),
            // Additive
            new(true, BlendOption.SourceAlpha, BlendOption.One, BlendOperation.Add,
                BlendOption.One, BlendOption.InverseSourceAlpha, BlendOperation.Add, ColorWriteMaskFlags.All),
            // Multiply
            new(true, BlendOption.Zero, BlendOption.SourceColor, BlendOperation.Add,
                BlendOption.One, BlendOption.DestinationAlpha, BlendOperation.Add, ColorWriteMaskFlags.All),
            // Invert
            new(true, BlendOption.InverseDestinationColor, BlendOption.InverseSourceColor, BlendOperation.Add,
                BlendOption.One, BlendOption.InverseSourceAlpha, BlendOperation.Add, ColorWriteMaskFlags.All),
            // None
            new(false, BlendOption.SourceAlpha, BlendOption.InverseSourceAlpha, BlendOperation.Add,
                BlendOption.One, BlendOption.InverseSourceAlpha, BlendOperation.Add, ColorWriteMaskFlags.All),
            // PreMultipliedExperimental
            new(true, BlendOption.SourceAlpha, BlendOption.InverseSourceAlpha, BlendOperation.Add,
                BlendOption.InverseSourceAlpha, BlendOption.One, BlendOperation.Add, ColorWriteMaskFlags.All),
            // BlendOnWhite
            new(true, BlendOption.One, BlendOption.SourceAlpha, BlendOperation.ReverseSubtract,
                BlendOption.One, BlendOption.InverseSourceAlpha, BlendOperation.Add, ColorWriteMaskFlags.All),
            // BlendOnWhite01
            new(true, BlendOption.SourceAlpha, BlendOption.InverseSourceColor, BlendOperation.ReverseSubtract,
                BlendOption.One, BlendOption.InverseSourceAlpha, BlendOperation.Add, ColorWriteMaskFlags.All),
            // UseImage_Alpha
            new(true, BlendOption.SourceAlpha, BlendOption.InverseSourceAlpha, BlendOperation.Minimum,
                BlendOption.One, BlendOption.Zero, BlendOperation.Add, ColorWriteMaskFlags.All),
        ];

    private static BlendState[]? _blendStates;

    [Input(Guid = "30b58444-0485-4116-8b15-7e62fee69eaa", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMode = new();
}
