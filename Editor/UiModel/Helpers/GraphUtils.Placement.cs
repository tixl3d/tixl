#nullable enable
using System.Numerics;

namespace T3.Editor.UiModel.Helpers;

internal static partial class GraphUtils
{
    /// <summary>
    /// Finds a canvas position near <paramref name="preferredPosition"/> whose
    /// <paramref name="size"/> rectangle doesn't overlap any existing child node of
    /// <paramref name="symbolUi"/>. Starts at the preferred spot, steps downward, and
    /// shifts to a fresh column when one fills up. For nodes created without a mouse
    /// position (e.g. a recording session adding a <c>LoadDataClip</c>), so successive
    /// creations don't stack on top of each other.
    /// </summary>
    public static Vector2 FindFreePosition(SymbolUi symbolUi, Vector2 preferredPosition, Vector2 size)
    {
        const float margin = 20f;
        const int maxRows = 200;
        const int maxColumns = 50;

        var candidate = preferredPosition;
        for (var column = 0; column < maxColumns; column++)
        {
            for (var row = 0; row < maxRows; row++)
            {
                if (!OverlapsAnyChild(symbolUi, candidate, size, margin))
                    return candidate;

                candidate.Y += size.Y + margin;
            }

            // Column is full — shift right and restart from the preferred height.
            candidate = new Vector2(candidate.X + size.X + margin * 2f, preferredPosition.Y);
        }

        return candidate;
    }

    private static bool OverlapsAnyChild(SymbolUi symbolUi, Vector2 pos, Vector2 size, float margin)
    {
        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            var otherPos = childUi.PosOnCanvas;
            var otherSize = childUi.Size;

            var overlapsX = pos.X < otherPos.X + otherSize.X + margin && pos.X + size.X + margin > otherPos.X;
            var overlapsY = pos.Y < otherPos.Y + otherSize.Y + margin && pos.Y + size.Y + margin > otherPos.Y;
            if (overlapsX && overlapsY)
                return true;
        }

        return false;
    }
}
