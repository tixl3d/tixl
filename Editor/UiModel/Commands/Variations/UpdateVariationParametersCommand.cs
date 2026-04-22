using T3.Core.Operator.Slots;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.UiModel.Commands.Variations;

/// <summary>
/// Replaces a variation's ParameterSetsForChildIds with a new snapshot. Used when the user
/// "updates" an existing snapshot/preset to capture the current instance parameters. Stores both
/// before/after snapshots so undo/redo round-trips cleanly.
/// </summary>
internal sealed class UpdateVariationParametersCommand : ICommand
{
    public string Name => "Update variation";
    public bool IsUndoable => true;

    private readonly SymbolVariationPool _pool;
    private readonly Variation _variation;
    private readonly Dictionary<Guid, Dictionary<Guid, InputValue>> _previousParameterSets;
    private readonly Dictionary<Guid, Dictionary<Guid, InputValue>> _newParameterSets;

    internal UpdateVariationParametersCommand(SymbolVariationPool pool,
                                              Variation variation,
                                              Dictionary<Guid, Dictionary<Guid, InputValue>> newParameterSets)
    {
        _pool = pool;
        _variation = variation;
        _previousParameterSets = Clone(variation.ParameterSetsForChildIds);
        _newParameterSets = Clone(newParameterSets);
    }

    public void Do() => Apply(_newParameterSets);
    public void Undo() => Apply(_previousParameterSets);

    private void Apply(Dictionary<Guid, Dictionary<Guid, InputValue>> source)
    {
        _variation.ParameterSetsForChildIds = Clone(source);
        _pool.SaveVariationsToFile();
    }

    private static Dictionary<Guid, Dictionary<Guid, InputValue>> Clone(Dictionary<Guid, Dictionary<Guid, InputValue>> source)
    {
        var result = new Dictionary<Guid, Dictionary<Guid, InputValue>>(source.Count);
        foreach (var (childId, inner) in source)
        {
            var clonedInner = new Dictionary<Guid, InputValue>(inner.Count);
            foreach (var (inputId, value) in inner)
                clonedInner[inputId] = value.Clone();
            result[childId] = clonedInner;
        }
        return result;
    }
}
