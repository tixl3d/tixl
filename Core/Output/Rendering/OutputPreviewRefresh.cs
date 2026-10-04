#nullable enable
using System;
using System.Collections.Generic;

namespace T3.Core.Output.Rendering;

/// <summary>
/// Rations how often the setup's previews evaluate. Presenting an output earns a full evaluation every frame;
/// a card on the Board does not — and since every send renders its own scene, a card per send costs their sum
/// on every frame, which made the setup view by far the slowest thing in the editor.
/// <para>So a preview renders <b>once</b>, and only what the host marks as being edited keeps rendering. The
/// first fill is staggered over frames (one per frame), and several edited at once take turns, so a frame never
/// carries more than a single send's cost.</para>
/// </summary>
public static class OutputPreviewRefresh
{
    /// <summary>Starts this frame's set of previews that must stay live; <see cref="AddPriority"/> fills it.</summary>
    public static void BeginPriorityFrame()
    {
        _priority.Clear();
    }

    /// <summary>Marks a preview as being edited: it re-renders every frame, or in turn when several are.</summary>
    public static void AddPriority(Guid id)
    {
        if (id != Guid.Empty)
            _priority.Add(id);
    }

    /// <summary>Whether this preview may evaluate now; false means the caller shows its last content instead.</summary>
    public static bool ShouldRefresh(Guid id)
    {
        var frame = OutputFrame.Index;
        if (frame != _frame)
        {
            _frame = frame;
            _refreshesThisFrame = 0;

            // A send appearing or going away is the one event that can make every preview wrong at once.
            if (_registryVersion != ContentSupplierRegistry.Version)
            {
                _registryVersion = ContentSupplierRegistry.Version;
                _renderedFrameById.Clear();
            }
        }

        var hasRendered = _renderedFrameById.ContainsKey(id);
        if (hasRendered && !_priority.Contains(id))
            return false;

        // The one preview being edited stays live; everything else — first fills and several edited at once —
        // goes through the per-frame budget, so their costs never land on the same frame.
        if (hasRendered && _priority.Count == 1)
        {
            _renderedFrameById[id] = frame;
            return true;
        }

        if (_refreshesThisFrame >= MaxRefreshesPerFrame || !IsNextInTurn(id, hasRendered, frame))
            return false;

        _renderedFrameById[id] = frame;
        _refreshesThisFrame++;
        return true;
    }

    /// <summary>Forgets a preview, so it renders once more when asked for again.</summary>
    public static void Invalidate(Guid id)
    {
        _renderedFrameById.Remove(id);
    }

    public static void InvalidateAll()
    {
        _renderedFrameById.Clear();
    }

    /// <summary>
    /// Whether this is the preview whose turn it is: the one waiting longest. Without the turn, whichever
    /// preview is drawn first would take the budget every frame and the others would never refresh.
    /// </summary>
    private static bool IsNextInTurn(Guid id, bool hasRendered, int frame)
    {
        // Never rendered: nothing to compare against, and a blank card is the most urgent case there is.
        if (!hasRendered)
            return true;

        var renderedFrame = _renderedFrameById[id];
        foreach (var other in _priority)
        {
            if (other != id && _renderedFrameById.TryGetValue(other, out var otherFrame) && otherFrame < renderedFrame)
                return false;
        }

        return renderedFrame < frame;
    }

    /** One per frame, so a stack of previews can never add up to more than a single send's cost. */
    private const int MaxRefreshesPerFrame = 1;

    private static readonly Dictionary<Guid, int> _renderedFrameById = new();
    private static readonly HashSet<Guid> _priority = [];
    private static int _frame = -1;
    private static int _refreshesThisFrame;
    private static int _registryVersion = -1;
}
