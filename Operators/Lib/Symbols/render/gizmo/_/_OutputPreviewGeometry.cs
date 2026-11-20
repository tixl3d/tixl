namespace Lib.render.gizmo.@_;

[Guid("70c1d6d4-1e97-4c9d-93c8-93702000ab5b")]
internal sealed class _OutputPreviewGeometry : Instance<_OutputPreviewGeometry>
{
    [Output(Guid = "10c42d0f-737b-4a23-90f1-f20eb5390eb4", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "76e0cf44-0f1a-451f-a0ce-bd5a9c2fb5ff")]
    public readonly InputSlot<Geometry> Geometry = new();
}
