#nullable enable
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Grows a tree layout from the current selection one level per invocation: operators feeding the
/// selected items are snapped into place (main input stacked above or, for input drivers, left of the row; other
/// inputs left of their row)
/// and added to the selection, so repeating the action walks further up and left through the graph.
/// Selected items that feed other selected items are arranged as well, so a box selection gets tidied.
/// Already snapped sources are kept where they are. A source snaps horizontally into a row only if the rows it
/// covers below are free or fed by its own further outputs; taller ones and shared sources (fan-out, placed
/// once all their consumers are part of the selection) go loosely left of their consumers with a gap. Only items
/// taking part in the layout count as obstacles (the fixed part of the selection with everything snapped to it,
/// plus placed clusters); unrelated operators in the way are overlapped, not avoided. Items only touch each other
/// at exact snap positions; everything placed loosely keeps at least a row or column of air, so nothing reads as
/// snapped that isn't. Connections are never changed.
/// </summary>
internal static class TreeLayouting
{
    /// <returns>True if an item was moved or the selection grew</returns>
    internal static bool LayoutInputsOfSelection(GraphUiContext context)
    {
        var layout = context.Layout;
        var targets = new List<MagGraphItem>();
        var selected = new HashSet<MagGraphItem>();
        foreach (var selectable in context.Selector.Selection)
        {
            if (!layout.Items.TryGetValue(selectable.Id, out var item))
                continue;

            if (item.Variant is not (MagGraphItem.Variants.Operator or MagGraphItem.Variants.Output))
                continue;

            targets.Add(item);
            selected.Add(item);
        }

        if (targets.Count == 0)
            return false;

        // Upper targets first: their (possibly taller) sources claim the shared left column, lower ones search below
        targets.Sort(CompareTopLeftFirst);

        // Selected items feeding other selected items through a loose connection are arranged like any
        // other source; the rest of the selection stays put, together with everything snapped to it.
        var movableSelected = new HashSet<MagGraphItem>();
        foreach (var target in targets)
        {
            foreach (var inputLine in target.InputLines)
            {
                var connection = inputLine.ConnectionIn;
                if (connection == null || connection.IsSnapped || connection.IsTemporary)
                    continue;

                var source = connection.SourceItem;
                if (!selected.Contains(source) || source == target)
                    continue;

                if (TryGetSnapRow(source, target, out _) || AllConsumersAreIn(source, selected))
                {
                    movableSelected.Add(source);
                }
            }
        }

        var handled = new HashSet<MagGraphItem>(selected);
        handled.ExceptWith(movableSelected);
        var obstacles = MagItemMovement.CollectSnappedItems(handled);

        // Items snapped together form a block: the fixed selection with everything snapped to it, and each
        // loosely placed cluster on its own. A snapping cluster may pack flush only against its target's block.
        var fixedBlock = new HashSet<MagGraphItem>(obstacles);
        var blocks = new Dictionary<MagGraphItem, HashSet<MagGraphItem>>();
        foreach (var item in fixedBlock)
        {
            blocks[item] = fixedBlock;
        }

        // Classify first, place afterwards: sources that are going to move must not block each other's spots
        var placements = new List<Placement>();
        var pendingItems = new HashSet<MagGraphItem>();
        var newlySelected = new List<MagGraphItem>();

        foreach (var target in targets)
        {
            for (var lineIndex = 0; lineIndex < target.InputLines.Length; lineIndex++)
            {
                var connection = target.InputLines[lineIndex].ConnectionIn;
                if (connection == null || connection.IsTemporary)
                    continue;

                var source = connection.SourceItem;
                if (source.Variant is not (MagGraphItem.Variants.Operator or MagGraphItem.Variants.Input)
                    || source.IsCollapsedAway
                    || source == target
                    || handled.Contains(source))
                    continue;

                if (connection.IsSnapped)
                {
                    handled.Add(source);
                    newlySelected.Add(source);
                    continue;
                }

                // A source can snap only if the target is its sole consumer and takes its outputs in matching
                // rows. Shared sources wait until every consumer is laid out, then go unsnapped left of all of them.
                var canSnap = TryGetSnapRow(source, target, out var snapRow);
                if (!canSnap && !AllConsumersAreIn(source, handled, selected))
                    continue;

                // The source moves with everything snapped to it, like a drag would - unless that would drag
                // along something that has to stay
                var cluster = new HashSet<MagGraphItem>();
                MagItemMovement.CollectSnappedItems(source, cluster);
                if (cluster.Overlaps(obstacles))
                    continue;

                // A tall item snapped into a row covers the rows below it - fine as long as nothing else wants to
                // snap there. Stacking above allows any height.
                var canStack = canSnap && snapRow == 0 && CountOutgoingConnections(source) == 1;
                var maySnapIntoRow = canSnap && CoveredRowsAreFree(target, snapRow, source);
                var snaps = maySnapIntoRow || canStack;

                handled.UnionWith(cluster);
                obstacles.UnionWith(cluster);
                pendingItems.UnionWith(cluster);
                placements.Add(new Placement(target, snapRow, source, cluster, snaps, canStack, maySnapIntoRow));
            }
        }

        // A target that is itself going to move must be placed before its own sources, and snapped
        // placements before loose ones so the latter settle below the tidy column instead of inside it.
        var sourceToTarget = new Dictionary<MagGraphItem, MagGraphItem>();
        foreach (var placement in placements)
        {
            sourceToTarget[placement.Source] = placement.Target;
        }

        var orderedPlacements = new List<(int order, int index, Placement placement)>(placements.Count);
        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];
            var order = GetChainDepth(placement.Target, sourceToTarget) * 2 + (placement.Snaps ? 0 : 1);
            orderedPlacements.Add((order, index, placement));
        }

        orderedPlacements.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.index.CompareTo(b.index));

        var movedCount = 0;
        context.StartMacroCommand("Layout inputs");

        foreach (var (_, _, placement) in orderedPlacements)
        {
            // A selected target can be skipped as a source itself (e.g. its cluster overlaps the fixed part);
            // it then stays put and forms a block with whatever is snapped to it
            if (!blocks.TryGetValue(placement.Target, out var targetBlock))
            {
                targetBlock = MagItemMovement.CollectSnappedItems(placement.Target);
                foreach (var item in targetBlock)
                {
                    blocks[item] = targetBlock;
                }
            }

            var isSnapped = false;
            var found = placement.Snaps
                            ? TryFindSnappedPosition(placement, obstacles, targetBlock, pendingItems, out var newPos, out isSnapped)
                            : TryFindPositionLeftOfConsumers(placement, obstacles, targetBlock, pendingItems, out newPos);

            // From here on the cluster blocks at its final position, whether it moved or not
            pendingItems.ExceptWith(placement.Cluster);
            var block = isSnapped ? targetBlock : new HashSet<MagGraphItem>();
            block.UnionWith(placement.Cluster);
            foreach (var item in placement.Cluster)
            {
                blocks[item] = block;
            }

            if (!found)
                continue;

            MoveCluster(context, placement.Cluster, newPos - placement.Source.PosOnCanvas);
            movedCount++;
            newlySelected.Add(placement.Source);
        }

        if (movedCount > 0)
        {
            context.CompleteMacroCommand();

            // Section membership follows geometry: an op arranged outside its frame leaves it,
            // like a dragged one - the frame must not be stretched after it
            context.Layout.FlagStructureAsChanged();
        }
        else
        {
            context.CancelMacroCommand();
        }

        foreach (var item in newlySelected)
        {
            item.AddToSelection(context.Selector);
        }

        return movedCount > 0 || newlySelected.Count > 0;
    }

    /// <param name="SnapRow">Target input row the source's first output line aligns with when snapped horizontally</param>
    private readonly record struct Placement(MagGraphItem Target,
                                             int SnapRow,
                                             MagGraphItem Source,
                                             HashSet<MagGraphItem> Cluster,
                                             bool Snaps,
                                             bool CanStack,
                                             bool MaySnapIntoRow);

    private static void MoveCluster(GraphUiContext context, HashSet<MagGraphItem> cluster, Vector2 offset)
    {
        var selectables = new List<ISelectableCanvasObject>(cluster.Count);
        foreach (var item in cluster)
        {
            selectables.Add(item);
        }

        var moveCommand = new ModifyCanvasElementsCommand(context.CompositionInstance.Symbol.Id, selectables, context.Selector);
        context.MacroCommand!.AddExecutedCommandForUndo(moveCommand);
        foreach (var item in selectables)
        {
            item.PosOnCanvas += offset;
        }

        moveCommand.StoreCurrentValues();
    }

    private static int CompareTopLeftFirst(MagGraphItem a, MagGraphItem b)
    {
        var byY = a.PosOnCanvas.Y.CompareTo(b.PosOnCanvas.Y);
        return byY != 0 ? byY : a.PosOnCanvas.X.CompareTo(b.PosOnCanvas.X);
    }

    private static int CountOutgoingConnections(MagGraphItem item)
    {
        var count = 0;
        foreach (var outputLine in item.OutputLines)
        {
            count += outputLine.ConnectionsOut.Count;
        }

        return count;
    }

    /// <summary>
    /// A source snaps horizontally when the target is its only consumer and every connection lands in the row
    /// its output line sits in, so a multi-output helper feeding consecutive rows of a multi-input snaps as a whole.
    /// </summary>
    private static bool TryGetSnapRow(MagGraphItem source, MagGraphItem target, out int snapRow)
    {
        snapRow = -1;
        foreach (var outputLine in source.OutputLines)
        {
            foreach (var connection in outputLine.ConnectionsOut)
            {
                if (connection.TargetItem != target)
                {
                    snapRow = -1;
                    return false;
                }

                var row = connection.InputLineIndex - connection.VisibleOutputIndex;
                if (snapRow == -1)
                {
                    snapRow = row;
                }
                else if (snapRow != row)
                {
                    snapRow = -1;
                    return false;
                }
            }
        }

        return snapRow >= 0;
    }

    private static bool AllConsumersAreIn(MagGraphItem source, HashSet<MagGraphItem> set, HashSet<MagGraphItem>? orSet = null)
    {
        foreach (var outputLine in source.OutputLines)
        {
            foreach (var connection in outputLine.ConnectionsOut)
            {
                var consumer = connection.TargetItem;
                if (!set.Contains(consumer) && (orSet == null || !orSet.Contains(consumer)))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Number of pending placements the item's position depends on: zero for a fixed target, one for a
    /// source that will move, two for a source of that source, and so on.
    /// </summary>
    private static int GetChainDepth(MagGraphItem item, Dictionary<MagGraphItem, MagGraphItem> sourceToTarget)
    {
        var depth = 0;
        while (sourceToTarget.TryGetValue(item, out var consumer) && depth < MaxSearchSteps)
        {
            depth++;
            item = consumer;
        }

        return depth;
    }

    /// <summary>
    /// True if the target rows a source of this height would cover below its snap row are unconnected, fed by
    /// the source itself, or don't exist.
    /// </summary>
    private static bool CoveredRowsAreFree(MagGraphItem target, int snapRow, MagGraphItem source)
    {
        var sourceRows = (int)MathF.Round(source.Size.Y / MagGraphItem.LineHeight);
        for (var row = snapRow + 1; row < snapRow + sourceRows && row < target.InputLines.Length; row++)
        {
            var connection = target.InputLines[row].ConnectionIn;
            if (connection != null && connection.SourceItem != source)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Main input: stacked above the target, or snapped left of its row. Input drivers prefer the row, where they
    /// pack tighter; operators prefer the stack. If neither exact spot is free, or the source would cover connected
    /// rows below, it falls back to the loose placement - the connection then becomes a curve.
    /// </summary>
    private static bool TryFindSnappedPosition(in Placement placement, HashSet<MagGraphItem> obstacles, HashSet<MagGraphItem> targetBlock,
                                               HashSet<MagGraphItem> pendingItems, out Vector2 position, out bool isSnapped)
    {
        isSnapped = true;
        var rowFirst = placement.Source.Variant == MagGraphItem.Variants.Input;
        if (rowFirst && TryGetRowPosition(placement, obstacles, targetBlock, pendingItems, out position))
            return true;

        if (TryGetStackPosition(placement, obstacles, targetBlock, pendingItems, out position))
            return true;

        if (!rowFirst && TryGetRowPosition(placement, obstacles, targetBlock, pendingItems, out position))
            return true;

        isSnapped = false;
        return TryFindPositionLeftOfConsumers(placement, obstacles, targetBlock, pendingItems, out position);
    }

    private static bool TryGetStackPosition(in Placement placement, HashSet<MagGraphItem> obstacles, HashSet<MagGraphItem> targetBlock,
                                            HashSet<MagGraphItem> pendingItems, out Vector2 position)
    {
        position = placement.Target.PosOnCanvas - new Vector2(0, placement.Source.Size.Y);
        return placement.CanStack && IsFree(placement, obstacles, targetBlock, pendingItems, position, isSnapPosition: true);
    }

    private static bool TryGetRowPosition(in Placement placement, HashSet<MagGraphItem> obstacles, HashSet<MagGraphItem> targetBlock,
                                          HashSet<MagGraphItem> pendingItems, out Vector2 position)
    {
        position = placement.Target.PosOnCanvas + new Vector2(-MagGraphItem.Width, placement.SnapRow * MagGraphItem.LineHeight);
        return placement.MaySnapIntoRow && IsFree(placement, obstacles, targetBlock, pendingItems, position, isSnapPosition: true);
    }

    /// <summary>
    /// Loose placement: one column left of the leftmost consumer, at the row of the topmost consumer connection,
    /// walking down row by row until there is air on all sides.
    /// </summary>
    private static bool TryFindPositionLeftOfConsumers(in Placement placement, HashSet<MagGraphItem> obstacles, HashSet<MagGraphItem> targetBlock,
                                                       HashSet<MagGraphItem> pendingItems, out Vector2 position)
    {
        var minX = float.PositiveInfinity;
        var minRowY = float.PositiveInfinity;
        foreach (var outputLine in placement.Source.OutputLines)
        {
            foreach (var connection in outputLine.ConnectionsOut)
            {
                var consumerPos = connection.TargetItem.PosOnCanvas;
                minX = MathF.Min(minX, consumerPos.X);
                minRowY = MathF.Min(minRowY, consumerPos.Y + connection.InputLineIndex * MagGraphItem.LineHeight);
            }
        }

        var columnPos = new Vector2(minX - MagGraphItem.Width - UnsnappedGap, minRowY);
        for (var step = 0; step < MaxSearchSteps; step++)
        {
            position = columnPos + new Vector2(0, step * MagGraphItem.LineHeight);
            if (IsFree(placement, obstacles, targetBlock, pendingItems, position, isSnapPosition: false))
                return true;
        }

        position = default;
        return false;
    }

    /// <summary>
    /// Obstacles are the items taking part in the layout (fixed selection with everything snapped to it, placed
    /// clusters) minus those still pending a move. Unrelated items don't block: overlapping them is visible and
    /// easy to fix by hand, while avoiding them would silently break the snap the user asked for.
    /// </summary>
    private static bool IsFree(in Placement placement, HashSet<MagGraphItem> obstacles, HashSet<MagGraphItem> targetBlock,
                               HashSet<MagGraphItem> pendingItems, Vector2 sourcePos, bool isSnapPosition)
    {
        var offset = sourcePos - placement.Source.PosOnCanvas;
        foreach (var movedItem in placement.Cluster)
        {
            var area = ImRect.RectWithSize(movedItem.PosOnCanvas + offset, movedItem.Size);
            foreach (var other in obstacles)
            {
                if (placement.Cluster.Contains(other)
                    || pendingItems.Contains(other))
                    continue;

                // At its snap position a cluster shares an edge with the target and packs flush against the
                // sources (and their inputs) already snapped around it. Everywhere else it keeps air on all sides.
                var mayTouch = isSnapPosition && targetBlock.Contains(other);
                if (mayTouch ? area.Overlaps(other.Area) : IsTooClose(area, other.Area))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Overlapping, or sharing an edge of some length (corner contact is fine): an item placed flush against
    /// an unrelated one would read as snapped to it.
    /// </summary>
    private static bool IsTooClose(ImRect area, ImRect other)
    {
        var inflatedX = new ImRect(area.Min - new Vector2(TouchMargin, 0), area.Max + new Vector2(TouchMargin, 0));
        var inflatedY = new ImRect(area.Min - new Vector2(0, TouchMargin), area.Max + new Vector2(0, TouchMargin));
        return inflatedX.Overlaps(other) || inflatedY.Overlaps(other);
    }

    private const int MaxSearchSteps = 200;
    private const float UnsnappedGap = MagGraphItem.WidthHalf;
    private const float TouchMargin = MagGraphItem.LineHeight * 0.5f;
}
