namespace Skills.Image.I03_Analyze;

[Guid("f1d54e9e-b385-4865-a3bd-6782f9314815")]
internal sealed class I03f_BlurQuality :Instance<I03f_BlurQuality>{
    [Output(Guid = "402ec1db-bd1c-41bb-b9dd-912c4ed418c8")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}