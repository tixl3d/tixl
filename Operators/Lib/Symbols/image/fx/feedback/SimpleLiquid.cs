namespace Lib.image.fx.feedback;

[Guid("e4e608e7-1248-4d73-910c-e8bfbb34251b")]
internal sealed class SimpleLiquid : Instance<SimpleLiquid>
{
    [Output(Guid = "9397f264-fde0-4806-b287-5cac9fd638b1")]
    public readonly Slot<Texture2D> ColorBuffer = new();

    [Input(Guid = "5c42604f-bbc1-4e65-8f21-8d8e79179e5a")]
    public readonly InputSlot<float> ShouldReset = new();

    [Input(Guid = "a9d8c898-545e-4c0e-af61-6d2251209460")]
    public readonly InputSlot<Vector2> Gravity = new();

    [Input(Guid = "3520474e-1341-4b49-a4ce-247b52ae3fe7")]
    public readonly InputSlot<float> BorderStrength = new();

    [Input(Guid = "b4db432f-3284-4b36-9c81-b644d958f582")]
    public readonly InputSlot<float> Damping = new();

    [Input(Guid = "04dce025-be57-4478-a66c-4e92f4f1f8c4")]
    public readonly InputSlot<float> MassAttraction = new();

    [Input(Guid = "fdd814ec-b8c6-41bd-91d1-ae6e6cfb7e8c")]
    public readonly InputSlot<float> Brightness = new();

    [Input(Guid = "bee3be17-6ac1-4c11-902a-0321cf5b1a19")]
    public readonly InputSlot<float> StabilizeMass = new();

    [Input(Guid = "ac239fb9-b90f-43e9-b591-b4e4e07bb5f7")]
    public readonly InputSlot<float> StabilizeMassTarget = new();

    [Input(Guid = "6bd77c9b-de54-4058-994d-351bdf6bc4da")]
    public readonly InputSlot<Texture2D> FxTexture = new();

    [Input(Guid = "75c2f9b1-b252-424b-ab8b-7fa543e20138")]
    public readonly InputSlot<Vector4> ApplyFxTexture = new();

}