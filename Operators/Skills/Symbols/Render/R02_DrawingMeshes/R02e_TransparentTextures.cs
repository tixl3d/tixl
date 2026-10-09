namespace Skills.Render.R02_DrawingMeshes;

[Guid("f5045ea4-8080-4f05-94d5-fab87c256251")]
internal sealed class R02e_TransparentTextures :Instance<R02e_TransparentTextures>{
    [Output(Guid = "e1918bc1-36e8-44ac-8c29-4e00c1f8a9b1")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}