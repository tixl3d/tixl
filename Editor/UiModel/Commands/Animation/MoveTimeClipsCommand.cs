#nullable enable
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Animation;

internal sealed class MoveTimeClipsCommand : ICommand
{
    public string Name => "Move Time Clip";
    public bool IsUndoable => true;
        
    private sealed class  Entry
    {
        public Guid Id { get; set; }
        public TimeRange TimeRange { get; set; }
        public TimeRange SourceRange { get; set; }
        public int LayerIndex { get; set; }
        
        public TimeRange TimeRangeOrg { get; set; }
        public TimeRange SourceRangeOrg { get; set; }
        public int LayerIndexOrg { get; set; }
        
    }

    private readonly Entry[] _entries;
    private readonly Instance _compositionOp;

    internal MoveTimeClipsCommand(Instance compositionOp, IReadOnlyList<TimeClip> clips)
    {
        _compositionOp = compositionOp;
        _entries = new Entry[clips.Count];
        for (var i = 0; i < _entries.Length; i++)
        {
            var clip = clips[i];
            var entry = new Entry
                            {
                                Id = clip.Id,
                                TimeRange =clip.TimeRange.Clone(),
                                TimeRangeOrg = clip.TimeRange.Clone(),
                                SourceRange =clip.SourceRange.Clone(),
                                SourceRangeOrg =clip.SourceRange.Clone(),
                                LayerIndex = clip.LayerIndex,
                                LayerIndexOrg = clip.LayerIndex,
                            };
            _entries[i] = entry;
        }
    }
        

    internal void StoreCurrentValues()
    {
        foreach (var clip in Structure.GetAllTimeClips(_compositionOp))
        {
            var selectedEntry = _entries.SingleOrDefault(entry => entry.Id == clip.Id);
            if (selectedEntry == null)
                continue;

            selectedEntry.TimeRange = clip.TimeRange.Clone();
            selectedEntry.SourceRange = clip.SourceRange.Clone();
            selectedEntry.LayerIndex = clip.LayerIndex;
        }            
    }


    public void Undo()
    {
        bool changed = false;
        foreach (var clip in Structure.GetAllTimeClips(_compositionOp))
        {
            var selectedEntry = _entries.SingleOrDefault(entry => entry.Id == clip.Id);
            if (selectedEntry == null)
                continue;

            clip.TimeRange = selectedEntry.TimeRangeOrg.Clone();
            clip.SourceRange = selectedEntry.SourceRangeOrg.Clone();
            clip.LayerIndex = selectedEntry.LayerIndexOrg;
            changed = true;
        }

        if (changed)
        {
            _compositionOp.GetSymbolUi().FlagAsModified();
        }
    }

    public void Do()
    {
        var allTimeClips = Structure.GetAllTimeClips(_compositionOp).ToList();
            
        bool changed = false;
        foreach (var clip in allTimeClips)
        {
            var selectedEntry = _entries.SingleOrDefault(entry => entry.Id == clip.Id);
            if (selectedEntry == null)
                continue;
                
            clip.TimeRange = selectedEntry.TimeRange.Clone();
            clip.SourceRange = selectedEntry.SourceRange.Clone();
            clip.LayerIndex = selectedEntry.LayerIndex;

            while (clip.IsClipOverlappingOthers(allTimeClips))
            {
                clip.LayerIndex++;
            }
                
            changed = true;
        }

        if (changed)
        {
            _compositionOp.GetSymbolUi().FlagAsModified();
        }
    }
}