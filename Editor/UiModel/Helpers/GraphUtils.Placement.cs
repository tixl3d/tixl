#nullable enable
using System.Numerics;
using T3.Editor.Gui;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.UiModel.ProjectHandling;

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

    /// <summary>
    /// Position for an op created without a mouse position or a related op to sit next to: the free spot nearest
    /// to the bottom right of the visible graph. When <paramref name="symbolUi"/> isn't the composition on view, or
    /// nothing in view is free, the result can lie outside the view.
    /// </summary>
    public static Vector2 FindFreePositionInView(SymbolUi symbolUi)
    {
        var size = MagGraphItem.GridSize;
        var projectView = ProjectView.Focused;
        if (projectView?.GraphView == null || projectView.CompositionInstance?.Symbol.Id != symbolUi.Symbol.Id)
            return FindFreePosition(symbolUi, GetPositionBelowExistingChildren(symbolUi, new Vector2(0, 200)), size);

        var canvas = projectView.GraphView.Canvas;

        // The caller's ImGui window usually isn't the graph's, so this must not query the current window.
        var visible = canvas.GetLastVisibleCanvasArea();
        var viewEdgeInset = new Vector2(ViewEdgeInsetOnScreen * T3Ui.UiScaleFactor) / canvas.Scale;
        var cornerPosition = visible.Max - viewEdgeInset - size;
        var minPosition = visible.Min + viewEdgeInset;
        var layoutItems = projectView.GraphView is MagGraphView { Context: not null } magGraphView
                              ? magGraphView.Context.Layout.Items
                              : null;

        // Grid distance from the corner grows ring by ring, so the first free cell is close to the corner.
        var cellStep = size + new Vector2(FreeSpotMargin);
        for (var ring = 0;; ring++)
        {
            var isRingInView = false;
            for (var index = 0; index <= ring; index++)
            {
                if (IsFreeCell(ring, index, out var position) || IsFreeCell(index, ring, out position))
                    return position;
            }

            if (!isRingInView)
                break;

            bool IsFreeCell(int column, int row, out Vector2 cellPosition)
            {
                cellPosition = cornerPosition - cellStep * new Vector2(column, row);
                if (cellPosition.X < minPosition.X || cellPosition.Y < minPosition.Y)
                    return false;

                isRingInView = true;
                return !OverlapsAnyChild(symbolUi, cellPosition, size, FreeSpotMargin)
                       && !OverlapsAnyLayoutItem(layoutItems, cellPosition, size);
            }
        }

        return FindFreePosition(symbolUi, visible.GetCenter(), size);
    }

    /// <summary>
    /// Anchor for ops created without a mouse position: directly below the bounding box of the existing
    /// children, so the new op lands inside the project's populated canvas region. A fixed coordinate
    /// like (0, 200) can be arbitrarily far outside the bounds of a composition whose ops live elsewhere.
    /// </summary>
    public static Vector2 GetPositionBelowExistingChildren(SymbolUi symbolUi, Vector2 fallback)
    {
        const float spacingY = 80f;
        var minX = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            minX = MathF.Min(minX, childUi.PosOnCanvas.X);
            maxY = MathF.Max(maxY, childUi.PosOnCanvas.Y + childUi.Size.Y);
        }

        return float.IsNegativeInfinity(maxY)
                   ? fallback
                   : new Vector2(minX, maxY + spacingY);
    }

    private static bool OverlapsAnyChild(SymbolUi symbolUi, Vector2 pos, Vector2 size, float margin)
    {
        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            if (Overlaps(pos, size, childUi.PosOnCanvas, childUi.Size, margin))
                return true;
        }

        return false;
    }

    /** Layout items also cover what child uis don't: an op's real height with its input rows, and the symbol's inputs and outputs. */
    private static bool OverlapsAnyLayoutItem(Dictionary<Guid, MagGraphItem>? layoutItems, Vector2 pos, Vector2 size)
    {
        if (layoutItems == null)
            return false;

        foreach (var item in layoutItems.Values)
        {
            if (Overlaps(pos, size, item.PosOnCanvas, item.Size, FreeSpotMargin))
                return true;
        }

        return false;
    }

    private static bool Overlaps(Vector2 pos, Vector2 size, Vector2 otherPos, Vector2 otherSize, float margin)
    {
        var overlapsX = pos.X < otherPos.X + otherSize.X + margin && pos.X + size.X + margin > otherPos.X;
        var overlapsY = pos.Y < otherPos.Y + otherSize.Y + margin && pos.Y + size.Y + margin > otherPos.Y;
        return overlapsX && overlapsY;
    }

    private const float FreeSpotMargin = 20f;
    private const float ViewEdgeInsetOnScreen = 40f;
}
