namespace Lib.point.transform;

[Guid("58afd953-d3fd-44a9-b54b-ccb287edc40c")]
internal sealed class FindClosestPointsOnMesh : Instance<FindClosestPointsOnMesh>
{

    [Output(Guid = "fdf76150-0448-470b-bf31-c3844f7b84f3")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "b9b7bda8-969d-413a-9446-b72a4c5864bb")]
    public readonly InputSlot<BufferWithViews> Points = new();

    [Input(Guid = "603501a2-5581-47ca-a9e1-ab8e09fda1d8")]
    public readonly InputSlot<MeshBuffers> Mesh = new();
}