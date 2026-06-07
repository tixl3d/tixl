using T3.Core.Operator;

namespace T3.Editor.UiModel.Commands.Graph;

public sealed class AddSymbolChildCommand : ICommand
{
    public string Name => "Add Symbol Child";
    public bool IsUndoable => true;
    public Guid AddedChildId => _addedChildId;

    public AddSymbolChildCommand(Symbol compositionOp, Guid symbolIdToAdd)
    {
        if (compositionOp == null)
            return;

        _parentSymbolId = compositionOp.Id;
        _addedSymbolId = symbolIdToAdd;
        _addedChildId = Guid.NewGuid();
    }

    public void Undo()
    {
        if(!SymbolUiRegistry.TryGetSymbolUi(_parentSymbolId, out var parentSymbolUi))
        {
            Log.Warning($"Could not find symbol with id {_parentSymbolId} - was it removed?");
            return;
        }
            
        parentSymbolUi!.RemoveChild(_addedChildId);
    }

    public void Do()
    {
        if(!SymbolUiRegistry.TryGetSymbolUi(_parentSymbolId, out var parentSymbolUi))
        {
            Log.Warning($"Could not find symbol with id {_parentSymbolId} - was it removed?");
            return;
        }
            
        if(!SymbolUiRegistry.TryGetSymbolUi(_addedSymbolId, out var symbolToAdd))
        {
            Log.Warning($"Could not find symbol with id {_addedSymbolId} - was it removed?");
            return;
        }
            
        parentSymbolUi!.AddChild(symbolToAdd!.Symbol, _addedChildId, PosOnCanvas, Size, ChildName);
        InitContentClipSourceRange(parentSymbolUi.Symbol);
    }

    // Content clips (e.g. [VideoClip]) play from the start of their source, so their SourceRange is content-time
    // (0-based), not the placement position the generic TimeClip default uses. Set it on the new child's persisted
    // TimeClip here — editor-side — so it serializes and undoes together with the add (mirrors RecordingSession).
    private void InitContentClipSourceRange(Symbol composition)
    {
        if (!composition.Children.TryGetValue(_addedChildId, out var child)
            || !typeof(T3.Core.Operator.Slots.IContentTimeClip).IsAssignableFrom(child.Symbol.InstanceType))
            return;

        foreach (var output in child.Outputs.Values)
        {
            if (output.OutputData is T3.Core.Animation.TimeClip timeClip)
            {
                timeClip.SourceRange = new T3.Core.Animation.TimeRange(0f, timeClip.TimeRange.Duration);
                break;
            }
        }
    }

    // core data
    private readonly Guid _parentSymbolId;
    private readonly Guid _addedSymbolId;
    private readonly Guid _addedChildId;

    // ui data
    public Vector2 PosOnCanvas { get; set; } = Vector2.Zero;
    public Vector2 Size { get; set; } = SymbolUi.Child.DefaultOpSize;
    public string ChildName { get; set; } = string.Empty;
}