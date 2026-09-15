namespace Types.Routing;

/// <summary>Passes T3.Core.DataTypes.RenderTargetReference through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("fbc12c1a-ec4f-46a5-91c3-31ddd923bf5f")]
public sealed class RerouteRenderTargetReference : Instance<RerouteRenderTargetReference>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "f07e866b-ce6d-4342-9c90-d1ac16cdf135")]
    public readonly Slot<T3.Core.DataTypes.RenderTargetReference> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "d2fff25e-1ad4-4c45-a6da-08692a7cef10")]
    public readonly InputSlot<T3.Core.DataTypes.RenderTargetReference> Input = new();
}
