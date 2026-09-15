namespace Types.Routing;

/// <summary>Passes SharpDX.Mathematics.Interop.RawViewportF through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("38c3fe02-c841-428d-aeb0-689a57e15388")]
public sealed class RerouteRawViewportF : Instance<RerouteRawViewportF>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "23809fbb-c3de-4598-8b1c-ff0dea5344e3")]
    public readonly Slot<SharpDX.Mathematics.Interop.RawViewportF> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "f3222283-0e2d-49ed-a52b-7537d0d4386a")]
    public readonly InputSlot<SharpDX.Mathematics.Interop.RawViewportF> Input = new();
}
