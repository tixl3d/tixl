namespace Skills.Render.R05_ShadingAndMaterial;

[Guid("2aeda7da-e0e0-414b-a40f-6ff80554ce32")]
internal sealed class R05e_Alpha :Instance<R05e_Alpha>{
    [Output(Guid = "c75f250d-3218-453e-b894-e1844968b4d4")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}