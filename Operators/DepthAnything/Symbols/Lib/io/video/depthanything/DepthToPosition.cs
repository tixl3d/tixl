using Lib.io.video.depthanything.Utils;

#pragma warning disable CS8981

namespace Lib.io.video.depthanything;

/// <summary>
/// Unprojects a depth map into camera-space positions
/// </summary>
[Guid("6a1f3c85-2d4b-4e97-8c10-5b7d9e2a4f61")]
public class DepthToPosition : Instance<DepthToPosition>
{
    #region Outputs

    [Output(Guid = "8c3e5a07-4f6d-4b19-9e32-7d9fb14c6a83", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> PositionTexture = new();

    #endregion

    public DepthToPosition()
    {
        PositionTexture.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var depthTexture = DepthTexture.GetValue(context);
        if (depthTexture == null || depthTexture.IsDisposed)
        {
            PositionTexture.Value = null;
            return;
        }

        var parameters = new Parameters
                             {
                                 TanHalfFovY = MathF.Tan(Math.Clamp(FieldOfView.GetValue(context), 1f, 179f) * MathF.PI / 360f),
                                 DepthScale = DepthScale.GetValue(context),
                             };

        var description = depthTexture.Description;
        if (!_pass.TryRun(parameters, depthTexture, null, description.Width, description.Height, Format.R32G32B32A32_Float))
        {
            if (_pass.Error != _loggedError)
            {
                _loggedError = _pass.Error;
                Log.Warning($"[DepthToPosition] {_pass.Error}", this);
            }

            PositionTexture.Value = null;
            return;
        }

        PositionTexture.Value = _pass.Output;
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
        public float TanHalfFovY;
        public float DepthScale;
        public float Padding;
    }
#pragma warning restore CS0649

    private readonly ComputePass _pass = new("DepthAnything:shaders/depth-to-position-cs.hlsl");
    private string? _loggedError;

    #region Inputs

    [Input(Guid = "0b4d6f29-5a7c-4d3b-a154-9e0c2d4f7b05")]
    public readonly InputSlot<Texture2D> DepthTexture = new();

    [Input(Guid = "1c5e7a3b-6b8d-4e4c-b265-0f1d3e5a8c16")]
    public readonly InputSlot<float> FieldOfView = new(50f);

    [Input(Guid = "2d6f8b4c-7c9e-4f5d-8376-1a2e4f6b9d27")]
    public readonly InputSlot<float> DepthScale = new(1f);


    #endregion
}
