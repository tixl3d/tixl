using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction.TransformGizmos;
using T3.Editor.UiModel;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.OutputUi;

internal abstract class OutputUi<T> : IOutputUi
{
    public Symbol.OutputDefinition OutputDefinition { get; set; }
    public Guid Id => OutputDefinition.Id;
    public Type Type { get; } = typeof(T);
    public virtual Type PresentedType => Type;
    public Vector2 PosOnCanvas { get; set; } = Vector2.Zero;
    public Vector2 Size { get; set; } = SymbolUi.Child.DefaultOpSize;

    public abstract IOutputUi Clone();

    public void DrawValue(ISlot slot, EvaluationContext context, string viewId, bool recompute)
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        {
            TransformGizmoHandling.SetDrawList(drawList);
            if (recompute)
            {
                Recompute(slot, context);
            }
        }
        
        drawList.ChannelsSetCurrent(0);
        {
            DrawTypedValue(slot, viewId);
        }
        drawList.ChannelsMerge();
        TransformGizmoHandling.RestoreDrawList();
    }

    protected virtual void Recompute(ISlot slot, EvaluationContext context)
    {
        StartInvalidation(slot, context);
        slot.Update(context);
    }

    /// <summary>
    /// Evaluates for a data view (a table or stats) that has no 3D view to place transform gizmos in; they would
    /// otherwise project through whatever viewport is set and draw distorted behind the data.
    /// </summary>
    protected void EvaluateWithoutGizmos(ISlot slot, EvaluationContext context)
    {
        var showGizmos = context.ShowGizmos;
        context.ShowGizmos = GizmoVisibility.Off;
        try
        {
            StartInvalidation(slot, context);
            slot.Update(context);
        }
        finally
        {
            context.ShowGizmos = showGizmos;
        }
    }

    protected abstract void DrawTypedValue(ISlot slot, string viewId);

    protected void StartInvalidation(ISlot slot, EvaluationContext context)
    {
        context.NotifyPullResolution();
        DirtyFlag.GlobalInvalidationTick++;
        slot.InvalidateGraph();
    }
}