#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Operator;

namespace T3.Core.Output;

/// <summary>
/// A graph node that supplies pixels to the output setup. It does no drawing: it registers itself with the
/// <see cref="OutputSinkRegistry"/> so the host's output manager can pull the content and composite it.
/// Routing (which surface shows what) is setup data keyed to this op's SymbolChild, not state on the op.
/// </summary>
public interface IOutputSink
{
    Vector4 GetColor(EvaluationContext context);
    Texture2D? GetContent(EvaluationContext context);

    /// <summary>When false the host stops invalidating this content, freezing it at its last frame.</summary>
    bool GetUpdateEnabled(EvaluationContext context);

    void SetUpdateEnabled(bool enabled);

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
    /// <summary>
    /// Bumped whenever membership changes. Lets hosts skip work that can only be invalidated by a send
    /// appearing or disappearing — notably scanning the symbol library to see whether an op was deleted.
    /// </summary>
    public static int Version { get; private set; }

    public static void Register(IOutputSink sink)
    {
        if (_sinks.Contains(sink))
            return;

        _sinks.Add(sink);
        Version++;
    }

    public static void Unregister(IOutputSink sink)
    {
        if (_sinks.Remove(sink))
            Version++;
    }

    public static IReadOnlyList<IOutputSink> Sinks => _sinks;

    private static readonly List<IOutputSink> _sinks = [];
}
