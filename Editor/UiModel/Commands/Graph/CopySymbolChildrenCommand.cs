#nullable enable
using T3.Core.Operator;

// ReSharper disable PossibleMultipleEnumeration

namespace T3.Editor.UiModel.Commands.Graph;

public sealed class CopySymbolChildrenCommand : ICommand
{
    public string Name => "Copy Symbol Children";

    public bool IsUndoable => true;

    internal Dictionary<Guid, Guid> OldToNewChildIds { get; } = new();
    internal Dictionary<Guid, Guid> OldToAnnotationIds { get; } = new();

    public enum CopyMode
    {
        Normal,
        ClipboardSource,
        ClipboardTarget
    }

    public CopySymbolChildrenCommand(SymbolUi sourceCompositionUi,
                                     List<SymbolUi.Child>? symbolChildrenToCopy,
                                     List<Annotation>? selectedAnnotations,
                                     SymbolUi targetCompositionUi,
                                     Vector2 targetPosition, CopyMode copyMode = CopyMode.Normal, Symbol? sourceSymbol = null)
    {
        _copyMode = copyMode;

        if (copyMode == CopyMode.ClipboardSource)
        {
            _clipboardSymbolUi = sourceCompositionUi;
            _sourcePastedSymbol = sourceSymbol;
            _sourceSymbolId = sourceSymbol!.Id;
        }
        else
        {
            _sourceSymbolId = sourceCompositionUi.Symbol.Id;
            sourceSymbol = sourceCompositionUi.Symbol;
        }

        _targetSymbolId = targetCompositionUi.Symbol.Id;

        if (copyMode == CopyMode.ClipboardTarget)
        {
            _clipboardSymbolUi = targetCompositionUi;
            //_destructorAction = () => ((EditorSymbolPackage)targetCompositionUi.Symbol.SymbolPackage).RemoveSymbolUi(targetCompositionUi);
        }

        _targetPosition = targetPosition;

        symbolChildrenToCopy ??= sourceCompositionUi.ChildUis.Values.ToList();

        // Add collapsed children
        if (selectedAnnotations != null && selectedAnnotations.Count > 0)
        {
            foreach (var a in selectedAnnotations)
            {
                if (!a.Collapsed)
                    continue;

                foreach (var childUi in sourceCompositionUi.ChildUis.Values)
                {
                    if (childUi.CollapsedIntoAnnotationFrameId == a.Id && !symbolChildrenToCopy.Contains(childUi))
                    {
                        symbolChildrenToCopy.Add(childUi);
                    }
                }
            }
        }
        
        
        selectedAnnotations ??=  sourceCompositionUi.Annotations.Values.ToList();
        
        var upperLeftCorner = new Vector2(float.MaxValue, float.MaxValue);
        foreach (var childToCopy in symbolChildrenToCopy)
        {
            upperLeftCorner = Vector2.Min(upperLeftCorner, childToCopy.PosOnCanvas);
        }
        
        foreach(var annotationToCopy in selectedAnnotations) 
        {
            upperLeftCorner = Vector2.Min(upperLeftCorner, annotationToCopy.PosOnCanvas);
        }

        PositionOffset = targetPosition - upperLeftCorner;

        foreach (var childToCopy in symbolChildrenToCopy)
        {
            var entry = new ChildCopy(childToCopy.Id, 
                                      Guid.NewGuid(), 
                                      childToCopy.PosOnCanvas - upperLeftCorner, 
                                      childToCopy.Size);
            _childrenToCopy.Add(entry);
            OldToNewChildIds.Add(entry.OrgChildId, entry.NewChildId);
        }

        foreach (var entry in _childrenToCopy)
        {
            _connectionsToCopy.AddRange(from con in sourceSymbol.Connections
                                        where con.TargetParentOrChildId == entry.OrgChildId
                                        let newTargetId = OldToNewChildIds[entry.OrgChildId]
                                        from connectionSource in symbolChildrenToCopy
                                        where con.SourceParentOrChildId == connectionSource.Id
                                        let newSourceId = OldToNewChildIds[connectionSource.Id]
                                        select new Symbol.Connection(newSourceId, con.SourceSlotId, newTargetId, con.TargetSlotId));
        }
        _connectionsToCopy.Reverse(); // to keep multi input order
        
        _annotationsToCopy = [];
        foreach (var orgAnnotation in selectedAnnotations)
        {
            var newAnnotation = orgAnnotation.Clone();
            newAnnotation.PosOnCanvas += PositionOffset;
            _annotationsToCopy.Add(newAnnotation);
            OldToAnnotationIds[orgAnnotation.Id] = newAnnotation.Id;
        }
    }

    // ~CopySymbolChildrenCommand()
    // {
    //     _destructorAction?.Invoke();
    // }

