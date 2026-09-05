namespace Lib.render.gizmo.@_;

[Guid("e5588101-5686-4b02-ab7d-e58199ba552e")]
internal sealed class _OutputWindowGrid : Instance<_OutputWindowGrid>
{
    [Output(Guid = "34f1eab4-9379-4b4e-a160-1bfed9103597", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();
}