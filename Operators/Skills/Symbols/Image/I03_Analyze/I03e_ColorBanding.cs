namespace Skills.Image.I03_Analyze;

[Guid("5011827a-67f5-489c-a8ed-d4354cc4feb5")]
internal sealed class I03e_ColorBanding :Instance<I03e_ColorBanding>{
    [Output(Guid = "2ec4a637-d2f4-4991-b8ea-edc8593f23e8")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}