namespace Skills.Render.R05_ShadingAndMaterial;

[Guid("f2d52d59-c099-476d-83ef-0429f76f9180")]
internal sealed class R05b_IsItShiny :Instance<R05b_IsItShiny>{
    [Output(Guid = "1494189e-6ccd-4429-bf28-1dc6c12ff11e")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}