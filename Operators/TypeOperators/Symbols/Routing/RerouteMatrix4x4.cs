namespace Types.Routing;

[Guid("437441f1-2da5-49aa-945f-c941b22ffc5a")]
public sealed class RerouteMatrix4x4 : Instance<RerouteMatrix4x4>, IRerouteNode
{
    [Output(Guid = "92c4f003-1d8a-4a7c-b647-042e4658ee5f")]
    public readonly Slot<System.Numerics.Matrix4x4> Output = new();

    [Input(Guid = "1b126c61-f8ea-4782-883d-e9099b37bf19")]
    public readonly InputSlot<System.Numerics.Matrix4x4> Input = new();
}
