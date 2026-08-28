namespace Skills.Render.R05_ShadingAndMaterial;

[Guid("99520f2a-7e6a-4bc2-ae1f-f1e6e3407766")]
internal sealed class R05f_EmittingLight :Instance<R05f_EmittingLight>{
    [Output(Guid = "4a3a0363-5118-46f3-99b0-9affef8493c3")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}