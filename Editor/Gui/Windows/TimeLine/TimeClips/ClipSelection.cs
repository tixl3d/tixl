#nullable enable
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Maps selection of <see cref="TimeClip"/>s
/// to <see cref="NodeSelection"/> with <see cref="ISelectableCanvasObject"/>s.
/// </summary>
internal sealed class ClipSelection
{
    public ClipSelection(NodeSelection nodeSelection)
    {
        _nodeSelection = nodeSelection;
    }
 
    public HashSet<Guid> SelectedClipsIds { get; } = new(100);

    public int Count => SelectedClipsIds.Count;
    public IReadOnlyCollection<Guid> AllOrSelectedClipIds => SelectedClipsIds.Count > 0 ? SelectedClipsIds : AllClipIds;
    public IReadOnlyCollection<Guid> AllClipIds => CompositionTimeClips.Keys;

    public IEnumerable<TimeClip> GetAllOrSelectedClips()
    {
        if (SelectedClipsIds.Count == 0)
        {
            foreach (var clip in CompositionTimeClips.Values)
            {
                yield return clip;
                
            }
            
            yield break;
        }

        foreach (var id in SelectedClipsIds)
        {
            yield return CompositionTimeClips[id];
        }
    }
    
    public void UpdateForComposition(Instance compositionOp)
    {
        _compositionOp = compositionOp;
        CompositionTimeClips.Clear();

        foreach (var s2 in Structure.GetAllTimeClips(compositionOp))
        {
            CompositionTimeClips[s2.Id] = s2;
        }
        
        // TODO: It's  unfortunate that Selection is a list makes access slow
        SelectedClipsIds.Clear();
        foreach (var selectedGraphNode in _nodeSelection.Selection)
        {
            if (CompositionTimeClips.TryGetValue(selectedGraphNode.Id, out var selectedTimeClip))
            {
                if (!SelectedClipsIds.Add(selectedTimeClip.Id))
                {
                    Log.Warning($"Clip {selectedTimeClip.Id} selected twice?");
                }
            }
        }
    }

    public void Clear()
    {
        if (_compositionOp == null) 
            return;
            
        foreach (var id in SelectedClipsIds)
        {
            _nodeSelection.DeselectCompositionChild(_compositionOp, id);
        }
                
        SelectedClipsIds.Clear();
    }

    public void Select(TimeClip timeClip)
    {
        if (_compositionOp == null) 
            return;
            
        _nodeSelection.Clear();
        _nodeSelection.SelectCompositionChild(_compositionOp, timeClip.Id);
        SelectedClipsIds.Clear();
        SelectedClipsIds.Add(timeClip.Id);
    }

    public void Deselect(TimeClip timeClip)
    {
        if (_compositionOp == null) 
            return;

        _nodeSelection.DeselectCompositionChild(_compositionOp, timeClip.Id);
        SelectedClipsIds.Remove(timeClip.Id);
    }

    public void AddSelection(TimeClip matchingClip)
    {
        if (_compositionOp == null) 
            return;

        _nodeSelection.SelectCompositionChild(_compositionOp, matchingClip.Id);
        SelectedClipsIds.Add(matchingClip.Id);
    }
            


    public bool Contains(TimeClip clip)
    {
        return SelectedClipsIds.Contains(clip.Id);
    }
            
    /// <summary>
    /// Reusing static collections to avoid GC leaks
    /// </summary>
    internal readonly Dictionary<Guid, TimeClip> CompositionTimeClips = new(100);

    private Instance? _compositionOp;
    private readonly NodeSelection _nodeSelection;
}