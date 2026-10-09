namespace Types.Routing;

[Guid("e5d94302-1cec-4ced-9962-9f1ef6d05e53")]
public sealed class RerouteRemapCurve : Instance<RerouteRemapCurve>, IRerouteNode
{
    [Output(Guid = "c469f5bb-1993-44ea-addc-015ffeea76ad")]
    public readonly Slot<T3.Core.DataTypes.RemapCurve> Output = new();

    [Input(Guid = "34d06894-8e53-4772-9810-fbfe70e44e79")]
    public readonly InputSlot<T3.Core.DataTypes.RemapCurve> Input = new();
}
