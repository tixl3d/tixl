using T3.Core.Utils;

namespace Lib.point.draw.legacy;

[Guid("071c9aad-ecbf-47bf-b2f6-c43e8212d5b1")]
internal sealed class _DrawBillboardsOld : Instance<_DrawBillboardsOld>
{
    [Output(Guid = "b90aed8c-8c65-4dca-9ed3-fc2f08fc2cd1")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "2548a5d8-b2ab-4783-9298-c2e261d390f0")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "1a9e522a-38fb-478f-9f45-d3846cf401c2")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "0e4e5d78-8fe1-4b84-a0ab-d21f9704ccda")]
    public readonly InputSlot<float> Scale = new();

    [Input(Guid = "43d1169f-e6db-4229-882c-ca912e1cb6fd")]
    public readonly InputSlot<float> ScaleRandomly = new();

    [Input(Guid = "CC9CD2A7-70EC-4DBE-A151-C23EA08BCE56", MappedType = typeof(UsePointOrientationModes))]
    public readonly InputSlot<int> ApplyPointOrientation = new();


    [Input(Guid = "4e02b0c1-8a71-480b-8e9b-1a2b38641e1e")]
    public readonly InputSlot<float> Rotate = new();

    [Input(Guid = "407b79be-13da-477d-8123-90e6408de354")]
    public readonly InputSlot<float> RotateRandomly = new();

    [Input(Guid = "0555c899-3049-4d47-a2da-0febf6871754", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMod = new();

    [Input(Guid = "6176e442-a1b5-4762-be92-3f65df4ccdae")]
    public readonly InputSlot<bool> EnableDepthWrite = new();
        
    [Input(Guid = "059554d5-bee7-4724-bf62-3d492aaae62c")]
    public readonly InputSlot<float> WSpeed = new();

    [Input(Guid = "7d0f3c5d-4860-4dde-ad33-22376520b59d")]
    public readonly InputSlot<Gradient> GradientForW = new();

    [Input(Guid = "81e652c3-d12c-4b45-afd2-f6de9de20df1")]
    public readonly InputSlot<Curve> ScaleForW = new();

    [Input(Guid = "c82b7f84-508e-4a04-905e-39b55f75f4e6")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "957c9ece-5e66-4aee-b0c6-03421cbaf075")]
    public readonly InputSlot<Texture2D> Texture_ = new();

    [Input(Guid = "2c2337ce-9890-4cb3-b85b-5d1edce66671")]
    public readonly InputSlot<float> AlphaCut = new();

    private enum UsePointOrientationModes
    {
        Ignore,
        AsRotation,
        AsColor,
    }
}