namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.DataSet.DataSet through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("d1c6b114-4670-4fb6-b906-91275533bcc2")]
public sealed class RerouteDataSet : Instance<RerouteDataSet>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "21409764-e3bb-4bdb-89ad-341f6259bab4")]
    public readonly Slot<T3.Core.DataTypes.DataSet.DataSet> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "a16c3bd5-21cd-4f3f-8ff0-0741638ed6b5")]
    public readonly InputSlot<T3.Core.DataTypes.DataSet.DataSet> Input = new();
}