    public void Undo()
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(_targetSymbolId, out var parentSymbolUi))
        {
            this.LogError(true, $"Failed to find target symbol with id: {_targetSymbolId} - was it removed?");
            return;
        }

        foreach (var child in _childrenToCopy)
        {
            parentSymbolUi.RemoveChild(child.NewChildId);
        }

        foreach (var annotation in _annotationsToCopy)
        {
            parentSymbolUi.Annotations.Remove(annotation.Id);
        }

        NewSymbolChildIds.Clear();
        NewSymbolAnnotationIds.Clear();
        parentSymbolUi.FlagAsModified();
    }

    public void Do()
    {
        SymbolUi? targetCompositionSymbolUi;
        SymbolUi? sourceCompositionSymbolUi;
        Symbol sourceCompositionSymbol;

        if (_copyMode == CopyMode.ClipboardTarget)
        {
            targetCompositionSymbolUi = _clipboardSymbolUi;
        }
        else if (!SymbolUiRegistry.TryGetSymbolUi(_targetSymbolId, out targetCompositionSymbolUi))
        {
            this.LogError(false, $"Failed to find target symbol with id: {_targetSymbolId} - was it removed?");
            return;
        }

        if (targetCompositionSymbolUi == null)
        {
            this.LogError(false, $"Undefined targetCompositionSymbolUi?");
            return;
        }

        if (_copyMode == CopyMode.ClipboardSource)
        {
            if (_clipboardSymbolUi == null)
            {
                this.LogError(false, $"Undefined symbolUi?");
                return;
            }

            sourceCompositionSymbolUi = _clipboardSymbolUi;
            sourceCompositionSymbol = _sourcePastedSymbol!;
        }
        else
        {
            if (!SymbolUiRegistry.TryGetSymbolUi(_sourceSymbolId, out sourceCompositionSymbolUi))
            {
                this.LogError(false, $"Failed to find source symbol with id: {_sourceSymbolId} - was it removed?");
                return;
            }

            sourceCompositionSymbol = sourceCompositionSymbolUi.Symbol;
        }

        var targetSymbol = targetCompositionSymbolUi.Symbol;

        // copy animations first, so when creating the new child instances can automatically create animations actions for the existing curves
        var childIdsToCopyAnimations = _childrenToCopy.Select(entry => entry.OrgChildId).ToList();
        var oldToNewIdDict = _childrenToCopy.ToDictionary(entry => entry.OrgChildId, entry => entry.NewChildId);
        sourceCompositionSymbol.Animator.CopyAnimationsTo(targetSymbol.Animator, childIdsToCopyAnimations, oldToNewIdDict);


        
        foreach (var childEntryToCopy in _childrenToCopy)
        {
            if (!sourceCompositionSymbol.Children.TryGetValue(childEntryToCopy.OrgChildId, out var symbolChildToCopy))
            {
                Log.Warning("Skipping attempt to copy undefined operator. This can be related to undo/redo operations. Please try to reproduce and tell pixtur");
                continue;
            }

            var symbolToAdd = symbolChildToCopy.Symbol;
            targetCompositionSymbolUi.AddChildAsCopyFromSource(symbolToAdd,
                                                               symbolChildToCopy,
                                                               sourceCompositionSymbolUi,
                                                               _targetPosition + childEntryToCopy.RelativePosition,
                                                               childEntryToCopy.NewChildId,
                                                               out var newSymbolChild,
                                                               out var newChildUi);

            //Symbol.Child newSymbolChild = targetSymbol.Children.Find(child => child.Id == childToCopy.AddedId);
            NewSymbolChildIds.Add(newSymbolChild.Id);
            
            var newSymbolInputs = newSymbolChild.Inputs;
            foreach (var (id, input) in symbolChildToCopy.Inputs)
            {
                var newInput = newSymbolInputs[id];
                newInput.Value.Assign(input.Value.Clone());
                newInput.IsDefault = input.IsDefault;
            }
            
            var newSymbolOutputs = newSymbolChild.Outputs;
            foreach (var (id, output) in symbolChildToCopy.Outputs)
            {
                var newOutput = newSymbolOutputs[id];

                if (output.OutputData != null)
                {
                    newOutput.OutputData.Assign(output.OutputData);
                }

                newOutput.DirtyFlagTrigger = output.DirtyFlagTrigger;
                newOutput.IsDisabled = output.IsDisabled;
            }

            if (symbolChildToCopy.IsBypassed)
            {
                newSymbolChild.IsBypassed = true;
            }
            
            // Update annotation id
            if (newChildUi.CollapsedIntoAnnotationFrameId != Guid.Empty)
            {
                if (OldToAnnotationIds.TryGetValue(newChildUi.CollapsedIntoAnnotationFrameId, out var newAnnotationId))
                {
                    newChildUi.CollapsedIntoAnnotationFrameId = newAnnotationId;
                }
            }            
        }

        // add connections between copied children
        foreach (var connection in _connectionsToCopy)
        {
            targetCompositionSymbolUi.Symbol.AddConnection(connection);
        }
        
        foreach (var newAnnotation in _annotationsToCopy)
        {
            targetCompositionSymbolUi.Annotations[newAnnotation.Id] = newAnnotation;
            NewSymbolAnnotationIds.Add(newAnnotation.Id);
        }
        
        targetCompositionSymbolUi.FlagAsModified();
    }

    internal readonly List<Guid> NewSymbolChildIds = []; //This primarily used for selecting the new children
    internal readonly List<Guid> NewSymbolAnnotationIds = []; //This primarily used for selecting the new children

    private struct ChildCopy
    {
        public ChildCopy(Guid orgChildId, Guid newChildId, Vector2 relativePosition, Vector2 size)
        {
            OrgChildId = orgChildId;
            NewChildId = newChildId;
            RelativePosition = relativePosition;
            Size = size;
        }

        public readonly Guid OrgChildId;
        public readonly Guid NewChildId;
        public readonly Vector2 RelativePosition;
        public readonly Vector2 Size;
    }

    private readonly CopyMode _copyMode;
    //private readonly Action? _destructorAction;

    private readonly Vector2 _targetPosition;
    private readonly Guid _sourceSymbolId;
    private readonly Symbol? _sourcePastedSymbol;
    private readonly SymbolUi? _clipboardSymbolUi;
    private readonly Guid _targetSymbolId;
    private readonly List<ChildCopy> _childrenToCopy = [];
    private readonly List<Annotation> _annotationsToCopy = [];
    private readonly List<Symbol.Connection> _connectionsToCopy = [];
    public Vector2 PositionOffset;
}