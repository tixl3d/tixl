namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.GeometryShader through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("15a56ac6-a12c-4191-a3c9-08f70e9d9547")]
public sealed class RerouteGeometryShader : Instance<RerouteGeometryShader>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "8f57818f-03db-4076-95a7-e805817926f8")]
    public readonly Slot<T3.Core.DataTypes.GeometryShader> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "dc39ef94-1842-4d0a-80f4-cab40fba687f")]
    public readonly InputSlot<T3.Core.DataTypes.GeometryShader> Input = new();
}
