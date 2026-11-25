#nullable enable
using T3.Core.Rendering;

namespace Lib.flow.context;

/// <summary>
/// Reads what the output window asks its preview to draw (<see cref="EvaluationContext.PreviewOverlay"/>): one flag per
/// overlay and the table's selected and hovered elements as small geometries. Used inside preview ops like
/// [_OutputPreviewGeometry]; outside an output window the flags have their defaults and the geometries are null.
/// </summary>
/** Re-read on every pull, like [GetRenderPassState]: the same op can render in several output windows per frame. */
[Guid("333f87ae-4641-477f-bb61-fc25d14552d6")]
internal sealed class GetPreviewState : Instance<GetPreviewState>
{
    [Output(Guid = "fa63f9a0-4203-4434-b638-13eeb743c2f1", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<bool> Wireframe = new();

    [Output(Guid = "aac0cca5-0f05-4f66-8fcb-d7564c9a6c02", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<bool> Points = new();

    [Output(Guid = "5d76e2a1-3d04-4461-9260-fdfba52ebfae", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<bool> PointIndices = new();

    [Output(Guid = "cb70f1f4-44db-46d8-a894-954e1b3a71fe", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<bool> Selection = new();

    [Output(Guid = "006658a5-795f-43ed-8d80-c8eafee79a56", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<bool> Hover = new();

    [Output(Guid = "1bddb185-9043-4a4c-8c46-9199e4aa47da", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<bool> Normals = new();

    [Output(Guid = "b82f34dc-0917-43b0-898c-c1bd395773ba", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Geometry?> Selected = new();

    [Output(Guid = "27ff19b0-ced8-4940-9bd8-25b1cd61c8b0", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Geometry?> Hovered = new();

    public GetPreviewState()
    {
        Wireframe.UpdateAction = Update;
        Points.UpdateAction = Update;
        PointIndices.UpdateAction = Update;
        Selection.UpdateAction = Update;
        Hover.UpdateAction = Update;
        Normals.UpdateAction = Update;
        Selected.UpdateAction = Update;
        Hovered.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var state = context.PreviewOverlay;
        Wireframe.Value = state.Shows(PreviewOverlays.Wireframe);
        Points.Value = state.Shows(PreviewOverlays.Points);
        PointIndices.Value = state.Shows(PreviewOverlays.PointIndices);
        Selection.Value = state.Shows(PreviewOverlays.Selection);
        Hover.Value = state.Shows(PreviewOverlays.Hover);
        Normals.Value = state.Shows(PreviewOverlays.Normals);
        Selected.Value = state.Selected;
        Hovered.Value = state.Hovered;

    }
}
