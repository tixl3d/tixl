using Lib.io.video.depthanything.Utils;

#pragma warning disable CS8981

namespace Lib.io.video.depthanything;

/// <summary>
/// Filters and processes depth data with bilateral filtering for edge-aware smoothing
/// </summary>
[Guid("e2f3a4b5-c6d7-e8f9-a0b1-c2d3e4f5a6b7")]
public class DepthFilter : Instance<DepthFilter>
{
    #region Outputs

    [Output(Guid = "f3a4b5c6-d7e8-f9a0-b1c2-d3e4f5a6b7c8", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> FilteredDepth = new();

    #endregion

    public DepthFilter()
    {
        FilteredDepth.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var depthTexture = DepthTexture.GetValue(context);
        var enabled = Enabled.GetValue(context);

        if (!enabled || depthTexture == null || depthTexture.IsDisposed)
        {
            FilteredDepth.Value = depthTexture;
            return;
        }

        var sigmaSpace = Math.Max(SigmaSpace.GetValue(context), MinSigma);
        var sigmaColor = Math.Max(SigmaColor.GetValue(context), MinSigma);

        var parameters = new Parameters
                             {
                                 SpaceCoefficient = -0.5f / (sigmaSpace * sigmaSpace),
                                 ColorCoefficient = -0.5f / (sigmaColor * sigmaColor),
                                 Radius = Math.Clamp((int)(sigmaSpace * 2), 1, MaxRadius)
                             };

        var description = depthTexture.Description;
        if (!_pass.TryRun(parameters, depthTexture, null, description.Width, description.Height, Format.R32_Float))
        {
            if (_pass.Error != _loggedError)
            {
                _loggedError = _pass.Error;
                Log.Warning($"[DepthFilter] {_pass.Error}", this);
            }

            FilteredDepth.Value = depthTexture;
            return;
        }

        FilteredDepth.Value = _pass.Output;
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
        public float SpaceCoefficient;
        public float ColorCoefficient;
        public int Radius;
        public float Padding;
    }
#pragma warning restore CS0649

    private const float MinSigma = 0.0001f;
    private const int MaxRadius = 5;

    private readonly ComputePass _pass = new("DepthAnything:shaders/depth-filter-cs.hlsl");
    private string? _loggedError;

    #region Inputs

    [Input(Guid = "a2b3c4d5-e6f7-a8b9-c0d1-e2f3a4b5c6d7")]
    public readonly InputSlot<Texture2D> DepthTexture = new();

    [Input(Guid = "b3c4d5e6-f7a8-b9c0-d1e2-f3a4b5c6d7e8")]
    public readonly InputSlot<float> SigmaSpace = new(1.0f);

    [Input(Guid = "c4d5e6f7-a8b9-c0d1-e2f3-a4b5c6d7e8f9")]
    public readonly InputSlot<float> SigmaColor = new(0.1f);

    [Input(Guid = "d5e6f7a8-b9c0-d1e2-f3a4-b5c6d7e8f9a0")]
    public readonly InputSlot<bool> Enabled = new(true);

    #endregion
}
