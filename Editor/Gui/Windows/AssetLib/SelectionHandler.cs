namespace T3.Editor.Gui.Windows.AssetLib;

internal sealed class SelectionHandler<TKey> where TKey : struct
{
    private readonly HashSet<TKey> _selected = new();
    
    public bool IsSelected(TKey key) => _selected.Contains(key);
    public IReadOnlyCollection<TKey> SelectedKeys => _selected;

    public void Select(TKey key) => _selected.Add(key);

    public void Deselect(TKey key) => _selected.Remove(key);

    public void Clear() => _selected.Clear();

    public void SetSelection(IEnumerable<TKey> keys)
    {
        _selected.Clear();
        foreach (var key in keys) _selected.Add(key);
    }

    public void AddSelection(IEnumerable<TKey> keys)
    {
        foreach (var key in keys) _selected.Add(key);
    }
}