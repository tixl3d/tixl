namespace Types.Routing;

// Passes T3.Core.DataTypes.MeshBuffers through the .t3 connection; no C# evaluator is needed.
[Guid("010bf03a-27a0-4d1b-bd8e-d23a482d8efd")]
public sealed class RerouteMeshBuffers : Instance<RerouteMeshBuffers>, IRerouteNode
{
    [Output(Guid = "23cea31d-c829-4062-894b-03000beccc5c")]
    public readonly Slot<T3.Core.DataTypes.MeshBuffers> Output = new();

    [Input(Guid = "6749c602-27db-4b5d-afaa-a7f0136fae2b")]
    public readonly InputSlot<T3.Core.DataTypes.MeshBuffers> Input = new();
}
