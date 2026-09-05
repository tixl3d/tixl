namespace Lib.render.gizmo;

[Guid("1998f949-5c0a-4f39-82cf-b0bda31f7f21")]
internal sealed class DrawSphereGizmo : Instance<DrawSphereGizmo>
{
    [Output(Guid = "0b43d459-2c94-4d5e-a75a-61d38d93118b")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "15f73f4f-a4f1-4ff0-aba5-074ea2d328bc")]
    public readonly InputSlot<float> Radius = new InputSlot<float>();

    [Input(Guid = "a407fbe3-1f1b-4466-b4c2-511733046d00")]
    public readonly InputSlot<float> InnerRadius = new InputSlot<float>();

    [Input(Guid = "188c2244-e55a-4c55-be93-cccbd6fd4e41")]
    public readonly InputSlot<Vector4> Color = new InputSlot<Vector4>();


}