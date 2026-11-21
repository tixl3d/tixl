using Lib.io.video.depthanything.Utils;

#pragma warning disable CS8981

namespace Lib.io.video.depthanything;

/// <summary>
/// Shifts an image by its depth, as if the camera moved sideways
/// </summary>
[Guid("9e4a6c18-3b5d-4f82-a091-6c8e0f3b5d72")]
public class DepthParallax : Instance<DepthParallax>
{
    #region Outputs

    [Output(Guid = "a05b7d29-4c6e-4093-b1a2-7d9f1a4c6e83", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> ResultTexture = new();

    #endregion

    public DepthParallax()
    {
        ResultTexture.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var imageTexture = ImageTexture.GetValue(context);
        var depthTexture = DepthTexture.GetValue(context);
        if (imageTexture == null || imageTexture.IsDisposed || depthTexture == null || depthTexture.IsDisposed)
        {
            ResultTexture.Value = imageTexture;
            return;
        }

        var offset = Offset.GetValue(context);
        var parameters = new Parameters
                             {
                                 OffsetX = offset.X,
                                 OffsetY = offset.Y,
                                 MinDepth = MinDepth.GetValue(context),
                                 MaxDepth = MaxDepth.GetValue(context),
                                 FocalPlane = FocalPlane.GetValue(context),
                                 Steps = Math.Clamp(Steps.GetValue(context), 1, MaxSteps),
                             };

        var description = imageTexture.Description;
        if (!_pass.TryRun(parameters, imageTexture, depthTexture, description.Width, description.Height, Format.R16G16B16A16_Float))
        {
            if (_pass.Error != _loggedError)
            {
                _loggedError = _pass.Error;
                Log.Warning($"[DepthParallax] {_pass.Error}", this);
            }

            ResultTexture.Value = imageTexture;
            return;
        }

        ResultTexture.Value = _pass.Output;
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
        public float OffsetX;
        public float OffsetY;
        public float MinDepth;
        public float MaxDepth;
        public float FocalPlane;
        public int Steps;
        public float Padding;
    }
#pragma warning restore CS0649

    private const int MaxSteps = 64;

    private readonly ComputePass _pass = new("DepthAnything:shaders/depth-parallax-cs.hlsl");
    private string? _loggedError;

    #region Inputs

    [Input(Guid = "b16c8e3a-5d7f-41a4-82b3-8ea02b5d7f94")]
    public readonly InputSlot<Texture2D> ImageTexture = new();

    [Input(Guid = "c27d9f4b-6e80-42b5-93c4-9fb13c6e80a5")]
    public readonly InputSlot<Texture2D> DepthTexture = new();

    [Input(Guid = "d38ea05c-7f91-43c6-a4d5-a0c24d7f91b6")]
    public readonly InputSlot<float> MinDepth = new(0f);

    [Input(Guid = "e49fb16d-80a2-44d7-b5e6-b1d35e80a2c7")]
    public readonly InputSlot<float> MaxDepth = new(1f);

    [Input(Guid = "f50ac27e-91b3-45e8-86f7-c2e46f91b3d8")]
    public readonly InputSlot<Vector2> Offset = new();

    [Input(Guid = "061bd38f-a2c4-46f9-97a8-d3f57a02c4e9")]
    public readonly InputSlot<float> FocalPlane = new(0.5f);

    [Input(Guid = "172ce490-b3d5-470a-a8b9-e4068b13d5fa")]
    public readonly InputSlot<int> Steps = new(16);


    #endregion
}
