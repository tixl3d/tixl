namespace Types.Routing;

[Guid("c683e480-28fd-48e8-a30b-2c063d70b699")]
public sealed class RerouteTextureAddressMode : Instance<RerouteTextureAddressMode>, IRerouteNode
{
    [Output(Guid = "9c13fc6e-ab98-4be2-8ce0-e0ad3545d4c0")]
    public readonly Slot<SharpDX.Direct3D11.TextureAddressMode> Output = new();

    [Input(Guid = "1a5c7a33-a8cf-48d7-9936-365f159a69df")]
    public readonly InputSlot<SharpDX.Direct3D11.TextureAddressMode> Input = new();
}
