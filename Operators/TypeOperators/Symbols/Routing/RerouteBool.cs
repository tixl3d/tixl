namespace Types.Routing;

[Guid("0334db8e-e2d4-46d2-b2be-c48d2dc7c1e5")]
public sealed class RerouteBool : Instance<RerouteBool>, IRerouteNode
{
    [Output(Guid = "a525f4b8-508a-4a8f-b3ea-fd00f991c971")]
    public readonly Slot<bool> Output = new();

    [Input(Guid = "b3528ff7-b1c2-4917-ae6f-435d298acd99")]
    public readonly InputSlot<bool> Input = new();
}
