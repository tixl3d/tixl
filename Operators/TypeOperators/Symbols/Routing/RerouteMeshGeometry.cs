namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.MeshGeometry through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("f25793d0-e843-43ce-aeae-0980866a3080")]
public sealed class RerouteMeshGeometry : Instance<RerouteMeshGeometry>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "c0dc313e-308e-4d1c-9261-d53552573a16")]
    public readonly Slot<T3.Core.DataTypes.MeshGeometry> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "02029be3-630f-40a6-94fd-f90a282c82d4")]
    public readonly InputSlot<T3.Core.DataTypes.MeshGeometry> Input = new();
}
