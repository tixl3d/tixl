namespace Skills.Image.I03_Analyze;

[Guid("ed6562fe-9e74-4a0f-b006-2ddb114fd0e6")]
internal sealed class I03b_ColorResolution :Instance<I03b_ColorResolution>{
    [Output(Guid = "63cf940d-767e-48e1-82a0-e39c8443d82c")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}