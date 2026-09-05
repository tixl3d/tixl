namespace Skills.Mesh.Me01_CreatingMeshes;

[Guid("bddff4d6-e874-47bb-8a09-a336997dddeb")]
internal sealed class Me01b_CombiningMeshes :Instance<Me01b_CombiningMeshes>{
    [Output(Guid = "faf687b7-e828-46c9-b2f3-8b4cc63d0b88")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}