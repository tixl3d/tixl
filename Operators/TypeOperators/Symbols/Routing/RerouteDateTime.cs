namespace Types.Routing;

// Passes System.DateTime through the .t3 connection; no C# evaluator is needed.
[Guid("9cdd6e12-84df-4cb8-9def-4a2ce35db768")]
public sealed class RerouteDateTime : Instance<RerouteDateTime>, IRerouteNode
{
    [Output(Guid = "eab0846c-21fc-433b-840f-543081a8f569")]
    public readonly Slot<System.DateTime> Output = new();

    [Input(Guid = "01686d8a-998e-4819-9047-51f87e6b1209")]
    public readonly InputSlot<System.DateTime> Input = new();
}
