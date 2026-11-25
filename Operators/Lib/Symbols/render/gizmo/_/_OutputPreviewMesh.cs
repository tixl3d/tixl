namespace Lib.render.gizmo.@_;

[Guid("ca23d69c-9dbf-421a-9c75-205c3eab3101")]
internal sealed class _OutputPreviewMesh : Instance<_OutputPreviewMesh>
{
    [Output(Guid = "06c27969-043e-4d77-90b8-0ae69962c640", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "f13f5753-d17d-4234-a605-a99fab1a57b5")]
    public readonly InputSlot<MeshBuffers> Mesh = new();
}
