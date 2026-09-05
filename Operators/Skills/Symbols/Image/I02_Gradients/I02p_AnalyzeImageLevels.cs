namespace Skills.Image.I02_Gradients;

[Guid("50658462-5800-44ce-936f-2bf069a6ca7b")]
internal sealed class I02p_AnalyzeImageLevels :Instance<I02p_AnalyzeImageLevels>{
    [Output(Guid = "6f7650cd-dee6-4711-b1df-dbd39e847fd5")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}