#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>A sub-element of a selectable entity, addressed by index. The entity plane uses <see cref="None"/>;
/// the canvas plane addresses corners, annotation endpoints, and lattice points.</summary>
internal enum SubPart
{
    None,
    Corner,
    Annotation,
    LatticePoint,
}

/// <summary>
/// One addressable selection target: an entity, optionally a sub-element by index. The single address form
/// shared by both selection planes (setup-entity and canvas sub-element). A value type, so it compares by
/// content and de-duplicates in a set/list.
/// </summary>
internal readonly record struct SelectionTarget(
    SetupEntitySelection.EntityKind Kind,
    Guid EntityId,
    SubPart Part = SubPart.None,
    int Index = -1);

/// <summary>
/// Which setup entities are selected — the "entity plane" (whole entities, not sub-elements).
/// Ordered: element 0 is the primary (drives the shown entity view). Single-click replaces,
/// ctrl/shift extend. Entities are referenced by kind + GUID, resolved against the active setup on
/// use — never by cached object reference. One instance shared by all output windows
/// (<see cref="T3.Editor.UiModel.ProjectHandling.OutputSetupHandling.EntitySelection"/>); a window
/// that shouldn't follow it keeps a per-window pin instead (see <see cref="OutputSetupModeView"/>).
/// </summary>
internal sealed class SetupEntitySelection
{
    public enum EntityKind
    {
        None,
        ReferenceImage,
        Surface,
        Prop,
        Output,
        Slice,
        ContentSource,
    }

    /// <summary>Replace the selection with a single entity. A pick: takes over the Parameter window.</summary>
    public void Select(EntityKind kind, Guid id)
    {
        _targets.Set(new SelectionTarget(kind, id));
        GlobalSelectionHandling.ClaimInspection(GlobalSelectionHandling.InspectionTargets.SetupEntity);
    }

    /// <summary>Add an entity to the selection (no-op if already present).</summary>
    public void Add(EntityKind kind, Guid id)
    {
        _targets.Add(new SelectionTarget(kind, id));
        GlobalSelectionHandling.ClaimInspection(GlobalSelectionHandling.InspectionTargets.SetupEntity);
    }

    /// <summary>Toggle an entity's membership.</summary>
    public void Toggle(EntityKind kind, Guid id)
    {
        _targets.Toggle(new SelectionTarget(kind, id));
        if (_targets.Count > 0)
            GlobalSelectionHandling.ClaimInspection(GlobalSelectionHandling.InspectionTargets.SetupEntity);
        else
            GlobalSelectionHandling.ReleaseInspection(GlobalSelectionHandling.InspectionTargets.SetupEntity);
    }

    /// <summary>
    /// Replaces the selection to mirror a pick made in the graph (a focused SendToOutput shows as its CONTENT
    /// row) without taking over the Parameter window — the graph keeps it and shows the op's parameters.
    /// </summary>
    public void Mirror(EntityKind kind, Guid id) => _targets.Set(new SelectionTarget(kind, id));

    public void Clear()
    {
        _targets.Clear();
        GlobalSelectionHandling.ReleaseInspection(GlobalSelectionHandling.InspectionTargets.SetupEntity);
    }

    public bool IsSelected(EntityKind kind, Guid id) => _targets.Contains(new SelectionTarget(kind, id));

    public int Count => _targets.Count;

    /// <summary>The selection in order, primary first. Copy before acting on it — anything that deletes
    /// entities prunes this list as it goes.</summary>
    public IReadOnlyList<SelectionTarget> Targets => _targets.Items;

    /// <summary>Resolves the primary selection against a setup, dropping any target whose entity is gone.</summary>
    public bool TryResolve(Setup setup, out EntityKind kind, out Guid id)
    {
        for (var i = _targets.Count - 1; i >= 0; i--)
        {
            if (!ExistsInSetup(setup, _targets[i]))
                _targets.RemoveAt(i);
        }

        if (!_targets.TryGetPrimary(out var primary))
        {
            kind = EntityKind.None;
            id = Guid.Empty;
            return false;
        }

        kind = primary.Kind;
        id = primary.EntityId;
        return true;
    }

    private static bool ExistsInSetup(Setup setup, SelectionTarget target)
    {
        return Exists(setup, target.Kind, target.EntityId);
    }

    /// <summary>Whether an entity reference still resolves against the setup — shared with the
    /// per-window pin, which validates the same way the selection prunes.</summary>
    internal static bool Exists(Setup setup, EntityKind kind, Guid id)
    {
        switch (kind)
        {
            case EntityKind.ReferenceImage:
                foreach (var e in setup.ReferenceImages)
                {
                    if (e.Id == id)
                        return true;
                }

                return false;

            case EntityKind.Surface:
                foreach (var e in setup.Surfaces)
                {
                    if (e.Id == id)
                        return true;
                }

                return false;

            case EntityKind.Prop:
                foreach (var e in setup.Props)
                {
                    if (e.Id == id)
                        return true;
                }

                return false;

            case EntityKind.Output:
                foreach (var e in setup.Outputs)
                {
                    if (e.Id == id)
                        return true;
                }

                return false;

            case EntityKind.Slice:
                foreach (var e in setup.Slices)
                {
                    if (e.Id == id)
                        return true;
                }

                return false;

            case EntityKind.ContentSource:
                // Content rows are addressed by the op's SymbolChildId, not ContentSource.Id. A freshly
                // created send can be selected before the sync adopts it into the setup, so a live send op
                // with that child also counts as existing.
                foreach (var source in setup.ContentSources)
                {
                    if (source.SymbolChildId == id)
                        return true;
                }

                foreach (var sink in OutputSinkRegistry.Sinks)
                {
                    if (sink is Instance instance && instance.SymbolChildId == id)
                        return true;
                }

                return false;

            default:
                return false;
        }
    }

    private readonly SelectionSet<SelectionTarget> _targets = new();
}
