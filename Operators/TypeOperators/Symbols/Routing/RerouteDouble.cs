namespace Types.Routing;

// Passes double through the .t3 connection; no C# evaluator is needed.
[Guid("52a23d05-009a-44ba-9047-191930bffe05")]
public sealed class RerouteDouble : Instance<RerouteDouble>, IRerouteNode
{
    [Output(Guid = "c04fffa6-ea6c-48ef-9017-c1973dbe72b6")]
    public readonly Slot<double> Output = new();

    [Input(Guid = "df3a357b-be56-42eb-a088-31e35b1af723")]
    public readonly InputSlot<double> Input = new();
}
