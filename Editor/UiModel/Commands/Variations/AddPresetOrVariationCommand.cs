using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.UiModel.Commands.Variations;

internal sealed class AddPresetOrVariationCommand : ICommand
{
    public string Name => "Add Preset";
    public bool IsUndoable => true;

    private readonly Guid _symbolId;
    private readonly Variation _newVariation;

    internal AddPresetOrVariationCommand(Symbol symbol, Variation variation)
    {
        _symbolId = symbol.Id;
        _newVariation = variation;
    }

    public void Do()
    {
        var pool = VariationHandling.GetOrLoadVariations(_symbolId);

        if (pool.AllVariations.Any(v => v.Id == _newVariation.Id))
        {
            Log.Warning($"Variations with id {_newVariation.Id} already exists for symbol {_symbolId}");
            return;
        }

        pool.AddUserVariation(_newVariation);
        FlagSymbolAsModified();
    }

    public void Undo()
    {
        var pool = VariationHandling.GetOrLoadVariations(_symbolId);

        if (pool.AllVariations.All(v => v.Id != _newVariation.Id))
        {
            Log.Warning($"No variations for symbol {_symbolId} with id {_newVariation.Id}");
            return;
        }


        pool.RemoveUserVariation(_newVariation);
        FlagSymbolAsModified();
    }

    private void FlagSymbolAsModified()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Could not find symbol with id {_symbolId} - was it removed?");
            return;
        }
            
        symbolUi!.FlagAsModified();
    }
}