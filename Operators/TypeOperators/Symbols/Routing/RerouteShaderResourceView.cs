namespace Types.Routing;

/// <summary>Passes SharpDX.Direct3D11.ShaderResourceView through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("84205996-1180-423d-934e-6b86dd232edd")]
public sealed class RerouteShaderResourceView : Instance<RerouteShaderResourceView>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "e08b4add-fa46-492f-8548-6ad4234aeff3")]
    public readonly Slot<SharpDX.Direct3D11.ShaderResourceView> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "3ab89bd7-3dc8-470a-a927-552f4f0e456e")]
    public readonly InputSlot<SharpDX.Direct3D11.ShaderResourceView> Input = new();
}
