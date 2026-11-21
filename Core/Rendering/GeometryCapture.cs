using System;
using System.Collections.Generic;
using System.Numerics;
using Buffer = T3.Graphics.Compat.Buffer;

namespace T3.Core.Rendering;

/// <summary>
/// Neutral classification of captured geometry - capture sources never need to know about lasers.
/// <see cref="Auto"/> entries are classified by the consumer from the buffer layout.
/// </summary>
public enum GeometryPrimitiveKind
{
    Auto = 0,
    PointCloud = 1,
    Lines = 2,
    Mesh = 3,
}

/// <summary>
/// Collects the geometry drawn inside a subtree. A consumer (e.g. Lib.laser.camera.LaserCamera)
/// installs an instance on <see cref="T3.Core.Operator.EvaluationContext.GeometryCapture"/> around
/// evaluating its scene; the ops that actually issue GPU draws register what they draw while it is
/// active (the <code>Draw</code> ops from their capture inputs, <code>_DispatchSceneDraws</code>
/// directly per dispatch). This works even for cached subtrees, because command slots re-run every
/// frame while data ops stay frozen by the dirty-flag system.
/// Entries hold non-owning buffer references that are only valid until the buffers are disposed.
/// </summary>
public sealed class GeometryCapture
{
    /// <summary>Bounded scratch - a scene with more unique draws per frame is truncated.</summary>
    public const int MaxEntries = 256;

    /// <summary>
    /// Buffers that must never be captured - laser-frame data drawn back into the scene as a
    /// preview (LaserPointsToBuffer output feeding a draw op inside the captured subtree).
    /// Capturing them turns the camera's own output into an input and the frame echoes itself
    /// every frame. Static and source-based: the preview draw may evaluate outside the capture
    /// pass that would have received the exclusion.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Buffer, object> ExcludedSources = new();

    public static void MarkExcludedFromCapture(Buffer buffer)
    {
        // GetValue is the idempotent add: the same buffer is uploaded (and marked) every frame,
        // and CWT.Add would throw on the repeat.
        if (buffer != null)
            ExcludedSources.GetValue(buffer, _ => null);
    }

    public static bool IsExcludedFromCapture(Buffer buffer)
    {
        return buffer != null && ExcludedSources.TryGetValue(buffer, out _);
    }

    public bool IsActive { get; private set; }

    public IReadOnlyList<Entry> Entries => _entries;

    public readonly record struct Entry(Buffer VertexBuffer, Buffer IndexBuffer, GeometryPrimitiveKind Kind, Matrix4x4 ObjectToWorld,
                                        int FirstElement, int ElementCount, float UniformScale = 1f);

    public void BeginPass()
    {
        _entries.Clear();
        _seen.Clear();
        IsActive = true;
    }

    public void EndPass()
    {
        IsActive = false;
    }

    /// <summary>Registers a draw. The same buffer drawn repeatedly under the same transform is
    /// registered once; under a different transform it is registered again. <paramref name="firstElement"/>
    /// and <paramref name="elementCount"/> bound the drawn slice so consumers don't read the buffer's
    /// (usually larger, zero-filled) capacity as phantom geometry at the origin; a negative count
    /// means the draw issued its count indirectly and the whole buffer has to be read.
    /// <paramref name="uniformScale"/> carries a draw-time size multiplier the draw applies outside
    /// the buffer (e.g. the points op's PointSize uniform) - content the buffer alone doesn't express.</summary>
    public void Register(Buffer vertexBuffer, Buffer indexBuffer, GeometryPrimitiveKind kind, Matrix4x4 objectToWorld,
                         int firstElement = 0, int elementCount = -1, float uniformScale = 1f)
    {
        if (!IsActive || vertexBuffer == null || _entries.Count >= MaxEntries)
            return;

        var entry = new Entry(vertexBuffer, indexBuffer, kind, objectToWorld, firstElement, elementCount, uniformScale);
        if (!_seen.Add(entry))
            return;

        _entries.Add(entry);
    }

    private readonly List<Entry> _entries = new();
    private readonly HashSet<Entry> _seen = new();
}
