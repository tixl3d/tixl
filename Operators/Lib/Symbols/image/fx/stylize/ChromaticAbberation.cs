namespace Lib.image.fx.stylize;

[Guid("8a203866-148d-4785-ae0e-61328b7646bb")]
internal sealed class ChromaticAbberation : Instance<ChromaticAbberation>
{
    [Output(Guid = "8af0d916-9708-422b-8fb7-39ef59c82d7f")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "b62aece4-8098-475b-a4d3-469f81a58207")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "4c51b5f5-5307-45a7-9641-25f572627926")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "361a838a-7bf1-4fd2-8e0e-77edcef11965")]
    public readonly InputSlot<float> Strength = new();

    [Input(Guid = "4e03d06a-d19b-463f-bbbd-b64d24c04b9e")]
    public readonly InputSlot<int> SampleCount = new();

    [Input(Guid = "6dd98990-82a7-4f68-aef1-ff34d1825b3b")]
    public readonly InputSlot<float> Distort = new();
}