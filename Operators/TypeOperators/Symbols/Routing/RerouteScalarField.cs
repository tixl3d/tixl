namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.ScalarField through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("68cd4a98-f507-43fb-99f3-f8e819887a41")]
public sealed class RerouteScalarField : Instance<RerouteScalarField>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "125fff18-59e3-4121-8161-2f9bf63b5ae6")]
    public readonly Slot<T3.Core.DataTypes.ScalarField> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "ed6b443e-4876-4134-9920-d7192a055c31")]
    public readonly InputSlot<T3.Core.DataTypes.ScalarField> Input = new();
}
