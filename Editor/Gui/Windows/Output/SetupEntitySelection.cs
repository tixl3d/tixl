#nullable enable
using T3.Core.Output;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Which entity inside the active setup an output window is looking at (e.g. "this window
/// shows surface 'Brick Wall'"). Each OutputWindow owns one instance — windows in setup mode
/// browse independently through their own panel, so showing an entity needs no extra pinning
/// step. Entities are referenced by kind + GUID, resolved against the active setup on use —
/// never by cached object reference.
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
    }

    public EntityKind SelectedKind { get; private set; }
    public Guid SelectedId { get; private set; }

    public void Select(EntityKind kind, Guid id)
    {
        SelectedKind = kind;
        SelectedId = id;
    }

    public void Clear()
    {
        SelectedKind = EntityKind.None;
        SelectedId = Guid.Empty;
    }

    public bool IsSelected(EntityKind kind, Guid id)
    {
        return SelectedKind == kind && SelectedId == id;
    }

    /// <summary>Resolves the selection against a setup; clears it if the entity is gone.</summary>
    public bool TryResolve(Setup setup, out EntityKind kind, out Guid id)
    {
        kind = SelectedKind;
        id = SelectedId;
        if (kind == EntityKind.None)
            return false;

        var localId = id;
        var exists = kind switch
                         {
                             EntityKind.ReferenceImage => setup.ReferenceImages.Exists(e => e.Id == localId),
                             EntityKind.Surface => setup.Surfaces.Exists(e => e.Id == localId),
                             EntityKind.Prop => setup.Props.Exists(e => e.Id == localId),
                             EntityKind.Output => setup.Outputs.Exists(e => e.Id == localId),
                             _ => false,
                         };
        if (!exists)
        {
            Clear();
            kind = EntityKind.None;
        }

        return exists;
    }
}
