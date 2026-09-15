namespace Types.Routing;

/// <summary>Passes SharpDX.Direct3D11.BlendOperation through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("66468f0e-0839-4cc6-b9c0-d805648444c7")]
public sealed class RerouteBlendOperation : Instance<RerouteBlendOperation>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "212223e2-8701-4c1d-81f0-fa0b26a12dfe")]
    public readonly Slot<SharpDX.Direct3D11.BlendOperation> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "09b1d884-1b06-42fc-803d-43911ef2cd9a")]
    public readonly InputSlot<SharpDX.Direct3D11.BlendOperation> Input = new();
}
