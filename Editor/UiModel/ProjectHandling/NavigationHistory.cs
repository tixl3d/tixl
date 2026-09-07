#nullable enable
using T3.Core.Operator;

namespace T3.Editor.UiModel.ProjectHandling;

/// <summary>
/// Manage the navigation between previously selected instances.
/// This can also be used sort items by relevancy in search dialog.
/// </summary>
internal sealed class NavigationHistory
{

    public NavigationHistory(Structure structure)
    {
        _structure = structure;
    }
        
    /// <summary>
    /// abC   select D -> append...
    /// abcD  select C -> keep order if new selection is next to current index 
    /// abCd  select A -> resort
    /// bcdA
    /// 
    /// </summary>
    public void UpdateSelectedInstance(Instance instance)
    {
        var path = instance.InstancePath;
        var hash = GetHashForIdPath(path);

        if (!_idPathsByHash.TryGetValue(hash, out var previousPath))
        {
            _idPathsByHash[hash] = path;
            _previousSelections.Insert(0, path);
            _currentIndex = 0;
            return;
        }

        var index = _previousSelections.IndexOf(previousPath);
        if (index == -1)
        {
            Log.Warning("Inconsistent navigation path");
            return;
        }

        if (index == 0)
        {
            _currentIndex = 0;
            return;
        }
            

        // Keep order
        if (Math.Abs(index - _currentIndex) <= 1)
        {
            _currentIndex = index;
            return;
        }
        
        // Rearrange

        _previousSelections.RemoveAt(index);
        _previousSelections.Insert(0, previousPath);
        _currentIndex = 0;
    }

    public Instance? GetLastSelectedInstance()
    {
        if (_previousSelections.Count == 0)
            return null;
        
        return _previousSelections.Count < _currentIndex 
                   ? null 
                   : _structure.GetInstanceFromIdPath(_previousSelections[_currentIndex]);
    }

    public IEnumerable<Instance> GetPreviouslySelectedInstances()
    {
        foreach (var path in _previousSelections)
        {
            var instance = _structure.GetInstanceFromIdPath(path);
            if (instance == null)
                continue;

            yield return instance;
        }
    }

    internal IReadOnlyList<Guid>? NavigateBackwards()
    {
        while (_currentIndex < _previousSelections.Count - 1)
        {
            _currentIndex++;
            var path = _previousSelections[_currentIndex];
            if (_structure.GetInstanceFromIdPath(path) == null)
                continue;

            return path;
        }

        return null;
    }

    internal IReadOnlyList<Guid>? NavigateForward()
    {
        while (_currentIndex > 0)
        {
            _currentIndex--;
            var path = _previousSelections[_currentIndex];
            if (_structure.GetInstanceFromIdPath(path) == null)
                continue;

            return path;
        }

        return null;
    }

    private long GetHashForIdPath(IReadOnlyList<Guid> path)
    {
        long hash = 31.GetHashCode();
        foreach (var id in path)
        {
            hash = hash * 31 + id.GetHashCode();
        }

        return hash;
    }

    private int _currentIndex;
    private Structure _structure;
    
    private readonly List<IReadOnlyList<Guid>> _previousSelections = new();
    private readonly Dictionary<long, IReadOnlyList<Guid>> _idPathsByHash = new();
}