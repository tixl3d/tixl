namespace Lib.image.fx.distort;

[Guid("ecf2c782-4461-4a94-8995-067425e3f84b")]
internal sealed class ChromaticDistortion : Instance<ChromaticDistortion>
{
    [Output(Guid = "418fdcb7-03e8-4292-94e3-f614c1c1a538")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "6297675f-891c-4c06-8685-de3a1a3ffe9b")]
    public readonly InputSlot<Texture2D> Texture2d = new();

    [Input(Guid = "1df828ad-2d74-47fc-9f0e-10f4ce10b170")]
    public readonly InputSlot<Vector2> Center = new();

    [Input(Guid = "b9e78f18-51e8-4440-b8a4-9338f14a5668")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "2bb8354d-b995-497d-962a-c5e9952eca50")]
    public readonly InputSlot<float> Colorize = new();

    [Input(Guid = "48e1129c-d6cc-4018-a6ee-5266244edd7f")]
    public readonly InputSlot<float> Distort = new();

    [Input(Guid = "e0344f32-479e-48b6-9473-26ef59d22a66")]
    public readonly InputSlot<float> DistortOffset = new();

    [Input(Guid = "cb3ba878-127a-44cd-9aea-7c43e32a090a")]
    public readonly InputSlot<float> ScaleImage = new();

    [Input(Guid = "2bd2c6b9-93ce-4de9-9ee5-0b35f4f2719e")]
    public readonly InputSlot<int> SampleCount = new();

}