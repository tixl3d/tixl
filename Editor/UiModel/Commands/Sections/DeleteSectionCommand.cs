namespace T3.Editor.UiModel.Commands.Sections;

public sealed class DeleteSectionCommand : ICommand
{
    public string Name => "Delete Section";
    public bool IsUndoable => true;

    public DeleteSectionCommand(SymbolUi symbolUi, Section section)
    {
        _symbolId = symbolUi.Symbol.Id;
        _originalSection = section;
    }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't restore section - symbol {_symbolId} is no longer available.");
            return;
        }

        symbolUi.Sections[_originalSection.Id] = _originalSection;
    }

    public void Do()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't delete section - symbol {_symbolId} is no longer available.");
            return;
        }

        symbolUi.Sections.Remove(_originalSection.Id);
    }

    private readonly Guid _symbolId;
    private readonly Section _originalSection;
}
