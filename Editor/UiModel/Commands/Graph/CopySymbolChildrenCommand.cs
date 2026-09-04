#nullable enable
using T3.Core.Operator;

// ReSharper disable PossibleMultipleEnumeration

namespace T3.Editor.UiModel.Commands.Graph;

public sealed class CopySymbolChildrenCommand : ICommand
{
    public string Name => "Copy Symbol Children";

    public bool IsUndoable => true;

    internal Dictionary<Guid, Guid> OldToNewChildIds { get; } = new();
    internal Dictionary<Guid, Guid> OldToSectionIds { get; } = new();

    public enum CopyMode
    {
        Normal,
        ClipboardSource,
        ClipboardTarget
    }

    public CopySymbolChildrenCommand(SymbolUi sourceCompositionUi,
                                     List<SymbolUi.Child>? symbolChildrenToCopy,
                                     List<Section>? selectedSections,
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
        if (selectedSections != null && selectedSections.Count > 0)
        {
            foreach (var a in selectedSections)
            {
                if (!a.Collapsed)
                    continue;

                foreach (var childUi in sourceCompositionUi.ChildUis.Values)
                {
                    if (childUi.SectionId == a.Id && !symbolChildrenToCopy.Contains(childUi))
                    {
                        symbolChildrenToCopy.Add(childUi);
                    }
                }
            }
        }
        
        
        selectedSections ??=  sourceCompositionUi.Sections.Values.ToList();
        
        var upperLeftCorner = new Vector2(float.MaxValue, float.MaxValue);
        foreach (var childToCopy in symbolChildrenToCopy)
        {
            upperLeftCorner = Vector2.Min(upperLeftCorner, childToCopy.PosOnCanvas);
        }
        
        foreach(var sectionToCopy in selectedSections) 
        {
            upperLeftCorner = Vector2.Min(upperLeftCorner, sectionToCopy.PosOnCanvas);
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
        
        _sectionsToCopy = [];
        foreach (var orgSection in selectedSections)
        {
            var newSection = orgSection.Clone();
            newSection.PosOnCanvas += PositionOffset;
            _sectionsToCopy.Add(newSection);
            OldToSectionIds[orgSection.Id] = newSection.Id;
        }

        // Remap nesting between copied sections. Parents that weren't copied keep their id -
        // valid when duplicating within the same composition, cleared by the consistency
        // check when pasting into a composition where that section doesn't exist.
        foreach (var newSection in _sectionsToCopy)
        {
            if (OldToSectionIds.TryGetValue(newSection.ParentSectionId, out var newParentId))
            {
                newSection.ParentSectionId = newParentId;
            }
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

        foreach (var section in _sectionsToCopy)
        {
            parentSymbolUi.Sections.Remove(section.Id);
        }

        NewSymbolChildIds.Clear();
        NewSymbolSectionIds.Clear();
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

        var bypassedChildIds = new List<Guid>();

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
                // Defer until after connections are added — Symbol.Child.SetBypassed
                // skips the assignment when the new child has live instances but no
                // output connection yet (which is always the case at this point during paste/duplicate).
                bypassedChildIds.Add(newSymbolChild.Id);
            }

            // Remap membership to the copied section
            if (newChildUi.SectionId != Guid.Empty)
            {
                if (OldToSectionIds.TryGetValue(newChildUi.SectionId, out var newSectionId))
                {
                    newChildUi.SectionId = newSectionId;
                }
            }
        }

        // add connections between copied children
        foreach (var connection in _connectionsToCopy)
        {
            targetCompositionSymbolUi.Symbol.AddConnection(connection);
        }

        // Apply bypass state now that connections exist so SetBypassed propagates to instances
        foreach (var bypassedChildId in bypassedChildIds)
        {
            if (targetCompositionSymbolUi.Symbol.Children.TryGetValue(bypassedChildId, out var bypassedChild))
            {
                bypassedChild.IsBypassed = true;
            }
        }
        
        foreach (var newSection in _sectionsToCopy)
        {
            targetCompositionSymbolUi.Sections[newSection.Id] = newSection;
            NewSymbolSectionIds.Add(newSection.Id);
        }
        
        targetCompositionSymbolUi.FlagAsModified();
    }

    internal readonly List<Guid> NewSymbolChildIds = []; //This primarily used for selecting the new children
    internal readonly List<Guid> NewSymbolSectionIds = []; //This primarily used for selecting the new children

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

    // Clipboard modes copy from / into a transient symbol that is parsed from clipboard JSON and
    // never registered in SymbolUiRegistry, so it cannot be re-resolved by Guid like the other paths.
    // Holding it by reference is safe here: a detached, unregistered symbol is not recreated by package
    // hot reload. For Normal mode these stay null and resolution goes through _sourceSymbolId/_targetSymbolId.
    private readonly Symbol? _sourcePastedSymbol;
    private readonly SymbolUi? _clipboardSymbolUi;
    private readonly Guid _targetSymbolId;
    private readonly List<ChildCopy> _childrenToCopy = [];
    private readonly List<Section> _sectionsToCopy = [];
    private readonly List<Symbol.Connection> _connectionsToCopy = [];
    public Vector2 PositionOffset;
}