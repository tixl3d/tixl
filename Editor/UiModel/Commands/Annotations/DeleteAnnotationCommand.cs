namespace T3.Editor.UiModel.Commands.Annotations;

public sealed class DeleteAnnotationCommand : ICommand
{
    public string Name => "Delete Annotation";
    public bool IsUndoable => true;

    public DeleteAnnotationCommand(SymbolUi symbolUi, Annotation annotation)
    {
        _symbolId = symbolUi.Symbol.Id;
        _originalAnnotation = annotation;
    }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't restore annotation - symbol {_symbolId} is no longer available.");
            return;
        }

        symbolUi.Annotations[_originalAnnotation.Id] = _originalAnnotation;
    }

    public void Do()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't delete annotation - symbol {_symbolId} is no longer available.");
            return;
        }

        symbolUi.Annotations.Remove(_originalAnnotation.Id);
    }

    private readonly Guid _symbolId;
    private readonly Annotation _originalAnnotation;
}
