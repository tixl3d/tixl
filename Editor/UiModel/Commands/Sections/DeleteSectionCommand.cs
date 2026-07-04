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

    public void Do()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't delete section - symbol {_symbolId} is no longer available.");
            return;
        }

        // Members and nested sections inherit the deleted section's parent
        _reassignedChildIds.Clear();
        _reparentedSectionIds.Clear();
        var parentId = _originalSection.ParentSectionId;

        foreach (var childUi in symbolUi.ChildUis.Values)
        {
            if (childUi.SectionId != _originalSection.Id)
                continue;

            _reassignedChildIds.Add(childUi.Id);
            childUi.SectionId = parentId;
        }

        foreach (var section in symbolUi.Sections.Values)
        {
            if (section.ParentSectionId != _originalSection.Id)
                continue;

            _reparentedSectionIds.Add(section.Id);
            section.ParentSectionId = parentId;
        }

        symbolUi.Sections.Remove(_originalSection.Id);
        symbolUi.FlagAsModified();
    }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't restore section - symbol {_symbolId} is no longer available.");
            return;
        }

        symbolUi.Sections[_originalSection.Id] = _originalSection;

        foreach (var childId in _reassignedChildIds)
        {
            if (symbolUi.ChildUis.TryGetValue(childId, out var childUi))
                childUi.SectionId = _originalSection.Id;
        }

        foreach (var sectionId in _reparentedSectionIds)
        {
            if (symbolUi.Sections.TryGetValue(sectionId, out var section))
                section.ParentSectionId = _originalSection.Id;
        }

        symbolUi.FlagAsModified();
    }

    private readonly Guid _symbolId;
    private readonly Section _originalSection;
    private readonly List<Guid> _reassignedChildIds = [];
    private readonly List<Guid> _reparentedSectionIds = [];
}
