namespace Lib.mesh.modify;

[Guid("ca6fc28d-5dc5-4162-9f85-954e65e65738")]
internal sealed class FilterMeshFaces : Instance<FilterMeshFaces>
{
    [Output(Guid = "b058f9d6-4800-439e-a462-2f2bb98bbfbd")]
    public readonly Slot<MeshBuffers> Output = new();

    [Input(Guid = "1aafe02d-7d25-4d7d-a3f3-e1be54d6012f")]
    public readonly InputSlot<MeshBuffers> InputMesh = new();

    [Input(Guid = "8c24b384-7e36-4b54-ad6c-e47341169450")]
    public readonly InputSlot<int> Count = new();

    [Input(Guid = "692037a3-1f93-4255-bb19-d8f60e2a5b74")]
    public readonly InputSlot<float> Percentage = new();

    [Input(Guid = "3e3dd39d-20a6-47f2-9037-ae861b88484a")]
    public readonly InputSlot<int> StartIndex = new();

    [Input(Guid = "e728c014-1387-4a5f-bfb6-f03fea682b25")]
    public readonly InputSlot<float> Step = new();

    [Input(Guid = "f3d48104-cedc-48e5-82db-c54bfb6b77be")]
    public readonly InputSlot<float> ScatterSelect = new();

    [Input(Guid = "a4acd677-5777-483f-a375-b7409982bd26")]
    public readonly InputSlot<int> Seed = new();
}
