namespace Skills.Render.R05_ShadingAndMaterial;

[Guid("c65e8c85-816b-456c-98fb-5c851802e8d6")]
internal sealed class R05g_NormalMapping :Instance<R05g_NormalMapping>{
    [Output(Guid = "2b67ef9e-c4ad-45d9-8bd8-f853f41fe2b4")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}