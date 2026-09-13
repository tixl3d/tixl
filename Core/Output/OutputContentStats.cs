#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.DataTypes.Vector;

namespace T3.Core.Output;

/// <summary>
/// Per-frame record of the resolutions each send's content was pulled at. The host notes every pull; the op
/// reads back whether it was asked for more than one size in the same frame.
///
/// Why it matters: content is invalidated once per frame, so the *first* pull evaluates the graph and every
/// later one gets that texture back. A send routed to two outputs of different sizes therefore renders once —
/// at whichever output composited first — and the other silently shows that size. Cheap, but not what the
/// setup says, so the op says it out loud rather than leaving it to be found on a wall.
/// </summary>
public static class OutputContentStats
{
    /// <summary>Notes that <paramref name="symbolChildId"/>'s content was pulled at this resolution.</summary>
    public static void NotePull(Guid symbolChildId, Int2 resolution, int frame)
    {
        lock (_pulls)
        {
            if (frame != _frame)
            {
                _frame = frame;
                _pulls.Clear();
            }

            if (!_pulls.TryGetValue(symbolChildId, out var entry))
            {
                _pulls[symbolChildId] = new Pull(resolution, resolution);
                return;
            }

            if (entry.First.Width == resolution.Width && entry.First.Height == resolution.Height)
                return;

            _pulls[symbolChildId] = entry with { Other = resolution };
        }
    }

    /// <summary>True when this send was asked for two different sizes in the last recorded frame.</summary>
    public static bool TryGetSizeConflict(Guid symbolChildId, out Int2 rendered, out Int2 other)
    {
        lock (_pulls)
        {
            if (_pulls.TryGetValue(symbolChildId, out var entry)
                && (entry.First.Width != entry.Other.Width || entry.First.Height != entry.Other.Height))
            {
                rendered = entry.First;
                other = entry.Other;
                return true;
            }
        }

        rendered = default;
        other = default;
        return false;
    }

    private readonly record struct Pull(Int2 First, Int2 Other);

    private static readonly Dictionary<Guid, Pull> _pulls = new();
    private static int _frame = -1;
}
