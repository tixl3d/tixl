using System.Numerics;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;

namespace Lib.render.output;

/// <summary>
/// Supplies a texture to the project's active output setup. Routing lives in the setup, not here: the setup
/// holds a ContentSource standing 1:1 with this op, slices cut rectangles from it, and surfaces name the slice
/// they show. So this op only says "here are the pixels" — it registers with the <see cref="OutputSinkRegistry"/>
/// and the host's output manager pulls from it. A pure sink: no output slot.
/// </summary>
[Guid("0b8f2d4e-6a1c-47d3-9f5e-8c2a1b7d4e60")]
internal sealed class SendToOutput : Instance<SendToOutput>, IOutputSink, IStatusProvider
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

    /// <summary>
    /// Pulls the content, rendered at <see cref="Resolution"/> when one is set. Left at 0×0 the host's requested
    /// resolution stands — the canvas of the output this content is routed to — so an auto-sized render target
    /// upstream follows the projector rather than needing a size of its own.
    /// </summary>
    T3.Core.DataTypes.Texture2D IOutputSink.GetContent(EvaluationContext context)
    {
        var requested = Resolution.GetValue(context);
        if (requested.Width <= 0 || requested.Height <= 0)
            return Texture.GetValue(context);

        var inherited = context.RequestedResolution;
        context.RequestedResolution = requested;
        var texture = Texture.GetValue(context);
        context.RequestedResolution = inherited;
        return texture;
    }
    void IOutputSink.InvalidateContent() => Texture.InvalidateGraph();
    bool IOutputSink.GetUpdateEnabled(EvaluationContext context) => Update.GetValue(context);
    void IOutputSink.SetUpdateEnabled(bool enabled) => Update.SetTypedInputValue(enabled);
    Int2 IOutputSink.GetResolution(EvaluationContext context) => Resolution.GetValue(context);
    void IOutputSink.SetResolution(Int2 resolution) => Resolution.SetTypedInputValue(resolution);

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        // Named rather than discarded: this package has a namespace called `_`, which `out _` binds to.
        return OutputContentStats.TryGetSizeConflict(SymbolChildId, out var rendered, out var other)
                   ? IStatusProvider.StatusLevel.Warning
                   : IStatusProvider.StatusLevel.Success;
    }

    string? IStatusProvider.GetStatusMessage()
    {
        if (!OutputContentStats.TryGetSizeConflict(SymbolChildId, out var rendered, out var other))
            return null;

        return $"Routed to outputs of different sizes ({rendered.Width}×{rendered.Height} and {other.Width}×{other.Height}).\n"
               + "The content renders once per frame, at the first of them, so the other shows that size.\n"
               + "Set this op's Resolution to pick one deliberately, or give each output its own send.";
    }

    [Input(Guid = "8a4dd1b3-2e6f-4c25-9d0a-7f3b61c8e942")]
    public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new();

    [Input(Guid = "9a1c4f7e-2d38-4b6a-8e50-1f7c3d9b0a24")]
    public readonly InputSlot<bool> Update = new();

    [Input(Guid = "1d83a6f2-49c0-4e17-8b5d-c72e90fa4b36")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "3c7f1e58-0a94-4d62-b8f3-2e5d9a0c4b17")]
    public readonly InputSlot<Int2> Resolution = new();
}
