namespace Lib.render.gizmo.@_;

[Guid("dd353ac7-1f11-4dd6-aff5-5c557c695512")]
internal sealed class _VisualizeTBN : Instance<_VisualizeTBN>
{
    [Output(Guid = "82fc9f76-6a6d-4464-a94d-e28a06d82205")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "c1f85fa0-c66c-4c7b-b58f-3f9c6375fa3f")]
    public readonly InputSlot<MeshBuffers> Mesh = new();

    [Input(Guid = "d9287fce-b451-4d5e-83d8-fc8c9d39b9a8")]
    public readonly InputSlot<float> Length = new();

    [Input(Guid = "d1cfa06d-a4bd-4975-90d4-5aa1ca39dc39")]
    public readonly InputSlot<GizmoVisibility> Visibility = new();

}