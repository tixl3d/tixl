#nullable enable
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.UiModel;

/// <summary>
/// Shared helpers for resolving the section structure of a symbol graph: nesting,
/// membership, and collapse visibility. Graph views, the dope sheet, and the snapshot
/// control view should all derive section structure from here instead of re-deriving
/// it from geometry.
/// </summary>
internal static class SectionTree
{
    internal sealed class Node
    {
        internal required Section Section;
        internal readonly List<Node> Children = [];
        internal readonly List<SymbolUi.Child> Members = [];
    }

    /// <summary>
    /// Builds the section tree with member ops in display order (canvas position,
    /// top-to-bottom then left-to-right). Ops outside any section end up in
    /// <paramref name="unsectionedOps"/>.
    /// </summary>
    internal static List<Node> Build(SymbolUi symbolUi, List<SymbolUi.Child>? unsectionedOps = null)
    {
        var nodesById = new Dictionary<Guid, Node>(symbolUi.Sections.Count);
        foreach (var section in symbolUi.Sections.Values)
        {
            nodesById[section.Id] = new Node { Section = section };
        }

        var roots = new List<Node>();
        foreach (var node in nodesById.Values)
        {
            if (nodesById.TryGetValue(node.Section.ParentSectionId, out var parentNode) && parentNode != node)
            {
                parentNode.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            if (nodesById.TryGetValue(childUi.SectionId, out var node))
            {
                node.Members.Add(childUi);
            }
            else
            {
                unsectionedOps?.Add(childUi);
            }
        }

        foreach (var node in nodesById.Values)
        {
            node.Members.Sort(CompareByCanvasPosition);
        }

        return roots;
    }

    /// <summary>
    /// Derives ownership from canvas geometry: ops belong to the innermost section fully
    /// containing them, sections nest into the innermost strictly larger one. Called on
    /// load and on every structural layout refresh — ownership is derived state, so undoing
    /// a move restores it implicitly and no command needs to track it.
    ///
    /// Collapsed sections neither adopt nor release: only their header renders, so the rest
    /// of their stored rect is invisible and geometry against it would silently hide loose
    /// ops dropped there. Ownership of hidden ops and hidden nested frames is fully sticky —
    /// they can't be moved by the user, so re-deriving it (e.g. after their collapsed frame
    /// was pushed across another section) would silently hand them over.
    /// </summary>
    internal static void UpdateOwnershipFromGeometry(SymbolUi symbolUi)
    {
        if (symbolUi.Sections.Count == 0 && symbolUi.ChildUis.Count == 0)
            return;

        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            // Ops hidden in a collapsed frame keep their ownership unconditionally: they are
            // invisible and can't be moved by the user, so when the collapsed frame gets
            // pushed across another section, geometry must not hand them over.
            if (symbolUi.Sections.TryGetValue(childUi.SectionId, out var owner)
                && IsSelfOrAncestorCollapsed(symbolUi, owner))
                continue;

            var rect = new ImRect(childUi.PosOnCanvas, childUi.PosOnCanvas + childUi.Size);
            childUi.SectionId = FindOwnerSectionId(symbolUi, rect, currentOwnerId: childUi.SectionId,
                                                   excludeId: Guid.Empty, requireLargerThan: 0f);
        }

        foreach (var section in symbolUi.Sections.Values)
        {
            // Same stickiness for nesting: frames hidden under a collapsed ancestor keep their parent
            if (symbolUi.Sections.TryGetValue(section.ParentSectionId, out var parent)
                && IsSelfOrAncestorCollapsed(symbolUi, parent))
                continue;

            // A collapsed frame nests by its visible header footprint, not its stored expanded
            // rect - dropping a collapsed frame into a section should parent it; expanding it
            // later grows the parent via the bounds-expansion cascade.
            var visibleSize = section.Collapsed
                                  ? new Vector2(section.Size.X, MagGraphItem.LineHeight)
                                  : section.Size;
            var rect = ImRect.RectWithSize(section.PosOnCanvas, visibleSize);

            section.ParentSectionId = FindParentSectionId(symbolUi, rect, currentOwnerId: section.ParentSectionId,
                                                          excludeId: section.Id);
        }
    }

