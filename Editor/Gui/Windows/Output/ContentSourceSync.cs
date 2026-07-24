#nullable enable
using T3.Core.Operator;
using T3.Core.Output;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Keeps the setup's <see cref="ContentSource"/> list 1:1 with the ops that supply pixels.
/// <para>Bound to the op's <b>SymbolChild</b> — the durable graph entity — not to a live instance. Instances
/// come and go with hot-reloads and with whichever part of the graph happens to be instantiated, so "no live
/// sink" only means "no pixels this frame". A source is removed only once its child is confirmed *gone* from
/// a symbol we can actually see, which is what makes deleting the op cascade to its slices and to every
/// surface showing them.</para>
/// </summary>
internal static class ContentSourceSync
{
    public static void Update(Setup setup)
    {
        var changed = false;

        // Adopt any send that has no source yet.
        foreach (var sink in OutputSinkRegistry.Sinks)
        {
            if (sink is not Instance instance)
                continue;

            var childId = instance.SymbolChildId;
            if (setup.ContentSources.Exists(s => s.SymbolChildId == childId))
                continue;

            setup.ContentSources.Add(new ContentSource
                                         {
                                             SymbolChildId = childId,
                                             Name = ReadName(instance),
                                             IsRenamed = HasCustomName(instance),
                                         });
            changed = true;
        }

        // Keep names in step with the ops, so the setup still reads sensibly with nothing instantiated.
        foreach (var source in setup.ContentSources)
        {
            if (!TryFindInstance(source.SymbolChildId, out var instance))
                continue;

            var name = ReadName(instance!);
            var renamed = HasCustomName(instance!);
            if ((string.IsNullOrEmpty(name) || name == source.Name) && renamed == source.IsRenamed)
                continue;

            source.Name = name;
            source.IsRenamed = renamed;
            changed = true;
        }

        // The deletion sweep scans the whole symbol library, so it only runs when a send could actually have
        // gone away — i.e. when registry membership changed. Everything above is O(sends × sources) on two
        // small lists, so it stays per-frame and keeps names live.
        if (_sweptRegistryVersion != OutputSinkRegistry.Version || _sweptSetupId != setup.Id)
        {
            _sweptRegistryVersion = OutputSinkRegistry.Version;
            _sweptSetupId = setup.Id;
            changed |= DropDeletedSources(setup);
        }

        if (changed)
            OutputSetupHandling.SaveActive();
    }

    private static int _sweptRegistryVersion = -1;
    private static Guid _sweptSetupId;

    /// <summary>
    /// Removes sources whose op is provably gone, cascading to their slices and to any surface showing one.
    /// "Provably" matters: a missing *instance* is normal, so the child is only treated as deleted once its
    /// owning symbol is loaded and no longer lists it.
    /// </summary>
    private static bool DropDeletedSources(Setup setup)
    {
        var changed = false;
        for (var i = setup.ContentSources.Count - 1; i >= 0; i--)
        {
            var source = setup.ContentSources[i];
            if (!IsChildConfirmedDeleted(source.SymbolChildId))
                continue;

            var sourceId = source.Id;
            for (var s = setup.Slices.Count - 1; s >= 0; s--)
            {
                if (setup.Slices[s].SourceId != sourceId)
                    continue;

                var sliceId = setup.Slices[s].Id;
                foreach (var surface in setup.Surfaces)
                {
                    if (surface.SliceId == sliceId)
                        surface.SliceId = Guid.Empty;
                }

                setup.Slices.RemoveAt(s);
            }

            setup.ContentSources.RemoveAt(i);
            changed = true;
        }

        return changed;
    }

    private static bool IsChildConfirmedDeleted(Guid childId)
    {
        if (TryFindInstance(childId, out _))
            return false;

        // No instance is not evidence on its own — only a loaded symbol that no longer lists the child is.
        foreach (var package in EditorSymbolPackage.AllPackages)
        {
            foreach (var symbol in package.Symbols.Values)
            {
                foreach (var child in symbol.Children.Values)
                {
                    if (child.Id == childId)
                        return false;
                }
            }
        }

        return true;
    }

    private static bool TryFindInstance(Guid childId, out Instance? instance)
    {
        foreach (var sink in OutputSinkRegistry.Sinks)
        {
            if (sink is Instance candidate && candidate.SymbolChildId == childId)
            {
                instance = candidate;
                return true;
            }
        }

        instance = null;
        return false;
    }

    private static string ReadName(Instance instance)
    {
        var childUi = instance.Parent?.GetSymbolUi().ChildUis.GetValueOrDefault(instance.SymbolChildId);
        var name = childUi?.SymbolChild.Name;
        return string.IsNullOrEmpty(name) ? instance.Symbol.Name : name!;
    }

    /// <summary>The op carries a name of its own when its <see cref="SymbolChild"/> name is set; otherwise it
    /// only shows its symbol's default. This is what distinguishes "Slice N" from "{op}.N".</summary>
    private static bool HasCustomName(Instance instance)
    {
        var childUi = instance.Parent?.GetSymbolUi().ChildUis.GetValueOrDefault(instance.SymbolChildId);
        return !string.IsNullOrEmpty(childUi?.SymbolChild.Name);
    }
}
