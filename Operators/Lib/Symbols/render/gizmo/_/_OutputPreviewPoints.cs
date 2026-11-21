namespace Lib.render.gizmo.@_;

[Guid("6bf2c69e-0ab5-40b2-8ea1-b469333f4247")]
internal sealed class _OutputPreviewPoints : Instance<_OutputPreviewPoints>
{
    [Output(Guid = "fbe0ea36-924f-485e-8606-4457e666a955", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "89d49547-bbf2-4acf-80b8-aba4175acade")]
    public readonly InputSlot<BufferWithViews> Points = new();
}
