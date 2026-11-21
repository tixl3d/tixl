namespace Lib.render.gizmo.@_;

[Guid("63eedd22-6495-47dc-817c-801b512e93f7")]
internal sealed class _DrawGeometryHighlight : Instance<_DrawGeometryHighlight>
{
    [Output(Guid = "7a94a219-a4ef-45ff-8743-282f01038f79")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "b37da34f-e932-4541-bead-2d69f663c756")]
    public readonly InputSlot<Geometry> Geometry = new();

    [Input(Guid = "5138f02e-f261-462b-b82b-9dda3e7a3ef8")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "b4e92de5-f4b2-4f78-b2e6-6b025f036f47")]
    public readonly InputSlot<Vector4> HiddenColor = new();

    [Input(Guid = "7031d4a4-22b8-43ed-bf3d-53ce15e6ea75")]
    public readonly InputSlot<float> LineWidth = new();

    [Input(Guid = "e0b0507b-7f81-431d-a82e-b1a83e09bd9c")]
    public readonly InputSlot<float> PointSize = new();
}
