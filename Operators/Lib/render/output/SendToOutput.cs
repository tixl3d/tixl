using System.Numerics;
using T3.Core.Output;

namespace Lib.render.output;

/// <summary>
/// Declares that its incoming texture should be shown on a setup output (and optionally a specific
/// surface). It does no drawing itself — it registers with the <see cref="OutputSinkRegistry"/>, and
/// the host's output manager walks the active outputs, pulls this content, and composites it through
/// the surface corner-pin mappings. A pure sink: no output slot to wire.
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

    Guid IOutputSink.GetOutputId(EvaluationContext context) => OutputRef.GetValue(context);
    Guid IOutputSink.GetSurfaceId(EvaluationContext context) => SurfaceRef.GetValue(context);
    Vector4 IOutputSink.GetColor(EvaluationContext context) => Color.GetValue(context);
    T3.Core.DataTypes.Texture2D IOutputSink.GetContent(EvaluationContext context) => Texture.GetValue(context);
    void IOutputSink.InvalidateContent() => Texture.InvalidateGraph();

    [Input(Guid = "8a4dd1b3-2e6f-4c25-9d0a-7f3b61c8e942")]
    public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new();

    [Input(Guid = "5c7e19a4-8b3d-4f6e-a201-93d5c4b7f180")]
    public readonly InputSlot<Guid> OutputRef = new();

    [Input(Guid = "e2b64f0d-71a9-4d38-b5c6-08af92d31e75")]
    public readonly InputSlot<Guid> SurfaceRef = new();

    [Input(Guid = "1d83a6f2-49c0-4e17-8b5d-c72e90fa4b36")]
    public readonly InputSlot<Vector4> Color = new();
}
