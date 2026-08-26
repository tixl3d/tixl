namespace Skills.Mesh.R02_DrawingMeshes;

[Guid("02f5c918-9680-4891-8d78-a1bc2a705b6b")]
internal sealed class R02b_UnlitDrawing :Instance<R02b_UnlitDrawing>{
    [Output(Guid = "312f5d4a-85d6-4bc7-bc17-f966c89d1135")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}