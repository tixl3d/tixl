namespace Lib.image.fx.feedback;

[Guid("d75de240-28a1-48cc-9b8f-388272188023")]
internal sealed class AfterGlow : Instance<AfterGlow>
{
    [Output(Guid = "93c699c3-6d32-4425-8a00-6ce472bb808b")]
    public readonly Slot<Texture2D> Output = new();

    [Output(Guid = "36d50979-ceae-4087-886e-eeaf1b33d73b")]
    public readonly Slot<Command> CombinedLayers = new();

    [Input(Guid = "4a83673a-c0ec-4dba-9ce0-b71cb2ee0849")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "a4794ed9-d432-4118-9078-0a832cd1c046")]
    public readonly InputSlot<float> BlurAmount = new();

    [Input(Guid = "baa63233-d740-4978-b115-1f20975b53b0")]
    public readonly InputSlot<float> GlowImpact = new();

    [Input(Guid = "6ddbf04b-75ff-4e6e-b850-d28cb7fb8fc0")]
    public readonly InputSlot<float> DecayRate = new();

    [Input(Guid = "f9ef4416-3730-41ca-a1ae-336a0c3c52dd")]
    public readonly InputSlot<float> ContrastOffset2 = new();

    [Input(Guid = "9af9f8af-f7f6-465d-a35b-7b0d952a7db6")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "949dc09e-9782-4876-b4d3-d2ffa3a87a0a")]
    public readonly InputSlot<Vector4> Color = new();

}