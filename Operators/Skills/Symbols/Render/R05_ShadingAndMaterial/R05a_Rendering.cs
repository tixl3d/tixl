namespace Skills.Render.R05_ShadingAndMaterial;

[Guid("2b277fb9-2caa-4652-ae05-9f1806c19044")]
internal sealed class R05a_Rendering :Instance<R05a_Rendering>{
    [Output(Guid = "ffc914f9-7924-45df-b1db-1e225b11907e")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}