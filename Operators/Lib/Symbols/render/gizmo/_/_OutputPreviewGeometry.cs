namespace Lib.render.gizmo.@_;

[Guid("91e30c88-bd71-42df-993a-54a0187ccd26")]
internal sealed class _OutputPreviewGeometry : Instance<_OutputPreviewGeometry>
{
    [Output(Guid = "cd1acc57-bf0a-419c-96e0-00da788541dc", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "b8a7277a-cfac-4e46-8619-7054c1cc6369")]
    public readonly InputSlot<MeshGeometry> Geometry = new();
}
