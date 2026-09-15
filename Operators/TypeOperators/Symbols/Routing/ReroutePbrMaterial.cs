namespace Types.Routing;

/// <summary>Passes T3.Core.Rendering.Material.PbrMaterial through the .t3 connection; no C# evaluator is needed.</summary>
[Guid("00fbd77b-c94a-4606-8ed1-2db9360c70cf")]
public sealed class ReroutePbrMaterial : Instance<ReroutePbrMaterial>, IRerouteNode
{
    /// <summary>Forwards the input value through the direct connection in the .t3 definition.</summary>
    [Output(Guid = "94deebfe-3ae6-46ac-ad6f-289dec1bdb3b")]
    public readonly Slot<T3.Core.Rendering.Material.PbrMaterial> Output = new();

    /// <summary>Value forwarded unchanged to the output.</summary>
    [Input(Guid = "662a0079-c98c-4e92-95a0-cd71e011cc42")]
    public readonly InputSlot<T3.Core.Rendering.Material.PbrMaterial> Input = new();
}
