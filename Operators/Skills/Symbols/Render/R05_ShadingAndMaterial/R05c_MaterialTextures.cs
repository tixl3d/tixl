namespace Skills.Render.R05_ShadingAndMaterial;

[Guid("fdca1fdf-fbae-4844-a6a2-a543f80ceb79")]
internal sealed class R05c_MaterialTextures :Instance<R05c_MaterialTextures>{
    [Output(Guid = "3c9b337c-0cc2-40b8-be20-7e3cecdf2e3c")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}