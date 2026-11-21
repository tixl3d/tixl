using Lib.io.video.depthanything.Utils;

#pragma warning disable CS8981

namespace Lib.io.video.depthanything;

/// <summary>
/// Converts depth map to normal map using Sobel edge detection
/// </summary>
[Guid("d9e0f1a2-3b4c-5d6e-7f8a-9b0c1d2e3f4a")]
public class DepthToNormal : Instance<DepthToNormal>
{
    #region Outputs

    [Output(Guid = "e1f2a3b4-c5d6-e7f8-a9b0-c1d2e3f4a5b6", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> NormalTexture = new();

    #endregion

    public DepthToNormal()
    {
        NormalTexture.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var depthTexture = DepthTexture.GetValue(context);
        if (depthTexture == null || depthTexture.IsDisposed)
        {
            NormalTexture.Value = null;
            return;
        }

        var parameters = new Parameters
                             {
                                 Strength = Strength.GetValue(context),
                                 Invert = Invert.GetValue(context) ? 1u : 0u
                             };

        var description = depthTexture.Description;
        if (!_pass.TryRun(parameters, depthTexture, null, description.Width, description.Height, Format.R8G8B8A8_UNorm))
        {
            if (_pass.Error != _loggedError)
            {
                _loggedError = _pass.Error;
                Log.Warning($"[DepthToNormal] {_pass.Error}", this);
            }

            NormalTexture.Value = null;
            return;
        }

        NormalTexture.Value = _pass.Output;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing) return;

        _pass.Dispose();
        base.Dispose(isDisposing);
    }

    // Padding rounds the constant buffer up to whole 16-byte rows
#pragma warning disable CS0649
    private struct Parameters
    {
        public float Strength;
        public uint Invert;
        public float Padding0;
        public float Padding1;
    }
#pragma warning restore CS0649

    private readonly ComputePass _pass = new("DepthAnything:shaders/depth-to-normal-cs.hlsl");
    private string? _loggedError;

    #region Inputs

    [Input(Guid = "a1b2c3d4-e5f6-a7b8-c9d0-e1f2a3b4c5d6")]
    public readonly InputSlot<Texture2D> DepthTexture = new();

    [Input(Guid = "b2c3d4e5-f6a7-b8c9-d0e1-f2a3b4c5d6e7")]
    public readonly InputSlot<float> Strength = new(1.0f);

    [Input(Guid = "c3d4e5f6-a7b8-c9d0-e1f2-a3b4c5d6e7f8")]
    public readonly InputSlot<bool> Invert = new(false);

    #endregion
}
