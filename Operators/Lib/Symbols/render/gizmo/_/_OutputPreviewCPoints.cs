namespace Lib.render.gizmo.@_;

[Guid("0c3eb144-a156-4d72-bc7e-afdf13ba61f7")]
internal sealed class _OutputPreviewCPoints : Instance<_OutputPreviewCPoints>
{
    [Output(Guid = "30a400c7-e467-499a-af4d-7a880d75f74e", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "37e80d44-7206-4046-9c11-2a8ba34e6929")]
    public readonly InputSlot<StructuredList> Points = new();
}
