namespace Examples.user.still.worksforeverybody.scenes;

[Guid("1cdedb32-f23b-4649-b4d3-9e158ef9be40")]
internal sealed class TVIntroSceneSetup : Instance<TVIntroSceneSetup>
{
    [Output(Guid = "087b8484-23cc-45e8-9290-3a98f07bd87a")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "4c5c6be2-05d0-4a70-8705-1c0aad83fa2d")]
    public readonly InputSlot<Texture2D> TvImage = new();

    [Input(Guid = "7b9c9b05-4654-48c5-9ee4-8eff949f106f")]
    public readonly InputSlot<float> TransitionProgress = new();

    [Input(Guid = "4a3bb4d3-89ad-4d1f-969d-6791c6384a41")]
    public readonly InputSlot<float> FX_Amount = new();

    [Input(Guid = "b9b50526-af84-4a25-927d-a4b64f230edb")]
    public readonly InputSlot<float> MainIntensity = new();

    [Input(Guid = "92d469d0-33ed-41e5-9b47-d20f58e38e91")]
    public readonly InputSlot<float> SceneBrightness = new();

    [Input(Guid = "b8191816-955d-4152-bc61-cf1d33351f22")]
    public readonly InputSlot<bool> Bypass = new();

    [Input(Guid = "3fb860f8-2c9b-4c5f-b589-2d08a13764ad")]
    public readonly InputSlot<float> ImageBrightess = new();

    [Input(Guid = "556055e6-9fde-41c4-94d5-c98c8e6e74d2")]
    public readonly InputSlot<bool> ShowClassroom = new();


}