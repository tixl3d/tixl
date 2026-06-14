#nullable enable
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.UiModel.Commands.Variations;

/// <summary>
/// Reassigns a snapshot/preset's <see cref="Variation.ActivationIndex"/> — the slot it occupies on a
/// MIDI controller grid. Dropping onto a free slot moves; dropping onto an occupied slot swaps the two
/// indices, so the controller never ends up with two snapshots fighting over one pad.
/// </summary>
internal sealed class ChangeVariationActivationIndexCommand : ICommand
{
    public string Name => "Reassign snapshot index";
    public bool IsUndoable => true;

    private readonly SymbolVariationPool _pool;
    private readonly Variation _variation;
    private readonly Variation? _displaced;
    private readonly int _oldIndex;
    private readonly int _newIndex;

    internal ChangeVariationActivationIndexCommand(SymbolVariationPool pool, Variation variation, int newIndex, Variation? displaced)
    {
        _pool = pool;
        _variation = variation;
        _displaced = displaced;
        _oldIndex = variation.ActivationIndex;
        _newIndex = newIndex;
    }

    public void Do()
    {
        _variation.ActivationIndex = _newIndex;
        if (_displaced != null)
            _displaced.ActivationIndex = _oldIndex;

        _pool.SaveVariationsToFile();
    }

    public void Undo()
    {
        _variation.ActivationIndex = _oldIndex;
        if (_displaced != null)
            _displaced.ActivationIndex = _newIndex;

        _pool.SaveVariationsToFile();
    }
}
