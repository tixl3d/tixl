namespace Types.Routing;

[Guid("bc6eba9c-93f1-4568-9070-e9a2bb8854af")]
public sealed class RerouteVector3 : Instance<RerouteVector3>, IRerouteNode
{
    [Output(Guid = "1d4b0cef-88b9-4119-a686-b729b9ef213e")]
    public readonly Slot<System.Numerics.Vector3> Output = new();

    [Input(Guid = "ee791789-c1d7-4193-a889-929e50d3b7ef")]
    public readonly InputSlot<System.Numerics.Vector3> Input = new();
}
