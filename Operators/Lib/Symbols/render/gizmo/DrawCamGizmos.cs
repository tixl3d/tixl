namespace Lib.render.gizmo;

[Guid("cdf5dd6a-73dc-4779-a366-df19b69071a6")]
internal sealed class DrawCamGizmos : Instance<DrawCamGizmos>
{
    [Output(Guid = "6cee53fc-92df-4a9e-b519-da857bdf9419")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "f322ca22-8200-449c-b09b-618cddf488d3")]
    public readonly InputSlot<GizmoVisibility> Visibility = new();

    [Input(Guid = "d8ac3b98-5738-41f2-8398-f832103f1dc1")]
    public readonly InputSlot<float> Size = new();


}