namespace Skills.Image.I03_Analyze;

[Guid("ed9e9727-22bb-4ac8-85b0-6cab1c2f3338")]
internal sealed class I03a_AnalyzingImages :Instance<I03a_AnalyzingImages>{
    [Output(Guid = "b0843cbe-bebb-4062-8fa3-289d89f582fe")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}