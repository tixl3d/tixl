using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Windows.Variations;

namespace T3.Editor.Gui.Interaction.Variations;

internal static class SnapshotActions
{
    /// <summary>
    /// Activates the snapshot at <paramref name="rawIndex"/> taken modulo the number of snapshots in the
    /// active pool (reading order), so any integer wraps to an existing snapshot. Applies it like a
    /// launchpad pad press; never creates a snapshot. Backs the [ActivateSnapshot] operator.
    /// </summary>
    public static void ActivateSnapshotByModuloIndex(int rawIndex)
    {
        var pool = VariationHandling.ActivePoolForSnapshots;
        var instance = VariationHandling.ActiveInstanceForSnapshots;
        if (pool == null || instance == null)
            return;

        var snapshots = new List<Variation>();
        foreach (var v in pool.AllVariations)
        {
            if (v.IsSnapshot)
                snapshots.Add(v);
        }

        if (snapshots.Count == 0)
            return;

        VariationBaseCanvas.SortByActivationIndex(snapshots);

        var count = snapshots.Count;
        var position = ((rawIndex % count) + count) % count;
        var variation = snapshots[position];

        // No undo step: a procedural trigger is automation output, not a user edit on the undo stack.
        pool.ApplyWithoutUndo(instance, variation);
        BlendActions.SetActiveSnapshot(variation.ActivationIndex);
    }

    public static void ActivateOrCreateSnapshotAtIndex(int activationIndex)
    {
        // Log.Debug($"SnapshotActions.ActivateOrCreateSnapshotAtIndex called with index {activationIndex}");
        
        if (VariationHandling.ActivePoolForSnapshots == null)
        {
            Log.Warning($"Can't save variation #{activationIndex}. No variation pool active.");
            return;
        }

        if (SymbolVariationPool.TryGetSnapshot(activationIndex, out var existingVariation))
        {
            // Log.Debug($"Activating existing snapshot at index {activationIndex}");
            VariationHandling.ActivePoolForSnapshots.Apply(VariationHandling.ActiveInstanceForSnapshots, existingVariation);
            BlendActions.SetActiveSnapshot(activationIndex);
            return;
        }

        // Log.Debug($"Creating new snapshot at index {activationIndex}");
        VariationHandling.CreateOrUpdateSnapshotVariation(activationIndex);
        VariationHandling.ActivePoolForSnapshots.UpdateActiveStateForVariation(activationIndex);
        BlendActions.SetActiveSnapshot(activationIndex);
    }

    public static void SaveSnapshotAtIndex(int activationIndex)
    {
        // Log.Debug($"SnapshotActions.SaveSnapshotAtIndex called with index {activationIndex}");
        
        if (VariationHandling.ActivePoolForSnapshots == null)
        {
            Log.Warning($"Can't save variation #{activationIndex}. No variation pool active.");
            return;
        }

        VariationHandling.CreateOrUpdateSnapshotVariation(activationIndex);
        VariationHandling.ActivePoolForSnapshots.UpdateActiveStateForVariation(activationIndex);
        BlendActions.SetActiveSnapshot(activationIndex);
    }

    public static void RemoveSnapshotAtIndex(int activationIndex)
    {
        // Log.Debug($"SnapshotActions.RemoveSnapshotAtIndex called with index {activationIndex}");
        
        if (VariationHandling.ActivePoolForSnapshots == null)
            return;

        //ActivePoolForSnapshots.DeleteVariation
        if (SymbolVariationPool.TryGetSnapshot(activationIndex, out var snapshot))
        {
            VariationHandling.ActivePoolForSnapshots.DeleteVariation(snapshot);
        }
        else
        {
            Log.Warning($"No preset to delete at index {activationIndex}");
        }
    }

    public static void SaveSnapshotAtNextFreeSlot(int obj)
    {
        //Log.Warning($"SaveSnapshotAtNextFreeSlot {obj} not implemented");
    }
}