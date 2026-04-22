using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.UiModel.Commands.Variations;

/// <summary>
/// Removes a set of child-ids from the parameter sets of multiple variations and restores them on
/// undo. Used when a child is disabled-for-snapshots or deleted — its parameters should no longer
/// appear in any snapshot belonging to its composition.
/// </summary>
internal sealed class RemoveInstancesFromVariationsCommand : ICommand
{
    public string Name => "Remove instances from variations";
    public bool IsUndoable => true;

    private readonly SymbolVariationPool _pool;
    private readonly List<Entry> _removedEntries = [];

    internal RemoveInstancesFromVariationsCommand(SymbolVariationPool pool,
                                                  IEnumerable<Guid> symbolChildIds,
                                                  IReadOnlyList<Variation> variations)
    {
        _pool = pool;

        var ids = symbolChildIds as ICollection<Guid> ?? symbolChildIds.ToList();
        foreach (var variation in variations)
        {
            foreach (var id in ids)
            {
                if (!variation.ParameterSetsForChildIds.TryGetValue(id, out var parameterSet))
                    continue;

                _removedEntries.Add(new Entry(variation, id, CloneInner(parameterSet)));
            }
        }
    }

    public void Do()
    {
        foreach (var entry in _removedEntries)
            entry.Variation.ParameterSetsForChildIds.Remove(entry.ChildId);

        _pool.SaveVariationsToFile();
    }

    public void Undo()
    {
        foreach (var entry in _removedEntries)
            entry.Variation.ParameterSetsForChildIds[entry.ChildId] = CloneInner(entry.ParameterSet);

        _pool.SaveVariationsToFile();
    }

    private static Dictionary<Guid, InputValue> CloneInner(Dictionary<Guid, InputValue> source)
    {
        var clone = new Dictionary<Guid, InputValue>(source.Count);
        foreach (var (inputId, value) in source)
            clone[inputId] = value.Clone();
        return clone;
    }

    private readonly record struct Entry(Variation Variation, Guid ChildId, Dictionary<Guid, InputValue> ParameterSet);
}
