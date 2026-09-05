namespace Skills.Render.R06_ShadingAndLight;

[Guid("af21d4fd-c99f-4649-8290-b557953b1a09")]
internal sealed class R06d_Foggy :Instance<R06d_Foggy>{
    [Output(Guid = "01cab103-cc4b-4f23-b661-4bf12d3d14b1")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}