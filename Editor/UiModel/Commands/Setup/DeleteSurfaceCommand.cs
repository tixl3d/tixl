#nullable enable
using System.Collections.Generic;
using T3.Core.Output;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Setup;

/// <summary>
/// Deletes a surface together with its whole sub-region sub-tree. Setup entities are plain data (not Symbol
/// instances), so the removed surfaces are captured by reference — detached on delete — along with their
/// original list index, and undo re-inserts them in place with ids, quads and nesting intact.
/// </summary>
internal sealed class DeleteSurfaceCommand : ICommand
{
    public string Name => "Delete surface";
    public bool IsUndoable => true;

    public DeleteSurfaceCommand(T3.Core.Output.Setup setup, Guid surfaceId)
    {
        _setupId = setup.Id;
        CollectSubtree(setup, surfaceId, _removed);
    }

    /// <summary>False when the surface was already gone (e.g. removed as part of a parent's sub-tree).</summary>
    public bool HasSurfaces => _removed.Count > 0;

    public void Do()
    {
        if (!SetupCommands.TryGetSetup(_setupId, Name, out var setup))
            return;

        foreach (var (_, surface) in _removed)
            setup.Surfaces.Remove(surface);

        OutputSetupHandling.SaveActive();
    }

    public void Undo()
    {
        if (!SetupCommands.TryGetSetup(_setupId, Name, out var setup))
            return;

        // Re-insert in ascending original index: each earlier insertion shifts the rest right, so the captured
        // indices line up again — valid because undo is the immediate inverse of Do.
        foreach (var (index, surface) in _removed)
            setup.Surfaces.Insert(Math.Min(index, setup.Surfaces.Count), surface);

        OutputSetupHandling.SaveActive();
    }

    private static void CollectSubtree(T3.Core.Output.Setup setup, Guid rootId, List<(int index, Surface surface)> into)
    {
        var ids = new HashSet<Guid> { rootId };

        // Children aren't guaranteed to follow their parent in list order, so sweep until no new descendant is found.
        bool grew;
        do
        {
            grew = false;
            foreach (var surface in setup.Surfaces)
            {
                if (ids.Contains(surface.ParentId) && ids.Add(surface.Id))
                    grew = true;
            }
        }
        while (grew);

        for (var i = 0; i < setup.Surfaces.Count; i++)
        {
            if (ids.Contains(setup.Surfaces[i].Id))
                into.Add((i, setup.Surfaces[i]));
        }
    }

    private readonly Guid _setupId;
    private readonly List<(int index, Surface surface)> _removed = [];
}
