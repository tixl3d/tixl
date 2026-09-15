namespace Types.Routing;

/// <summary>Passes float through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("f41e72fb-82ec-4381-b835-8007f2ce6894")]
public sealed class RerouteFloat : Instance<RerouteFloat>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "82aec913-d773-4f5c-8b6c-05cbef19d2df")]
    public readonly Slot<float> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "9cbfb7e5-8d56-4132-b34e-f105633f90d7")]
    public readonly InputSlot<float> Input = new();
}
