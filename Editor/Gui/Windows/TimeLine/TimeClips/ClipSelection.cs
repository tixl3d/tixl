#nullable enable
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Timing;
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
            // A selected id can briefly outlive its clip (e.g. right after a cut replaces it), so skip
            // stale selections rather than throwing KeyNotFoundException.
            if (CompositionTimeClips.TryGetValue(id, out var clip))
                yield return clip;
        }
    }

    public IEnumerable<TimeClip> GetSelectedClips()
    {
        foreach (var id in SelectedClipsIds)
        {
            // A selected id can briefly outlive its clip (e.g. right after a cut replaces it), so skip
            // stale selections rather than throwing KeyNotFoundException.
            if (CompositionTimeClips.TryGetValue(id, out var clip))
                yield return clip;
        }
    }

    
    public void UpdateForComposition(Instance compositionOp)
    {
        _compositionOp = compositionOp;
        CompositionTimeClips.Clear();

        // The effective main soundtrack renders as the timeline background instead of a clip block —
        // exclude it here so drawing, selection, split and boundary jumps all ignore it. Only the clip
        // the lookup actually resolves is hidden; a flagged-but-losing clip stays visible as a hint
        // that multiple clips claim the designation. Switch Display back to Clip to edit placement.
        var hiddenSoundtrackClipId = Guid.Empty;
        if (PlaybackUtils.TryFindingSoundtrack(out var soundtrackHandle, out _)
            && soundtrackHandle.Clip.Display == AudioClipDisplay.BackgroundImage
            && soundtrackHandle.Owner is IAudioClipProvider and Instance soundtrackOp
            && soundtrackOp.Parent == compositionOp)
        {
            hiddenSoundtrackClipId = soundtrackOp.SymbolChildId;
        }

        foreach (var s2 in Structure.GetAllTimeClips(compositionOp))
        {
            if (s2.Id == hiddenSoundtrackClipId)
                continue;

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
        _nodeSelection.TrySelectCompositionChild(_compositionOp, timeClip.Id);
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

        _nodeSelection.TrySelectCompositionChild(_compositionOp, matchingClip.Id);
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