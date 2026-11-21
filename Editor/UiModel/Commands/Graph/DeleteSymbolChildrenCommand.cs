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

    // Read access for the collab wire encoder; does not participate in undo semantics.
    internal Guid CompositionSymbolIdForCollab => _compositionSymbolId;
    internal IEnumerable<Guid> RemovedChildIdsForCollab => _removedChildren.Select(e => e.ChildId);

    /// <summary>
    /// Snapshot of the children about to be removed, taken from the live graph while this command is
    /// created (before Do). The collab wire encoder ships it with the delete op so any participant
    /// can invert the delete (see restore_children). Mirrors what ChildEntry captures for local undo,
    /// plus the connections Do() will remove — without them an undone delete would lose wiring.
    /// </summary>
    internal CollabChildRestoreData[] CaptureCollabRestoreData()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var compositionSymbolUi))
            return [];

        var compositionSymbol = compositionSymbolUi.Symbol;
        var restoreData = new List<CollabChildRestoreData>(_removedChildren.Length);
        foreach (var entry in _removedChildren)
        {
            if (!compositionSymbolUi.ChildUis.TryGetValue(entry.ChildId, out var childUi))
                continue;

            var child = childUi.SymbolChild;
            var inputValues = new Dictionary<Guid, InputValue>();
            foreach (var (inputId, input) in child.Inputs)
            {
                if (input.IsDefault)
                    continue;

                inputValues[inputId] = input.Value.Clone();
            }

            var timeClips = new Dictionary<Guid, TimeClip>();
            foreach (var (outputId, output) in child.Outputs)
            {
                if (output.OutputData is TimeClip timeClip)
                    timeClips[outputId] = timeClip.Clone();
            }

            var removedConnections = new List<(Symbol.Connection Connection, int MultiInputIndex)>();
            foreach (var connection in compositionSymbol.Connections)
            {
                if (connection.SourceParentOrChildId != entry.ChildId && connection.TargetParentOrChildId != entry.ChildId)
                    continue;

                removedConnections.Add((connection, compositionSymbol.GetMultiInputIndexFor(connection)));
            }

            restoreData.Add(new CollabChildRestoreData
                                {
                                    SymbolId = entry.SymbolId,
                                    ChildId = entry.ChildId,
                                    Name = entry.ChildName,
                                    PosOnCanvas = entry.PosInCanvas,
                                    Size = entry.Size,
                                    SectionId = entry.SectionId,
                                    InputValues = inputValues,
                                    TimeClips = timeClips,
                                    Connections = removedConnections,
                                });
        }

        return restoreData.ToArray();
    }
}

/// <summary>
/// Pure data description of one removed child, sufficient for the collab wire to re-create it.
/// Values are clones; encoding happens after the command is created but before it executes.
/// </summary>
internal sealed class CollabChildRestoreData
{
    public Guid SymbolId { get; init; }
    public Guid ChildId { get; init; }
    public string Name { get; init; } = string.Empty;
    public Vector2 PosOnCanvas { get; init; }
    public Vector2 Size { get; init; }
    public Guid SectionId { get; init; }
    public Dictionary<Guid, InputValue> InputValues { get; init; } = new();
    public Dictionary<Guid, TimeClip> TimeClips { get; init; } = new();
    public List<(Symbol.Connection Connection, int MultiInputIndex)> Connections { get; init; } = new();
}