using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.UiModel.Commands.Variations;

internal sealed class DeleteVariationCommand : ICommand
{
    public string Name => "Delete Variation";
    public bool IsUndoable => true;
        
    private readonly SymbolVariationPool _variationPool;
    private readonly Variation _originalVariation;
        
    public DeleteVariationCommand(SymbolVariationPool pool, Variation variation)
    {
        _variationPool = pool;
        _originalVariation = variation;
    }
        
    public void Undo()
    {
        _variationPool.AddUserVariation(_originalVariation);
    }

    public void Do()
    {
        _variationPool.RemoveUserVariation(_originalVariation);
    }
}