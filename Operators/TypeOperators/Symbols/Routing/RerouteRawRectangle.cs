namespace Types.Routing;

/// <summary>Passes SharpDX.Mathematics.Interop.RawRectangle through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("240b1703-395f-43b6-b9f7-b18e9c1ae979")]
public sealed class RerouteRawRectangle : Instance<RerouteRawRectangle>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "fdead096-3d7f-4f4c-8d8c-649647b0cf8e")]
    public readonly Slot<SharpDX.Mathematics.Interop.RawRectangle> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "052b0b82-9067-423c-bbaf-07bcf8072c54")]
    public readonly InputSlot<SharpDX.Mathematics.Interop.RawRectangle> Input = new();
}
