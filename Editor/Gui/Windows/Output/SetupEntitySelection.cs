#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Output;

namespace T3.Editor.Gui.Windows.Output;

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
/// Which setup entities an output window has selected — its "entity plane" (whole entities, not
/// sub-elements). Ordered: element 0 is the primary (drives the shown entity view). Single-click replaces,
/// ctrl/shift extend. Entities are referenced by kind + GUID, resolved against the active setup on use —
/// never by cached object reference. One instance per OutputWindow.
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

    /// <summary>Replace the selection with a single entity.</summary>
    public void Select(EntityKind kind, Guid id)
    {
        _targets.Clear();
        _targets.Add(new SelectionTarget(kind, id));
    }

    /// <summary>Add an entity to the selection (no-op if already present).</summary>
    public void Add(EntityKind kind, Guid id)
    {
        var target = new SelectionTarget(kind, id);
        if (!_targets.Contains(target))
            _targets.Add(target);
    }

    /// <summary>Toggle an entity's membership.</summary>
    public void Toggle(EntityKind kind, Guid id)
    {
        var target = new SelectionTarget(kind, id);
        if (!_targets.Remove(target))
            _targets.Add(target);
    }

    public void Clear() => _targets.Clear();

    public bool IsSelected(EntityKind kind, Guid id) => _targets.Contains(new SelectionTarget(kind, id));

    /// <summary>Resolves the primary selection against a setup, dropping any target whose entity is gone.</summary>
    public bool TryResolve(Setup setup, out EntityKind kind, out Guid id)
    {
        _targets.RemoveAll(t => !ExistsInSetup(setup, t));

        if (_targets.Count == 0)
        {
            kind = EntityKind.None;
            id = Guid.Empty;
            return false;
        }

        kind = _targets[0].Kind;
        id = _targets[0].EntityId;
        return true;
    }

    // Slice/ContentSource live on live graph ops, not the setup, so they can't be validated here — kept.
    private static bool ExistsInSetup(Setup setup, SelectionTarget target)
    {
        return target.Kind switch
                   {
                       EntityKind.ReferenceImage => setup.ReferenceImages.Exists(e => e.Id == target.EntityId),
                       EntityKind.Surface => setup.Surfaces.Exists(e => e.Id == target.EntityId),
                       EntityKind.Prop => setup.Props.Exists(e => e.Id == target.EntityId),
                       EntityKind.Output => setup.Outputs.Exists(e => e.Id == target.EntityId),
                       EntityKind.Slice or EntityKind.ContentSource => true,
                       _ => false,
                   };
    }

    private readonly List<SelectionTarget> _targets = [];
}
