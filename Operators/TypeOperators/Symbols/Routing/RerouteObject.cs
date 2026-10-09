namespace Types.Routing;

[Guid("507de710-a745-4f8c-9245-90fc4ac105f8")]
public sealed class RerouteObject : Instance<RerouteObject>, IRerouteNode
{
    [Output(Guid = "422e3011-b822-4561-8e4b-8b11882ebffd")]
    public readonly Slot<object> Output = new();

    [Input(Guid = "09f32d22-f4e8-43f7-a3a7-bbbfc672448e")]
    public readonly InputSlot<object> Input = new();
}
