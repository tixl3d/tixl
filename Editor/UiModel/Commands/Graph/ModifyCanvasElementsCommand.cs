#nullable enable
using T3.Editor.UiModel.Selection;

namespace T3.Editor.UiModel.Commands.Graph;

internal sealed class ModifyCanvasElementsCommand : ICommand
{
    public string Name => "Move canvas elements";
    public bool IsUndoable => true;


    public ModifyCanvasElementsCommand(Guid compositionSymbolId, List<ISelectableCanvasObject> selectables, ISelection nodeSelection)
    {
        _compositionSymbolId = compositionSymbolId;
        _nodeSelection = nodeSelection;
        StoreSelectable(selectables);
    }

    private void StoreSelectable(List<ISelectableCanvasObject> selectables)
    {
        _entries = new Entry[selectables.Count];

        for (var i = 0; i < _entries.Length; i++)
        {
            var selectable = selectables[i];
            var entry = new Entry
                            {
                                SelectableId = selectable.Id,
                                OriginalPosOnCanvas = selectable.PosOnCanvas,
                                OriginalSize = selectable.Size,
                                PosOnCanvas = selectable.PosOnCanvas,
                                Size = selectable.Size
                            };
            _entries[i] = entry;
        }
    }

    public ModifyCanvasElementsCommand(ISelectionContainer selectionContainer, List<ISelectableCanvasObject> selectables, ISelection nodeSelection)
    {
        _selectionContainer = selectionContainer;
        _nodeSelection = nodeSelection;
        StoreSelectable(selectables);
    }

    public void StoreCurrentValues()
    {
        var selectables = GetSelectables(out _)?.ToArray();
        if (selectables == null)
            return;
            
        foreach (var entry in _entries)
        {
            var selectable = selectables.FirstOrDefault(s => s.Id == entry.SelectableId);
            if (selectable == null)
                continue;
                
            entry.PosOnCanvas = selectable.PosOnCanvas;
            entry.Size = selectable.Size;
            _nodeSelection.IsNodeSelected(selectable);
        }
        
        if(_selectionContainer is SymbolUi symbolUi)
            symbolUi.FlagAsModified();
    }

    public void Undo()
    {
        var selectables = GetSelectables(out var container)?.ToArray();
        if (selectables == null)
            return;

        var changed = false;
        foreach (var entry in _entries)
        {
            var selectable = selectables.FirstOrDefault(s => s.Id == entry.SelectableId);
            if (selectable == null)
                continue;
                
            changed |= selectable.PosOnCanvas != entry.OriginalPosOnCanvas || selectable.Size != entry.OriginalSize;
            selectable.PosOnCanvas = entry.OriginalPosOnCanvas;
            selectable.Size = entry.OriginalSize;
        }
            
        if(changed && container is SymbolUi symbolUi)
            symbolUi.FlagAsModified();
    }

    public void Do()
    {  
        var selectables = GetSelectables(out var container)?.ToArray();
        if (selectables == null)
            return;

        bool changed = false;
            
        foreach (var entry in _entries)
        {
            var selectable = selectables.SingleOrDefault(s => s.Id == entry.SelectableId);
            if (selectable == null)
                continue;
                
            changed |= selectable.PosOnCanvas != entry.PosOnCanvas || selectable.Size != entry.Size;
            selectable.PosOnCanvas = entry.PosOnCanvas;
            selectable.Size = entry.Size;
        }
            
        if(changed && container is SymbolUi symbolUi)
            symbolUi.FlagAsModified();
    }

    private IEnumerable<ISelectableCanvasObject>? GetSelectables(out ISelectionContainer? container)
    {
        if(_compositionSymbolId == Guid.Empty)
        {
            container = _selectionContainer;
        }
        else
        {
            SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var symbolUi);
            container = symbolUi;
        }
            
        return container?.GetSelectables();
    }
    
    private sealed class Entry
    {
        public Guid SelectableId;

        public Vector2 OriginalPosOnCanvas { get; init; }
        public Vector2 OriginalSize { get; init; }

        public Vector2 PosOnCanvas { get; set; }
        public Vector2 Size { get; set; }
    }

    private readonly ISelectionContainer? _selectionContainer;
    private Entry[] _entries = [];
    private readonly Guid _compositionSymbolId;
    private readonly ISelection _nodeSelection;

}