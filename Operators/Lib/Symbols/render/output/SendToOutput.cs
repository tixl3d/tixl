using System.Numerics;
using T3.Core.Output;

namespace Lib.render.output;

/// <summary>
/// Supplies a texture to the project's active output setup. Routing lives in the setup, not here: the setup
/// holds a ContentSource standing 1:1 with this op, slices cut rectangles from it, and surfaces name the slice
/// they show. So this op only says "here are the pixels" — it registers with the <see cref="OutputSinkRegistry"/>
/// and the host's output manager pulls from it. A pure sink: no output slot.
/// </summary>
[Guid("0b8f2d4e-6a1c-47d3-9f5e-8c2a1b7d4e60")]
internal sealed class SendToOutput : Instance<SendToOutput>, IOutputSink
{
    public SendToOutput()
    {
        OutputSinkRegistry.Register(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            OutputSinkRegistry.Unregister(this);

        base.Dispose(disposing);
    }

    Vector4 IOutputSink.GetColor(EvaluationContext context) => Color.GetValue(context);
    T3.Core.DataTypes.Texture2D IOutputSink.GetContent(EvaluationContext context) => Texture.GetValue(context);
    void IOutputSink.InvalidateContent() => Texture.InvalidateGraph();
    bool IOutputSink.GetUpdateEnabled(EvaluationContext context) => Update.GetValue(context);
    void IOutputSink.SetUpdateEnabled(bool enabled) => Update.SetTypedInputValue(enabled);

    [Input(Guid = "8a4dd1b3-2e6f-4c25-9d0a-7f3b61c8e942")]
    public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new();

    [Input(Guid = "9a1c4f7e-2d38-4b6a-8e50-1f7c3d9b0a24")]
    public readonly InputSlot<bool> Update = new();

    [Input(Guid = "1d83a6f2-49c0-4e17-8b5d-c72e90fa4b36")]
    public readonly InputSlot<Vector4> Color = new();
}
