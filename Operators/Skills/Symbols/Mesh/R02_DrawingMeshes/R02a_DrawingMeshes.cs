namespace Skills.Mesh.R02_DrawingMeshes;

[Guid("33a43877-abce-44cb-8504-eb2c5b5bca92")]
internal sealed class R02a_DrawingMeshes :Instance<R02a_DrawingMeshes>{
    [Output(Guid = "69f40505-df90-4d47-bfe6-2aa752f84a29")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}