    /// <summary>
    /// Recomputes <see cref="SymbolUi.Child.HiddenInCollapsedSectionId"/> for all children.
    /// Call after structural changes (collapse toggles, ownership changes, section deletes).
    /// </summary>
    internal static void UpdateCollapsedVisibility(SymbolUi symbolUi)
    {
        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            childUi.HiddenInCollapsedSectionId = FindOutermostCollapsedSection(symbolUi, childUi.SectionId);
        }

        // Nested frames hide with their collapsed ancestors; a collapsed section
        // itself stays visible as its header
        foreach (var section in symbolUi.Sections.Values)
        {
            section.HiddenInCollapsedSectionId = FindOutermostCollapsedSection(symbolUi, section.ParentSectionId);
        }
    }

    /// <summary>
    /// Collects everything that moves with a section: member ops, nested sections with
    /// their members, and inputs/outputs contained geometrically (those have no ownership).
    /// </summary>
    internal static void CollectSectionContents(SymbolUi symbolUi, Section section, List<ISelectableCanvasObject> results)
    {
        _visitedSectionIds.Clear();
        _visitedSectionIds.Add(section.Id);
        CollectMembersRecursively(symbolUi, section, results);

        var sectionRect = new ImRect(section.PosOnCanvas, section.PosOnCanvas + section.Size);
        foreach (var inputUi in symbolUi.InputUis.Values)
        {
            if (sectionRect.Contains(new ImRect(inputUi.PosOnCanvas, inputUi.PosOnCanvas + inputUi.Size)))
                results.Add(inputUi);
        }

        foreach (var outputUi in symbolUi.OutputUis.Values)
        {
            if (sectionRect.Contains(new ImRect(outputUi.PosOnCanvas, outputUi.PosOnCanvas + outputUi.Size)))
                results.Add(outputUi);
        }
    }

    /// <summary>
    /// Collects the member ops and nested frames hidden inside a collapsed section,
    /// recursively. Used when deleting a collapsed frame: once its header is gone the
    /// hidden contents have no visible representation left, so they are deleted with it.
    /// </summary>
    internal static void CollectHiddenContents(SymbolUi symbolUi, Section section, List<SymbolUi.Child> hiddenOps, List<Section> hiddenSections)
    {
        _visitedSectionIds.Clear();
        _visitedSectionIds.Add(section.Id);
        CollectHiddenContentsRecursively(symbolUi, section, hiddenOps, hiddenSections);
    }

    internal readonly record struct OverlapPush(ISelectableCanvasObject Block, Vector2 Delta);

    /// <summary>
    /// Computes how far sibling frames in the source's parent scope (with their contents)
    /// must move along <paramref name="pushDirection"/> (a unit
    /// axis) so nothing overlaps <paramref name="claimedBounds"/>. Pushes cascade to blocks
    /// shoved into others; the monotone relaxation is iteration-capped so cyclic
    /// arrangements can't loop forever. Growing the parent to fit is the caller's job —
    /// see <see cref="ResolveBoundsExpansion"/>.
    /// </summary>
    internal static void CollectOverlapPushes(SymbolUi symbolUi, Section source, ImRect claimedBounds, Vector2 pushDirection, List<OverlapPush> results)
    {
        _pushBlocks.Clear();
        CollectScopeBlocksIntoBuffer(symbolUi, source);
        RunPushRelaxation(claimedBounds, allowDown: pushDirection.Y > 0, allowRight: pushDirection.X > 0);

        foreach (var block in _pushBlocks)
        {
            if (block.PushDistance > 0)
            {
                results.Add(new OverlapPush(block.Root, block.PushAxis * block.PushDistance));
            }
        }
    }

    /// <summary>
    /// Snapshot of the sibling blocks the solver would consider for the given source —
    /// used to freeze original bounds for stateless per-frame push previews.
    /// </summary>
    internal static void CollectScopeBlocks(SymbolUi symbolUi, Section source, List<(ISelectableCanvasObject Root, ImRect Bounds)> blocks)
    {
        _pushBlocks.Clear();
        CollectScopeBlocksIntoBuffer(symbolUi, source);
        foreach (var block in _pushBlocks)
        {
            blocks.Add((block.Root, block.Bounds));
        }
    }

    /// <summary>
    /// Push deltas against frozen block bounds, one delta per block index (zero when
    /// unaffected). Each block resolves along a single axis - the one needing the smaller
    /// displacement when both are allowed. Recomputed from the same original bounds every
    /// frame, this yields a stateless preview: blocks return when the border retreats.
    /// </summary>
    internal static void ComputePushesForBlocks(IReadOnlyList<(ISelectableCanvasObject Root, ImRect Bounds)> blocks,
                                                ImRect claimedBounds, bool allowDown, bool allowRight, List<Vector2> deltasPerBlock)
    {
        _pushBlocks.Clear();
        foreach (var (root, bounds) in blocks)
        {
            _pushBlocks.Add(new PushBlock(root, bounds));
        }

        RunPushRelaxation(claimedBounds, allowDown, allowRight);

        deltasPerBlock.Clear();
        foreach (var block in _pushBlocks)
        {
            deltasPerBlock.Add(block.PushAxis * block.PushDistance);
        }
    }

    private static void CollectScopeBlocksIntoBuffer(SymbolUi symbolUi, Section source)
    {
        // Only frames are pushed - loose ops are never moved by the solver. Shoving ops
        // around would wreck hand-made layouts (and the relaxation would teleport them
        // across large frames). An op overlapped by an expanding frame is simply adopted:
        // visible inside the frame and reversible by dragging it out.
        var scopeId = source.ParentSectionId;

        foreach (var section in symbolUi.Sections.Values)
        {
            // Only sibling frames in the same scope move as blocks; nested ones travel with them
            if (section.Id == source.Id || section.ParentSectionId != scopeId)
                continue;

            // Collapsed neighbors only occupy their visible header strip
            var size = section.Collapsed
                           ? new Vector2(section.Size.X, MagGraphItem.LineHeight)
                           : section.Size;
            _pushBlocks.Add(new PushBlock(section, ImRect.RectWithSize(section.PosOnCanvas, size)));
        }
    }

    private static void RunPushRelaxation(ImRect claimedBounds, bool allowDown, bool allowRight)
    {
        _pushQueue.Clear();
        _pushQueue.Enqueue((claimedBounds, null));

        var remainingIterations = (_pushBlocks.Count + 1) * 4;
        while (_pushQueue.Count > 0 && remainingIterations-- > 0)
        {
            var (claimed, claimSource) = _pushQueue.Dequeue();
            for (var index = 0; index < _pushBlocks.Count; index++)
            {
                var block = _pushBlocks[index];

                // A block's own claim must not push it again - a rect always overlaps itself
                if (block == claimSource)
                    continue;

                var rect = block.Bounds;
                rect.Translate(block.PushAxis * block.PushDistance);
                if (!claimed.Overlaps(rect))
                    continue;

                // Each block resolves along a single axis, chosen on first contact by the
                // smaller displacement. Blocks starting before the claim on an axis can't
                // be pushed along it - they surround or precede the claim there; that's a
                // container relationship handled by nesting.
                if (block.PushDistance <= 0)
                {
                    var canPushDown = allowDown && block.Bounds.Min.Y >= claimed.Min.Y;
                    var canPushRight = allowRight && block.Bounds.Min.X >= claimed.Min.X;
                    if (!canPushDown && !canPushRight)
                        continue;

                    var requiredDown = claimed.Max.Y + PushPadding - block.Bounds.Min.Y;
                    var requiredRight = claimed.Max.X + PushPadding - block.Bounds.Min.X;

                    block.PushAxis = canPushDown && (!canPushRight || requiredDown <= requiredRight)
                                         ? Vector2.UnitY
                                         : Vector2.UnitX;
                }

                var required = block.PushAxis.Y > 0
                                   ? claimed.Max.Y + PushPadding - block.Bounds.Min.Y
                                   : claimed.Max.X + PushPadding - block.Bounds.Min.X;

                if (required <= block.PushDistance)
                    continue;

                block.PushDistance = required;
                var newRect = block.Bounds;
                newRect.Translate(block.PushAxis * required);
                _pushQueue.Enqueue((newRect, block));
            }
        }
    }

    /// <summary>
    /// Pushes neighboring blocks out of the given claimed bounds and folds the applied
    /// move into <paramref name="macro"/> as an executed command. No-op when nothing
    /// overlaps. Pushed sections take their members and nested frames along.
    /// </summary>
    internal static void PushNeighborsOutOfBounds(SymbolUi symbolUi, Section source, ImRect claimedBounds, Vector2 pushDirection,
                                                  Commands.MacroCommand macro, ISelection selector)
    {
        _overlapPushes.Clear();
        CollectOverlapPushes(symbolUi, source, claimedBounds, pushDirection, _overlapPushes);
        if (_overlapPushes.Count == 0)
            return;

        _elementsToPush.Clear();
        _pushDeltas.Clear();
        foreach (var push in _overlapPushes)
        {
            var firstIndex = _elementsToPush.Count;
            _elementsToPush.Add(push.Block);
            if (push.Block is Section pushedSection)
            {
                CollectSectionContents(symbolUi, pushedSection, _elementsToPush);
            }

            for (var i = firstIndex; i < _elementsToPush.Count; i++)
            {
                _pushDeltas.Add(push.Delta);
            }
        }

        var moveCommand = new Commands.Graph.ModifyCanvasElementsCommand(symbolUi, _elementsToPush, selector);
        for (var i = 0; i < _elementsToPush.Count; i++)
        {
            _elementsToPush[i].PosOnCanvas += _pushDeltas[i];
        }

        moveCommand.StoreCurrentValues();
        macro.AddExecutedCommandForUndo(moveCommand);
    }

    /// <summary>
    /// Resolves the fallout of a frame claiming more space (e.g. expanding from collapse):
    /// siblings in the same parent scope get pushed along <paramref name="pushDirection"/>,
    /// the parent grows (bottom/right) to keep its content inside, and that growth cascades
    /// further up the ancestor chain. All moves and resizes fold into the macro.
    /// </summary>
    internal static void ResolveBoundsExpansion(SymbolUi symbolUi, Section source, ImRect claimedBounds, Vector2 pushDirection,
                                                Commands.MacroCommand macro, ISelection selector)
    {
        var current = source;
        var claimed = claimedBounds;
        var remainingLevels = symbolUi.Sections.Count; // guards against parent-reference cycles
        while (remainingLevels-- >= 0)
        {
            PushNeighborsOutOfBounds(symbolUi, current, claimed, pushDirection, macro, selector);

            if (!symbolUi.Sections.TryGetValue(current.ParentSectionId, out var parent))
                break;

            // Grow the parent so its content (including the pushed siblings) stays inside
            var contentMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            var hasContent = false;
            foreach (var sibling in symbolUi.Sections.Values)
            {
                if (sibling.ParentSectionId != parent.Id)
                    continue;

                var size = sibling.Collapsed
                               ? new Vector2(sibling.Size.X, MagGraphItem.LineHeight)
                               : sibling.Size;
                contentMax = Vector2.Max(contentMax, sibling.PosOnCanvas + size);
                hasContent = true;
            }

            foreach (var childUi in symbolUi.ChildUis.Values)
            {
                if (childUi.SectionId != parent.Id)
                    continue;

                contentMax = Vector2.Max(contentMax, childUi.PosOnCanvas + childUi.Size);
                hasContent = true;
            }

            if (!hasContent)
                break;

            var parentMax = parent.PosOnCanvas + parent.Size;
            var requiredMax = Vector2.Max(parentMax, contentMax + ContentPadding);
            if (requiredMax.X <= parentMax.X + 0.5f && requiredMax.Y <= parentMax.Y + 0.5f)
                break; // parent still fits - nothing propagates further up

            var resizeCommand = new Commands.Graph.ModifyCanvasElementsCommand(symbolUi, [parent], selector);
            parent.Size = requiredMax - parent.PosOnCanvas;
            resizeCommand.StoreCurrentValues();
            macro.AddExecutedCommandForUndo(resizeCommand);

            claimed = ImRect.RectWithSize(parent.PosOnCanvas, parent.Size);
            current = parent;
        }
    }

    /// <summary>
    /// After placing new ops (paste, duplicate) around a target position inside a section,
    /// grows that section so items overflowing its bottom/right border stay inside and
    /// pushes neighbors out of the claimed area — folded into the given macro. No-op when
    /// the target position isn't inside a top-level expanded section or nothing overflows.
    /// </summary>
    internal static void GrowSectionToFitPlacedItems(SymbolUi symbolUi, Vector2 targetPosOnCanvas, IReadOnlyList<Guid> newChildIds,
                                                     Commands.MacroCommand macro, ISelection selector)
    {
        // Innermost expanded frame containing the placement anchor
        Section? target = null;
        var targetArea = float.PositiveInfinity;
        foreach (var section in symbolUi.Sections.Values)
        {
            if (section.Collapsed || section.IsHiddenInCollapsedSection)
                continue;

            var rect = ImRect.RectWithSize(section.PosOnCanvas, section.Size);
            if (!rect.Contains(targetPosOnCanvas))
                continue;

            var area = section.Size.X * section.Size.Y;
            if (area >= targetArea)
                continue;

            targetArea = area;
            target = section;
        }

        if (target == null)
            return;

        // Only measure pasted ops that actually landed inside the target frame. When a whole
        // section is pasted, the anchor can fall inside a pasted sub-section (the target) whose
        // members are scattered across the entire pasted region - measuring all of them would
        // balloon the target and cascade a huge push through every neighbor.
        var targetRect = ImRect.RectWithSize(target.PosOnCanvas, target.Size);
        var itemsMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        var anyItems = false;
        foreach (var id in newChildIds)
        {
            if (!symbolUi.ChildUis.TryGetValue(id, out var childUi))
                continue;

            // Top-left inside means the op started in this frame and may overflow its bottom/right border
            if (!targetRect.Contains(childUi.PosOnCanvas))
                continue;

            itemsMax = Vector2.Max(itemsMax, childUi.PosOnCanvas + childUi.Size);
            anyItems = true;
        }

        if (!anyItems)
            return;

        var boundsMax = target.PosOnCanvas + target.Size;
        var requiredMax = Vector2.Max(boundsMax, itemsMax + ContentPadding);
        var grewX = requiredMax.X > boundsMax.X + 0.5f;
        var grewY = requiredMax.Y > boundsMax.Y + 0.5f;
        if (!grewX && !grewY)
            return;

        var resizeCommand = new Commands.Graph.ModifyCanvasElementsCommand(symbolUi, [target], selector);
        target.Size = requiredMax - target.PosOnCanvas;
        resizeCommand.StoreCurrentValues();
        macro.AddExecutedCommandForUndo(resizeCommand);

        var newBounds = ImRect.RectWithSize(target.PosOnCanvas, target.Size);
        if (grewY)
            ResolveBoundsExpansion(symbolUi, target, newBounds, Vector2.UnitY, macro, selector);
        if (grewX)
            ResolveBoundsExpansion(symbolUi, target, newBounds, Vector2.UnitX, macro, selector);
    }

    /// <summary>Margin kept between section content and the frame border when auto-growing.</summary>
    internal static readonly Vector2 ContentPadding = new(15, 15);

    private sealed class PushBlock(ISelectableCanvasObject root, ImRect bounds)
    {
        internal readonly ISelectableCanvasObject Root = root;
        internal readonly ImRect Bounds = bounds;
        internal Vector2 PushAxis = Vector2.UnitY;
        internal float PushDistance;
    }

    /// <summary>
    /// Nesting is forgiving: a frame stays parented while the majority of its visible
    /// footprint lies inside the candidate. Full containment would silently un-nest a
    /// child poking slightly over the border, turning it into an overlapping "sibling"
    /// that the overlap solver may then push out entirely.
    /// </summary>
    private static Guid FindParentSectionId(SymbolUi symbolUi, ImRect rect, Guid currentOwnerId, Guid excludeId)
    {
        var ownArea = rect.GetWidth() * rect.GetHeight();

        Section? best = null;
        var bestArea = float.PositiveInfinity;
        foreach (var candidate in symbolUi.Sections.Values)
        {
            if (candidate.Id == excludeId)
                continue;

            // Collapsed frames (and frames hidden inside one) only keep their current
            // children, they never adopt - their bounds are invisible
            if (candidate.Id != currentOwnerId && IsSelfOrAncestorCollapsed(symbolUi, candidate))
                continue;

            // Strictly-larger requirement prevents identically-sized sections from parenting each other
            var candidateArea = candidate.Size.X * candidate.Size.Y;
            if (candidateArea <= ownArea || candidateArea >= bestArea)
                continue;

            var candidateRect = ImRect.RectWithSize(candidate.PosOnCanvas, candidate.Size);
            var overlapWidth = MathF.Min(rect.Max.X, candidateRect.Max.X) - MathF.Max(rect.Min.X, candidateRect.Min.X);
            var overlapHeight = MathF.Min(rect.Max.Y, candidateRect.Max.Y) - MathF.Max(rect.Min.Y, candidateRect.Min.Y);
            if (overlapWidth <= 0 || overlapHeight <= 0)
                continue;

            if (overlapWidth * overlapHeight < ownArea * 0.5f)
                continue;

            bestArea = candidateArea;
            best = candidate;
        }

        return best?.Id ?? Guid.Empty;
    }

    private static Guid FindOwnerSectionId(SymbolUi symbolUi, ImRect rect, Guid currentOwnerId, Guid excludeId, float requireLargerThan)
    {
        Section? best = null;
        var bestArea = float.PositiveInfinity;
        foreach (var section in symbolUi.Sections.Values)
        {
            if (section.Id == excludeId)
                continue;

            // Collapsed sections (and frames hidden inside one) only keep their
            // current members, they never adopt — their bounds are invisible
            if (section.Id != currentOwnerId && IsSelfOrAncestorCollapsed(symbolUi, section))
                continue;

            var area = new ImRect(section.PosOnCanvas, section.PosOnCanvas + section.Size);
            if (!area.Contains(rect))
                continue;

            var size = section.Size.X * section.Size.Y;
            if (size <= requireLargerThan || size >= bestArea)
                continue;

            bestArea = size;
            best = section;
        }

        return best?.Id ?? Guid.Empty;
    }

    private static void CollectHiddenContentsRecursively(SymbolUi symbolUi, Section section, List<SymbolUi.Child> hiddenOps, List<Section> hiddenSections)
    {
        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            if (childUi.SectionId == section.Id)
                hiddenOps.Add(childUi);
        }

        foreach (var nested in symbolUi.Sections.Values)
        {
            if (nested.ParentSectionId != section.Id)
                continue;

            // Visited set guards against parent-reference cycles in hand-edited files
            if (!_visitedSectionIds.Add(nested.Id))
                continue;

            hiddenSections.Add(nested);
            CollectHiddenContentsRecursively(symbolUi, nested, hiddenOps, hiddenSections);
        }
    }

    private static void CollectMembersRecursively(SymbolUi symbolUi, Section section, List<ISelectableCanvasObject> results)
    {
        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            if (childUi.SectionId == section.Id)
                results.Add(childUi);
        }

        foreach (var nested in symbolUi.Sections.Values)
        {
            if (nested.ParentSectionId != section.Id)
                continue;

            // Visited set guards against parent-reference cycles in hand-edited files
            if (!_visitedSectionIds.Add(nested.Id))
                continue;

            results.Add(nested);
            CollectMembersRecursively(symbolUi, nested, results);
        }
    }

    private static bool IsSelfOrAncestorCollapsed(SymbolUi symbolUi, Section section)
    {
        if (section.Collapsed)
            return true;

        return FindOutermostCollapsedSection(symbolUi, section.ParentSectionId) != Guid.Empty;
    }

    private static Guid FindOutermostCollapsedSection(SymbolUi symbolUi, Guid sectionId)
    {
        var result = Guid.Empty;
        var remainingSteps = symbolUi.Sections.Count; // guards against parent-reference cycles
        while (sectionId != Guid.Empty && remainingSteps-- >= 0)
        {
            if (!symbolUi.Sections.TryGetValue(sectionId, out var section))
                break;

            if (section.Collapsed)
                result = section.Id;

            sectionId = section.ParentSectionId;
        }

        return result;
    }

    private static int CompareByCanvasPosition(SymbolUi.Child a, SymbolUi.Child b)
    {
        var byY = a.PosOnCanvas.Y.CompareTo(b.PosOnCanvas.Y);
        return byY != 0 ? byY : a.PosOnCanvas.X.CompareTo(b.PosOnCanvas.X);
    }

    private const float PushPadding = 10;

    private static readonly HashSet<Guid> _visitedSectionIds = [];
    private static readonly List<PushBlock> _pushBlocks = [];
    private static readonly Queue<(ImRect Rect, PushBlock? Source)> _pushQueue = new();
    private static readonly List<OverlapPush> _overlapPushes = [];
    private static readonly List<ISelectableCanvasObject> _elementsToPush = [];
    private static readonly List<Vector2> _pushDeltas = [];
}
