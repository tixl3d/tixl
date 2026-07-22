using System.Collections.Generic;
using System.Numerics;
using T3.Core.Output;

namespace Lib.render.output;

/// <summary>
/// Declares that its incoming texture belongs on one or more targets of the project's active output setup —
/// surfaces (mapped through the setup's corner-pin) and/or outputs (direct, full-frame). It does no drawing
/// itself: it registers with the <see cref="OutputSinkRegistry"/>, and the host's output manager walks the
/// active outputs, pulls this content, and composites it onto each target. A pure sink: no output slot.
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

    IReadOnlyList<Guid> IOutputSink.GetTargetIds(EvaluationContext context) => TargetIds.GetValue(context) ?? _noTargets;
    void IOutputSink.SetTargets(IReadOnlyList<Guid> targetIds) => TargetIds.SetTypedInputValue(new List<Guid>(targetIds));

    void IOutputSink.RemoveTarget(Guid targetId)
    {
        // Leave procedurally driven target lists alone — only prune the statically stored value.
        if (TargetIds.HasInputConnections)
            return;

        var current = TargetIds.TypedInputValue?.Value;
        if (current == null || !current.Contains(targetId))
            return;

        var next = new List<Guid>(current);
        next.RemoveAll(g => g == targetId);
        TargetIds.SetTypedInputValue(next);
    }
    Vector4 IOutputSink.GetSourceRect(EvaluationContext context) => SourceRect.GetValue(context);
    Vector4 IOutputSink.GetColor(EvaluationContext context) => Color.GetValue(context);
    T3.Core.DataTypes.Texture2D IOutputSink.GetContent(EvaluationContext context) => Texture.GetValue(context);
    void IOutputSink.InvalidateContent() => Texture.InvalidateGraph();
    bool IOutputSink.GetUpdateEnabled(EvaluationContext context) => Update.GetValue(context);
    void IOutputSink.SetUpdateEnabled(bool enabled) => Update.SetTypedInputValue(enabled);

    [Input(Guid = "8a4dd1b3-2e6f-4c25-9d0a-7f3b61c8e942")]
    public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new();

    [Input(Guid = "9a1c4f7e-2d38-4b6a-8e50-1f7c3d9b0a24")]
    public readonly InputSlot<bool> Update = new();

    [Input(Guid = "e2b64f0d-71a9-4d38-b5c6-08af92d31e75")]
    public readonly InputSlot<List<Guid>> TargetIds = new();

    [Input(Guid = "7c3f9d21-4e8a-4b56-9f10-2d6c8b41a093")]
    public readonly InputSlot<Vector4> SourceRect = new();

    [Input(Guid = "1d83a6f2-49c0-4e17-8b5d-c72e90fa4b36")]
    public readonly InputSlot<Vector4> Color = new();

    private static readonly List<Guid> _noTargets = new();
}
