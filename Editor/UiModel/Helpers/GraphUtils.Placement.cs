#nullable enable
using System.Numerics;
using T3.Editor.Gui.Interaction;
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

    /// <summary>
    /// Canvas position for a clip op created outside the graph (timeline drop, recording), where there is no
    /// mouse position to place it at. Stacks below the lowest clip of the same type, else beside the op that
    /// collects it (e.g. [VideoClipPlayer]), else inside the graph's visible area.
    /// </summary>
    public static Vector2 FindPositionForNewClipOp(SymbolUi compositionUi, Guid clipSymbolId)
    {
        var size = SymbolUi.Child.DefaultOpSize;
        var hasAnchor = TryGetBelowLowestChildOfType(compositionUi, clipSymbolId, out var preferred)
                        || TryGetBesideCollector(compositionUi, clipSymbolId, size, out preferred);

        if (!hasAnchor)
        {
            preferred = GetPositionBelowExistingChildren(compositionUi, new Vector2(0, 200));

            // Called from other windows, so the graph's rect has to come from its last drawn frame.
            if (ProjectView.Focused is { GraphView: ScalableCanvas canvas } view
                && view.CompositionInstance?.Symbol.Id == compositionUi.Symbol.Id)
            {
                var visible = canvas.GetLastVisibleCanvasArea();
                if (visible.GetWidth() > 0 && visible.GetHeight() > 0 && !visible.Contains(preferred))
                    preferred = visible.GetCenter() - size * 0.5f;
            }
        }

        return FindFreePosition(compositionUi, preferred, size);
    }

    private static bool TryGetBelowLowestChildOfType(SymbolUi compositionUi, Guid symbolId, out Vector2 position)
    {
        const float spacingY = 20f;
        position = default;
        var maxY = float.NegativeInfinity;
        foreach (var (childId, childUi) in compositionUi.ChildUis)
        {
            if (!compositionUi.Symbol.Children.TryGetValue(childId, out var symbolChild) || symbolChild.Symbol.Id != symbolId)
                continue;

            var bottom = childUi.PosOnCanvas.Y + childUi.Size.Y;
            if (bottom <= maxY)
                continue;

            maxY = bottom;
            position = new Vector2(childUi.PosOnCanvas.X, bottom + spacingY);
        }

        return !float.IsNegativeInfinity(maxY);
    }

    /** Left of the collecting player, where its auto-collect link reads as an input. */
    private static bool TryGetBesideCollector(SymbolUi compositionUi, Guid clipSymbolId, Vector2 size, out Vector2 position)
    {
        const float gapX = 60f;
        position = default;

        Guid collectorSymbolId;
        if (clipSymbolId == _videoClipSymbolId)
        {
            collectorSymbolId = _videoClipPlayerSymbolId;
        }
        else if (clipSymbolId == _audioClipSymbolId)
        {
            collectorSymbolId = _audioClipPlayerSymbolId;
        }
        else
        {
            return false;
        }

        foreach (var (childId, childUi) in compositionUi.ChildUis)
        {
            if (!compositionUi.Symbol.Children.TryGetValue(childId, out var symbolChild) || symbolChild.Symbol.Id != collectorSymbolId)
                continue;

            position = new Vector2(childUi.PosOnCanvas.X - size.X - gapX, childUi.PosOnCanvas.Y);
            return true;
        }

        return false;
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

    // Clip ops and their auto-collecting players live in operator packages the editor can't reference.
    private static readonly Guid _videoClipSymbolId = new("04c1a6dc-3042-48a8-81d2-0a5a162016dc");
    private static readonly Guid _videoClipPlayerSymbolId = new("f98d28ed-5f30-4416-93be-f0d7a15a7f77");
    private static readonly Guid _audioClipSymbolId = new("f0008b50-091d-4e9f-91eb-baa212acfa20");
    private static readonly Guid _audioClipPlayerSymbolId = new("3f78a8a8-2c0b-4486-813e-f052e01e4248");
}
