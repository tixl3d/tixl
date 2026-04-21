namespace T3.Editor.UiModel.Commands.Graph;

public sealed class ChangeCommentCommand : ICommand
{
    public string Name => "Change comment";
    public bool IsUndoable => true;

    public ChangeCommentCommand(SymbolUi.Child symbolChildUi, Guid parentSymbolId, string newComment)
    {
        _symbolChildId = symbolChildUi.Id;
        _parentSymbolId = parentSymbolId;
        _originalComment = symbolChildUi.Comment;
        _newComment = newComment;
    }

    public void Do() => AssignValue(_newComment);
    public void Undo() => AssignValue(_originalComment);

    private void AssignValue(string newComment)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_parentSymbolId, out var symbolUi))
            return;

        if (!symbolUi.ChildUis.TryGetValue(_symbolChildId, out var childUi))
            return;

        childUi.Comment = newComment;
        symbolUi.FlagAsModified();
    }

    private readonly string _newComment;
    private readonly string _originalComment;
    private readonly Guid _symbolChildId;
    private readonly Guid _parentSymbolId;
}
