namespace Lib.image.fx.feedback;

[Guid("04ac6a30-a2ad-4e43-8954-94dc852b0602")]
internal sealed class AfterGlow2 : Instance<AfterGlow2>
{
    [Output(Guid = "358f9f9c-17f5-4d2b-82c8-306ceb5edaa0")]
    public readonly Slot<Texture2D> Output = new();

    [Output(Guid = "bba87b99-0d84-4710-b080-887f38860318")]
    public readonly Slot<Command> CombinedLayers = new();


    [Input(Guid = "f06e5ede-f7ff-48ed-b2cf-9a87ef5932ec")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "ec6f2f37-05f7-41ad-8a42-2e6974cce0cd")]
    public readonly InputSlot<float> DecayRate = new();

    [Input(Guid = "edc5e4e1-b4fc-4f1a-a080-ddbe2ca18a55")]
    public readonly InputSlot<float> GlowImpact = new();

    [Input(Guid = "41165195-b54b-46b2-a928-1347870e8af9")]
    public readonly InputSlot<float> BlurAmount = new();

    [Input(Guid = "47ec468d-b984-4653-879d-a08875a90d19")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "b2ee3011-3187-4093-ab7d-30ec81c8ccc9")]
    public readonly InputSlot<Vector4> OrgColor = new();

}