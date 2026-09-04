namespace T3.Editor.UiModel.Selection;

internal sealed class CanvasElementSelection : ISelection
{
    internal void Clear()
    {
        SelectedElements.Clear();
    }

    internal void SetSelection(ISelectableCanvasObject node)
    {
        if (node is SymbolUi.Child)
        {
            Log.Warning("Setting selection to a SymbolUi.Child without providing instance will lead to problems.");
        }

        Clear();
        AddSelection(node);
    }

    internal void AddSelection(ISelectableCanvasObject node)
    {
        if (SelectedElements.Contains(node))
            return;

        SelectedElements.Add(node);
    }

    internal IEnumerable<T> GetSelectedNodes<T>() where T : ISelectableCanvasObject
    {
        foreach (var item in SelectedElements)
        {
            if (item is T typedItem)
                yield return typedItem;
        }
    }
        
    public  bool IsNodeSelected(ISelectableCanvasObject node)
    {
        return SelectedElements.Contains(node);
    }

    internal void DeselectNode(ISelectableCanvasObject node)
    {
        SelectedElements.Remove(node);
    }

        
    public  bool IsAnythingSelected()
    {
        return SelectedElements.Count > 0;
    }

    internal readonly List<ISelectableCanvasObject> SelectedElements = new();
}