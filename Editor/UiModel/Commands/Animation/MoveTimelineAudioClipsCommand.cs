#nullable enable
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Animation;

/// <summary>
/// Captures and restores TimeRange + LayerIndex for a set of <see cref="TimelineAudioClip"/>s.
/// Drives both body-drag (move) and start/end-handle drag (trim) — the caller mutates the
/// clip directly during drag and calls <see cref="StoreCurrentValues"/> on completion before
/// pushing the command to the undo stack.
///
/// Parallels <c>MoveTimeClipsCommand</c> for op-backed <c>TimeClip</c>s; the audio clip
/// version has no <c>SourceRange</c> field (source positioning is in seconds via
/// <see cref="TimelineAudioClip.SourceOffsetSecs"/>, not bars).
///
/// Resolves the composition by symbol Guid on every Do/Undo so hot-reloaded
/// <see cref="Instance"/>s don't leave the command holding stale references.
/// </summary>
internal sealed class MoveTimelineAudioClipsCommand : ICommand
{
    public string Name => "Move Audio Clip";
    public bool IsUndoable => true;

    private sealed class Entry
    {
        public Guid Id { get; init; }
        public TimeRange TimeRange { get; set; }
        public int LayerIndex { get; set; }
        public double SourceOffsetSecs { get; set; }
        public TimeRange TimeRangeOrg { get; init; }
        public int LayerIndexOrg { get; init; }
        public double SourceOffsetSecsOrg { get; init; }
    }

    private readonly Entry[] _entries;
    private readonly Guid _compositionSymbolId;

    internal MoveTimelineAudioClipsCommand(Instance compositionOp, IReadOnlyList<TimelineAudioClip> clips)
    {
        _compositionSymbolId = compositionOp.Symbol.Id;
        _entries = new Entry[clips.Count];
        for (var i = 0; i < _entries.Length; i++)
        {
            var clip = clips[i];
            _entries[i] = new Entry
                              {
                                  Id = clip.Id,
                                  TimeRange = clip.TimeRange,
                                  TimeRangeOrg = clip.TimeRange,
                                  LayerIndex = clip.LayerIndex,
                                  LayerIndexOrg = clip.LayerIndex,
                                  SourceOffsetSecs = clip.SourceOffsetSecs,
                                  SourceOffsetSecsOrg = clip.SourceOffsetSecs,
                              };
        }
    }

    internal void StoreCurrentValues()
    {
        if (!TryGetClipList(out var clips, out _))
            return;
        foreach (var clip in clips)
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Id != clip.Id)
                    continue;
                _entries[i].TimeRange = clip.TimeRange;
                _entries[i].LayerIndex = clip.LayerIndex;
                _entries[i].SourceOffsetSecs = clip.SourceOffsetSecs;
                break;
            }
        }
    }

    public void Do() => Apply(useOriginal: false);
    public void Undo() => Apply(useOriginal: true);

    private void Apply(bool useOriginal)
    {
        if (!TryGetClipList(out var clips, out var symbolUi))
            return;

        var changed = false;
        foreach (var clip in clips)
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Id != clip.Id)
                    continue;
                clip.TimeRange = useOriginal ? _entries[i].TimeRangeOrg : _entries[i].TimeRange;
                clip.LayerIndex = useOriginal ? _entries[i].LayerIndexOrg : _entries[i].LayerIndex;
                clip.SourceOffsetSecs = useOriginal ? _entries[i].SourceOffsetSecsOrg : _entries[i].SourceOffsetSecs;
                changed = true;
                break;
            }
        }

        if (changed)
            symbolUi.FlagAsModified();
    }

    private bool TryGetClipList(out List<TimelineAudioClip> clips, out SymbolUi symbolUi)
    {
        clips = null!;
        symbolUi = null!;
        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var ui))
        {
            Log.Warning($"MoveTimelineAudioClipsCommand: symbol {_compositionSymbolId} not found — was the project closed?");
            return false;
        }
        symbolUi = ui;
        clips = ui.Symbol.CompositionSettings.Playback.AudioClips;
        return true;
    }
}
