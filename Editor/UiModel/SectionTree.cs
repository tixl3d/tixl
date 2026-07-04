#nullable enable
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
    /// ops dropped there (or free hidden members). Membership in a collapsed section is kept
    /// while the op stays inside the stored rect.
    /// </summary>
    internal static void UpdateOwnershipFromGeometry(SymbolUi symbolUi)
    {
        if (symbolUi.Sections.Count == 0 && symbolUi.ChildUis.Count == 0)
            return;

        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            var rect = new ImRect(childUi.PosOnCanvas, childUi.PosOnCanvas + childUi.Size);
            childUi.SectionId = FindOwnerSectionId(symbolUi, rect, currentOwnerId: childUi.SectionId,
                                                   excludeId: Guid.Empty, requireLargerThan: 0f);
        }

        foreach (var section in symbolUi.Sections.Values)
        {
            var rect = new ImRect(section.PosOnCanvas, section.PosOnCanvas + section.Size);

            // Strictly-larger requirement prevents identically-sized sections from parenting each other
            section.ParentSectionId = FindOwnerSectionId(symbolUi, rect, currentOwnerId: section.ParentSectionId,
                                                         excludeId: section.Id,
                                                         requireLargerThan: section.Size.X * section.Size.Y);
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

    private static readonly HashSet<Guid> _visitedSectionIds = [];
}
