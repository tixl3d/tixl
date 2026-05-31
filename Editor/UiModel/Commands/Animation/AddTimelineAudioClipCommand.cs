#nullable enable
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.UiModel.Commands.Animation;

/// <summary>
/// Inserts a new <see cref="TimelineAudioClip"/> into the composition's
/// <c>Playback.AudioClips</c> list. Used by the LayersArea drop handler.
///
/// Resolves the composition by symbol Guid on every Do/Undo to survive hot reloads.
/// </summary>
internal sealed class AddTimelineAudioClipCommand : ICommand
{
    public string Name => "Add Audio Clip";
    public bool IsUndoable => true;

    private readonly Guid _compositionSymbolId;
    private readonly TimelineAudioClip _clip;

    internal AddTimelineAudioClipCommand(Instance compositionOp, TimelineAudioClip clip)
    {
        _compositionSymbolId = compositionOp.Symbol.Id;
        _clip = clip;
    }

    public void Do()
    {
        if (!TryGetClipList(out var list, out var symbolUi))
            return;
        list.Add(_clip);
        symbolUi.FlagAsModified();
    }

    public void Undo()
    {
        if (!TryGetClipList(out var list, out var symbolUi))
            return;
        list.Remove(_clip);
        symbolUi.FlagAsModified();
    }

    private bool TryGetClipList(out List<TimelineAudioClip> clips, out SymbolUi symbolUi)
    {
        clips = null!;
        symbolUi = null!;
        if (!SymbolUiRegistry.TryGetSymbolUi(_compositionSymbolId, out var ui))
        {
            Log.Warning($"AddTimelineAudioClipCommand: symbol {_compositionSymbolId} not found — was the project closed?");
            return false;
        }
        symbolUi = ui;
        clips = ui.Symbol.CompositionSettings.Playback.AudioClips;
        return true;
    }
}
