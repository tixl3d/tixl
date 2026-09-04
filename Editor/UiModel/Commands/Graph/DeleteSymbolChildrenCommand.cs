using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Editor.UiModel.Commands.Graph;

public class DeleteSymbolChildrenCommand : ICommand
{
    public string Name => "Delete Operator";
    public bool IsUndoable => true;

    private readonly string _creationStack;
        
    public DeleteSymbolChildrenCommand(SymbolUi compositionSymbolUi, List<SymbolUi.Child> uiChildrenToRemove)
    {
        if(compositionSymbolUi == null)
            throw new ArgumentNullException(nameof(compositionSymbolUi));
            
        _creationStack = Environment.StackTrace;
        var compositionSymbol = compositionSymbolUi.Symbol;
            
        if(compositionSymbol == null)
            throw new ArgumentNullException(nameof(compositionSymbol));
            
        _removedChildren = new ChildEntry[uiChildrenToRemove.Count];
        _compositionSymbolId = compositionSymbol.Id;

        for (int i = 0; i < uiChildrenToRemove.Count; i++)
        {
            var childUi = uiChildrenToRemove[i];
            var child = childUi.SymbolChild;

            var originalValuesForNonDefaultInputs = new Dictionary<Guid, InputValue>();
            foreach (var (id, input) in child.Inputs)
            {
                if(input.IsDefault )
                    continue;

                originalValuesForNonDefaultInputs[id] = input.Value.Clone();
            }

            var timeClipSettingsForOutputs = new Dictionary<Guid, TimeClip>();
            foreach (var (id, output) in child.Outputs)
            {
                if (output.OutputData is TimeClip timeClip)
                {
                    timeClipSettingsForOutputs[id] =timeClip.Clone();
                }
            }
                
            _removedChildren[i] = new ChildEntry()
                                      {
                                          SymbolId = child.Symbol.Id,
                                          ChildId = child.Id,
                                          ChildName = child.Name,
                                          PosInCanvas = childUi.PosOnCanvas,
                                          Size = childUi.Size,
                                          SectionId = childUi.SectionId,
                                          OriginalValuesForInputs = originalValuesForNonDefaultInputs,
                                          TimeClipSettingsForOutputs = timeClipSettingsForOutputs,
                                      };
        }
    }

    public void Do()
    {
        if(!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionSymbolUi))
        {
            this.LogError(false, $"Could not find symbol with id {_compositionSymbolId} - was it removed?\nCreated at stack:\n{_creationStack}", false);
            return;
        }
            
        var compositionSymbol = compositionSymbolUi!.Symbol;
        _removedConnections.Clear();

        foreach (var childUndoData in _removedChildren)
        {
            // first get the connections to the child that is removed and store these, this must be done before
            // each child is removed in order to preserve restore-able multi input indices.
            var connectionToRemove = compositionSymbol.Connections.FindAll(con => con.SourceParentOrChildId == childUndoData.ChildId
                                                                                  || con.TargetParentOrChildId == childUndoData.ChildId);
            connectionToRemove.Reverse();
            foreach (var con in connectionToRemove)
            {
                var entry = new ConnectionEntry
                                {
                                    Connection = con,
                                    MultiInputIndex = compositionSymbol.GetMultiInputIndexFor(con)
                                };
                _removedConnections.Add(entry);
                compositionSymbol.RemoveConnection(con, entry.MultiInputIndex);
            }

            compositionSymbolUi.RemoveChild(childUndoData.ChildId);
        }

        compositionSymbolUi.FlagAsModified();
        _removedConnections.Reverse(); // reverse in order to restore in reversed order
    }

    public void Undo()
    {
        if(!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionSymbolUi))
        {
            this.LogError(true, $"Could not find symbol with id {_compositionSymbolId} - was it removed?\nCreated at stack:\n{_creationStack}", false);
            return;
        }
            
        foreach (var childUndoData in _removedChildren)
        {
            if(!SymbolUiRegistry.TryGetSymbolUi(childUndoData.SymbolId, out var childSymbolUi))
            {
                this.LogError(true, $"Could not find symbol {childUndoData.SymbolId} - was it removed?\nCreated at stack:\n{_creationStack}", false);
                continue;
            }
                
            var symbol = childSymbolUi.Symbol;
            var symbolChildUi = compositionSymbolUi.AddChild(symbol, childUndoData.ChildId, childUndoData.PosInCanvas, childUndoData.Size);

            // Membership is usually re-derived from geometry, but ops hidden in a collapsed
            // section are sticky - without restoring the id they would come back orphaned.
            symbolChildUi.SectionId = childUndoData.SectionId;
            var symbolChild = symbolChildUi.SymbolChild;
                
            foreach (var (inputId, input) in symbolChild!.Inputs)
            {
                if(childUndoData.OriginalValuesForInputs.TryGetValue(inputId, out var originalValue))
                {
                    input.Value.Assign(originalValue);
                    input.IsDefault = false;
                }
            }

            foreach (var (outputId, output) in symbolChild.Outputs)
            {
                if (childUndoData.TimeClipSettingsForOutputs.TryGetValue(outputId, out var timeClip))
                {
                    output.OutputData.Assign(timeClip);
                }
            }


            symbolChild.Name = childUndoData.ChildName;
        }

        foreach (var entry in _removedConnections)
        {
            compositionSymbolUi!.Symbol.AddConnection(entry.Connection, entry.MultiInputIndex);
        }
            
        compositionSymbolUi.FlagAsModified();
    }
        
    private sealed class ChildEntry
    {
        public Guid SymbolId { get; set; }
        public Guid ChildId { get; set; }
        public string ChildName { get; set; }
        public Vector2 PosInCanvas { get; set; }
        public Vector2 Size { get; set; }
        public Guid SectionId { get; set; }
            
        public Dictionary<Guid, InputValue> OriginalValuesForInputs { get; set; }
        public Dictionary<Guid, TimeClip> TimeClipSettingsForOutputs { get; set; }
    }

    private sealed class ConnectionEntry
    {
        public Symbol.Connection Connection { get; set; }
        public int MultiInputIndex { get; set; }
    }

    private readonly Guid _compositionSymbolId;
    private readonly ChildEntry[] _removedChildren;
    private readonly List<ConnectionEntry> _removedConnections = new();
}