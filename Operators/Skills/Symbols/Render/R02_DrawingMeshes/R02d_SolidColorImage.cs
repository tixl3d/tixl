namespace Skills.Render.R02_DrawingMeshes;

[Guid("ec042dc1-e616-4a60-81ab-91aede4b6e73")]
internal sealed class R02d_SolidColorImage :Instance<R02d_SolidColorImage>{
    [Output(Guid = "b58878b0-dff6-4525-b826-b56c43e26c54")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}