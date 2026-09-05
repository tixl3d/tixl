namespace Lib.image.fx.stylize;

[Guid("2a5475c8-9e16-409f-8c40-a3063e045d38")]
internal sealed class DetectEdges : Instance<DetectEdges>
{
    [Output(Guid = "caf8af48-8819-49b4-890b-89545c8c0ff5")]
    public readonly Slot<Texture2D> TextureOutput = new();


    [Input(Guid = "4041b6d8-15e5-428c-9967-7105975a46f7")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "7f66aa8d-fbdd-47d6-ba38-07e257e19401")]
    public readonly InputSlot<float> SampleRadius = new();

    [Input(Guid = "d3197979-b418-4182-b1c9-f3126b175f8d")]
    public readonly InputSlot<float> Strength = new();

    [Input(Guid = "9dae724d-7be8-4f82-8907-28550ddbf6e6")]
    public readonly InputSlot<float> Contrast = new();

    [Input(Guid = "6d10c73c-37b8-443b-94d9-854b04027a3c")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "c0a17636-f75b-45c0-ab63-cb0f9130a7ac")]
    public readonly InputSlot<float> MixOriginal = new();

    [Input(Guid = "921b8a04-d3b5-408e-ad3e-311a4c9890b1")]
    public readonly InputSlot<bool> OutputAsTransparent = new();
}