#nullable enable
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Animation;

/// <summary>
/// Removes a set of <see cref="TimelineAudioClip"/>s from the composition's
/// <c>Playback.AudioClips</c> list. Undo restores each clip at its original index so list
/// order is preserved across redo cycles.
///
/// Resolves the composition by symbol Guid on every Do/Undo to survive hot reloads.
/// </summary>
internal sealed class DeleteTimelineAudioClipsCommand : ICommand
{
    public string Name => "Delete Audio Clip";
    public bool IsUndoable => true;

    private readonly Guid _compositionSymbolId;
    private readonly (TimelineAudioClip clip, int originalIndex)[] _entries;

    internal DeleteTimelineAudioClipsCommand(Instance compositionOp, IReadOnlyList<TimelineAudioClip> clips)
    {
        _compositionSymbolId = compositionOp.Symbol.Id;
        var list = compositionOp.Symbol.CompositionSettings?.Playback.AudioClips;
        _entries = new (TimelineAudioClip, int)[clips.Count];
        for (var i = 0; i < clips.Count; i++)
        {
            // If CompositionSettings is null, treat the clip as not-in-list (-1) — Undo
            // will append at end rather than crash.
            _entries[i] = (clips[i], list?.IndexOf(clips[i]) ?? -1);
        }
    }

    public void Do()
    {
        if (!TryGetClipList(out var list, out var symbolUi))
            return;
        foreach (var (clip, _) in _entries)
        {
            list.Remove(clip);
        }
        symbolUi.FlagAsModified();
    }

    public void Undo()
    {
        if (!TryGetClipList(out var list, out var symbolUi))
            return;
        // Restore lowest index first so subsequent inserts land at the correct positions.
        var sorted = new (TimelineAudioClip clip, int originalIndex)[_entries.Length];
        Array.Copy(_entries, sorted, _entries.Length);
        Array.Sort(sorted, (a, b) => a.originalIndex.CompareTo(b.originalIndex));
        foreach (var (clip, originalIndex) in sorted)
        {
            var insertAt = Math.Min(originalIndex, list.Count);
            list.Insert(insertAt, clip);
        }
        symbolUi.FlagAsModified();
    }

    private bool TryGetClipList(out List<TimelineAudioClip> clips, out SymbolUi symbolUi)
    {
        clips = null!;
        symbolUi = null!;
        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var ui))
        {
            Log.Warning($"DeleteTimelineAudioClipsCommand: symbol {_compositionSymbolId} not found — was the project closed?");
            return false;
        }
        var settings = ui.Symbol.CompositionSettings;
        if (settings == null)
        {
            Log.Warning($"DeleteTimelineAudioClipsCommand: symbol {_compositionSymbolId} has no CompositionSettings.");
            return false;
        }
        symbolUi = ui;
        clips = settings.Playback.AudioClips;
        return true;
    }
}
