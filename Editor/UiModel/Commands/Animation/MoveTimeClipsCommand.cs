#nullable enable
using System.Diagnostics.CodeAnalysis;
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
    private readonly Guid[] _compositionPath;

    internal MoveTimeClipsCommand(Instance compositionOp, IReadOnlyList<TimeClip> clips)
    {
        _compositionPath = compositionOp.InstancePath.ToArray();
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
        

    /// <summary>The clip's ranges as captured at drag start. Lets drag handlers compute absolute updates
    /// from the originals instead of accumulating increments (e.g. trims that restore when dragged back).</summary>
    internal bool TryGetOriginalRanges(Guid clipId, out TimeRange timeRange, out TimeRange sourceRange)
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Id != clipId)
                continue;

            timeRange = _entries[i].TimeRangeOrg.Clone();
            sourceRange = _entries[i].SourceRangeOrg.Clone();
            return true;
        }

        timeRange = default;
        sourceRange = default;
        return false;
    }

    internal void StoreCurrentValues()
    {
        if (!TryGetCompositionOp(out var compositionOp))
            return;

        foreach (var clip in Structure.GetAllTimeClips(compositionOp))
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
        if (!TryGetCompositionOp(out var compositionOp))
            return;

        bool changed = false;
        foreach (var clip in Structure.GetAllTimeClips(compositionOp))
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
            compositionOp.GetSymbolUi().FlagAsModified();
        }
    }

    public void Do()
    {
        if (!TryGetCompositionOp(out var compositionOp))
            return;

        var allTimeClips = Structure.GetAllTimeClips(compositionOp).ToList();

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
                clip.LayerIndex--; // Move up
            }
                
            changed = true;
        }

        if (changed)
        {
            compositionOp.GetSymbolUi().FlagAsModified();
        }
    }

    /// <summary>
    /// Re-resolves the composition from its path rather than holding a live <see cref="Instance"/>, which would
    /// go stale across operator-package hot reloads while this command sits on the undo stack.
    /// </summary>
    private bool TryGetCompositionOp([NotNullWhen(true)] out Instance? compositionOp)
    {
        compositionOp = ProjectView.Focused?.Structure.GetInstanceFromIdPath(_compositionPath);
        if (compositionOp != null)
            return true;

        Log.Warning("Can't move time clips - composition is no longer available.");
        return false;
    }
}