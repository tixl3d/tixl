namespace Lib.render.gizmo.@_;

[Guid("9a87212f-f305-4d66-bf0f-4c8983e10e76")]
internal sealed class _OutputPreviewCurves : Instance<_OutputPreviewCurves>
{
    [Output(Guid = "b8efe9cd-2b0c-49c1-984d-d13edc21bd71", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "61d89b08-ed0a-4c6e-a945-246de435651e")]
    public readonly InputSlot<CurveGeometry> Curves = new();
}
