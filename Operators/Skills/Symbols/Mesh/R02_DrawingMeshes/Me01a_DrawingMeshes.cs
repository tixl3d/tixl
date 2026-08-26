namespace Skills.Mesh.R02_DrawingMeshes;

[Guid("effe072f-a15a-468c-8af2-53de5ec33378")]
internal sealed class Me01a_DrawingMeshes :Instance<Me01a_DrawingMeshes>{
    [Output(Guid = "e77402d9-51c6-4fd7-a3f3-ef690a285894")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}