namespace Types.Routing;

// Passes System.Numerics.Vector4 through the .t3 connection; no C# evaluator is needed.
[Guid("8348a66c-c4b9-4821-b85c-da6c4335f813")]
public sealed class RerouteVector4 : Instance<RerouteVector4>, IRerouteNode
{
    [Output(Guid = "3b5e0a53-2449-4ff2-8c11-df6acb75fa37")]
    public readonly Slot<System.Numerics.Vector4> Output = new();

    [Input(Guid = "9421c609-a271-417a-b5e3-388a4863ce28")]
    public readonly InputSlot<System.Numerics.Vector4> Input = new();
}
