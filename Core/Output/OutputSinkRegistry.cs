#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Operator;

namespace T3.Core.Output;

/// <summary>
/// A graph node that declares "this content belongs on this output/surface" — it does no drawing.
/// It registers itself with the <see cref="OutputSinkRegistry"/> so the host's output manager can
/// walk backwards from the active outputs, pull the content, and composite it. Implemented by the
/// SendToOutput operator; the getters read the op's input slots so bindings can be driven or static.
/// </summary>
public interface IOutputSink
{
    Guid GetOutputId(EvaluationContext context);
    Guid GetSurfaceId(EvaluationContext context);
    Vector4 GetColor(EvaluationContext context);
    Texture2D? GetContent(EvaluationContext context);

    /// <summary>Marks the content input graph dirty so a following <see cref="GetContent"/> re-evaluates
    /// time-dependent upstream ops (the manager pulls content manually, outside the normal output path).</summary>
    void InvalidateContent();
}

/// <summary>
/// The set of live <see cref="IOutputSink"/> instances. Sinks add themselves on construction and
/// remove themselves on dispose, so the registry survives operator hot-reloads (which recreate the
/// instances). Insertion order is preserved; the output manager resolves per output/surface.
/// </summary>
public static class OutputSinkRegistry
{
    public static void Register(IOutputSink sink)
    {
        if (!_sinks.Contains(sink))
            _sinks.Add(sink);
    }

    public static void Unregister(IOutputSink sink)
    {
        _sinks.Remove(sink);
    }

    public static IReadOnlyList<IOutputSink> Sinks => _sinks;

    private static readonly List<IOutputSink> _sinks = [];
}
