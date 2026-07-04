namespace T3.Editor.UiModel.Commands.Sections;

public sealed class AddSectionCommand : ICommand
{
    public string Name => "Add Section";
    public bool IsUndoable => true;

    public AddSectionCommand(SymbolUi symbolUi, Section section)
    {
        _symbolId = symbolUi.Symbol.Id;
        _newSection = section;
    }

    public void Do()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't add section - symbol {_symbolId} is no longer available.");
            return;
        }

        symbolUi.Sections[_newSection.Id] = _newSection;
        symbolUi.FlagAsModified();
    }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't remove section - symbol {_symbolId} is no longer available.");
            return;
        }

        symbolUi.Sections.Remove(_newSection.Id);
        symbolUi.FlagAsModified();
    }

    private readonly Guid _symbolId;
    private readonly Section _newSection;
}
