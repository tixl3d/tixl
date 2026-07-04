namespace T3.Editor.UiModel.Commands.Sections;

public sealed class ChangeSectionCollapseCommand : ICommand
{
    public string Name => _newCollapsed ? "Collapse Section" : "Expand Section";
    public bool IsUndoable => true;

    public ChangeSectionCollapseCommand(SymbolUi symbolUi, Section section, bool collapsed)
    {
        _symbolId = symbolUi.Symbol.Id;
        _sectionId = section.Id;
        _originalCollapsed = section.Collapsed;
        _newCollapsed = collapsed;
    }

    public void Do()
    {
        Apply(_newCollapsed);
    }

    public void Undo()
    {
        Apply(_originalCollapsed);
    }

    private void Apply(bool collapsed)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi)
            || !symbolUi.Sections.TryGetValue(_sectionId, out var section))
        {
            Log.Warning($"Can't change section collapse state - symbol {_symbolId} is no longer available.");
            return;
        }

        section.Collapsed = collapsed;
        symbolUi.FlagAsModified();
    }

    private readonly Guid _symbolId;
    private readonly Guid _sectionId;
    private readonly bool _originalCollapsed;
    private readonly bool _newCollapsed;
}
