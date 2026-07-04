namespace T3.Editor.UiModel.Commands.Sections;

/// <summary>
/// Sets the section an op belongs to. Folded into the MacroCommands of moves, pastes,
/// and section edits so undo restores membership together with positions.
/// </summary>
public sealed class AssignSectionMembershipCommand : ICommand
{
    public string Name => "Assign section membership";
    public bool IsUndoable => true;

    public AssignSectionMembershipCommand(SymbolUi symbolUi, SymbolUi.Child childUi, Guid newSectionId)
    {
        _symbolId = symbolUi.Symbol.Id;
        _childId = childUi.Id;
        _originalSectionId = childUi.SectionId;
        _newSectionId = newSectionId;
    }

    public void Do()
    {
        Apply(_newSectionId);
    }

    public void Undo()
    {
        Apply(_originalSectionId);
    }

    private void Apply(Guid sectionId)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi)
            || !symbolUi.ChildUis.TryGetValue(_childId, out var childUi))
        {
            Log.Warning($"Can't update section membership - symbol {_symbolId} is no longer available.");
            return;
        }

        childUi.SectionId = sectionId;
        symbolUi.FlagAsModified();
    }

    private readonly Guid _symbolId;
    private readonly Guid _childId;
    private readonly Guid _originalSectionId;
    private readonly Guid _newSectionId;
}
