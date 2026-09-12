namespace Types.Routing;

// Passes System.Numerics.Vector2 through the .t3 connection; no C# evaluator is needed.
[Guid("601cb757-45e5-422c-a4fa-f8e8faf24720")]
public sealed class RerouteVector2 : Instance<RerouteVector2>, IRerouteNode
{
    [Output(Guid = "c7a133a1-b275-4d82-8350-27e66651bb3a")]
    public readonly Slot<System.Numerics.Vector2> Output = new();

    [Input(Guid = "e8024598-2aed-4711-9e71-fb5a2bbfd469")]
    public readonly InputSlot<System.Numerics.Vector2> Input = new();
}
