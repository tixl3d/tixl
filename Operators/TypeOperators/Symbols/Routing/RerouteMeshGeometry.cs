namespace Types.Routing;

[Guid("f25793d0-e843-43ce-aeae-0980866a3080")]
public sealed class RerouteMeshGeometry : Instance<RerouteMeshGeometry>, IRerouteNode
{
    [Output(Guid = "c0dc313e-308e-4d1c-9261-d53552573a16")]
    public readonly Slot<T3.Core.DataTypes.MeshGeometry> Output = new();

    [Input(Guid = "02029be3-630f-40a6-94fd-f90a282c82d4")]
    public readonly InputSlot<T3.Core.DataTypes.MeshGeometry> Input = new();
}
