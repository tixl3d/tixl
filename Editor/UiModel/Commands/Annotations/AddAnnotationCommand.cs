namespace T3.Editor.UiModel.Commands.Annotations;

public sealed class AddAnnotationCommand : ICommand
{
    public string Name => "Add Annotation";
    public bool IsUndoable => true;

    public AddAnnotationCommand(SymbolUi symbolUi, Annotation annotation)
    {
        _symbolId = symbolUi.Symbol.Id;
        _newAnnotation = annotation;
    }

    public void Do()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't add annotation - symbol {_symbolId} is no longer available.");
            return;
        }

        symbolUi.Annotations[_newAnnotation.Id] = _newAnnotation;
        symbolUi.FlagAsModified();
    }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_symbolId, out var symbolUi))
        {
            Log.Warning($"Can't remove annotation - symbol {_symbolId} is no longer available.");
            return;
        }

        symbolUi.Annotations.Remove(_newAnnotation.Id);
        symbolUi.FlagAsModified();
    }

    private readonly Guid _symbolId;
    private readonly Annotation _newAnnotation;
}
