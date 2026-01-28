using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.Gui.Interaction.Variations;

internal static class SnapshotActions
{
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