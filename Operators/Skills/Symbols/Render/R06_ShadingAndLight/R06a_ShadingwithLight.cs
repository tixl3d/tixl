namespace Skills.Render.R06_ShadingAndLight;

[Guid("21eb13a7-04ad-4ba4-8911-3b254840ffd8")]
internal sealed class R06a_ShadingwithLight :Instance<R06a_ShadingwithLight>{
    [Output(Guid = "9d469f76-2666-4058-bffc-4987b0dea5b4")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}