using T3.Core.Rendering;
using T3.Core.Utils;

namespace Lib.flow.context;

/// <summary>
/// Sets a value of <see cref="EvaluationContext.RenderPass"/>: for its subgraph if one is connected (restored afterwards),
/// otherwise for everything evaluated later in this frame, like the [Set*Var] ops.
/// </summary>
[Guid("2294a247-1ae1-4bd5-8061-a7af26bf5347")]
internal sealed class SetRenderPassState : Instance<SetRenderPassState>
{
    /** Stable values: saved graphs store the number, so new entries are only appended. */
    private enum SettableValues
    {
        IsShadowPass = 0,
        JitterOffset = 1,
    }

    [Output(Guid = "0361754e-9952-4d92-a078-d3023bfab1a9")]
    public readonly Slot<Command> Output = new();

    public SetRenderPassState()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var select = Select.GetEnumValue<SettableValues>(context);
        var value = Value.GetValue(context);
        var offset = Offset.GetValue(context);

        if (!SubGraph.HasInputConnections)
        {
            Apply(ref context.RenderPass, select, value, offset);
            return;
        }

        var keepRenderPass = context.RenderPass;
        Apply(ref context.RenderPass, select, value, offset);
        SubGraph.GetValue(context);
        context.RenderPass = keepRenderPass;
    }

    private static void Apply(ref RenderPassState renderPass, SettableValues select, float value, Vector2 offset)
    {
        switch (select)
        {
            case SettableValues.IsShadowPass:
                renderPass.IsShadowPass = value != 0;
                break;
            case SettableValues.JitterOffset:
                renderPass.JitterOffset = offset;
                break;
        }
    }

    [Input(Guid = "ced37909-8b16-43cf-8bb9-d7230068ae89")]
    public readonly InputSlot<Command> SubGraph = new();

    [Input(Guid = "90674b03-4727-42dd-9882-45836016b06d", MappedType = typeof(SettableValues))]
    public readonly InputSlot<int> Select = new();

    [Input(Guid = "5a712820-bbff-434f-8f46-1e3281cd5b95")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "bc20ab5d-79dd-4aab-93ff-6e0e8bef2000")]
    public readonly InputSlot<Vector2> Offset = new();
}
