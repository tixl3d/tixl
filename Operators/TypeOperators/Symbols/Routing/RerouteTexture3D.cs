namespace Types.Routing;

// Passes T3.Core.DataTypes.Texture3D through the .t3 connection; no C# evaluator is needed.
[Guid("0733b9e4-236e-4c97-b967-01d144c33388")]
public sealed class RerouteTexture3D : Instance<RerouteTexture3D>, IRerouteNode
{
    [Output(Guid = "411f6fe8-1dc2-4031-96a1-2e9e0d533e3a")]
    public readonly Slot<T3.Core.DataTypes.Texture3D> Output = new();

    [Input(Guid = "ca8cd92e-ec5e-4dd0-867e-a45eb4aab8f7")]
    public readonly InputSlot<T3.Core.DataTypes.Texture3D> Input = new();
}
