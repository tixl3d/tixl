namespace Skills.Render.R02_DrawingMeshes;

[Guid("1e3ff394-1d2d-4318-b50f-2147e001f26c")]
internal sealed class R02f_HatchingStyle :Instance<R02f_HatchingStyle>{
    [Output(Guid = "ee6f70e7-ee21-4350-ab52-78fa617f81a7")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}