#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// All interaction concerns for symbol-level <see cref="TimelineAudioClip"/>s in the
/// timeline-clip area: selection set, draw loop dispatch, file drop, delete.
///
/// Per-item rendering + drag handling lives in <see cref="TimelineAudioClipItem"/>; this
/// class owns the cross-frame state (selection, active drag command) and the operations
/// that span multiple clips (delete-selected, drop-file).
/// </summary>
internal sealed class AudioClipInteractions
{
    public AudioClipInteractions(ClipArea.LayerContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Cross-frame drag state for moving / trimming <see cref="TimelineAudioClip"/>s.
    /// Set by <see cref="TimelineAudioClipItem"/> on drag start, pushed to the undo stack
    /// on drag completion. Static because there is at most one active drag in flight.
    /// </summary>
    internal static MoveTimelineAudioClipsCommand? ActiveMoveCommand;

    /// <summary>
    /// Selection by clip Guid. Parallel to op-backed <see cref="TimeClipInteractions"/>
    /// selection — the two are kept separate because <c>ClipSelection</c> is keyed by
    /// <c>TimeClip</c> references and can't hold audio clips.
    /// </summary>
    public HashSet<Guid> SelectedClipIds { get; } = new();

    public void DrawClips(Instance compositionOp, ImRect layerRect, int minLayerIndex, ImDrawListPtr drawList)
    {
        var audioClips = compositionOp.Symbol.CompositionSettings.Playback.AudioClips;
        var attrs = new TimelineAudioClipItem.DrawAttrs(
            layerRect, minLayerIndex, drawList, SelectedClipIds, _context.TimeCanvas, compositionOp);

        foreach (var ac in audioClips)
        {
            // Skip the main soundtrack — it still renders as the timeline background image
            // via TimeLineImage. Empty AssetPath = unconfigured entry.
            if (ac.IsMainSoundtrack || string.IsNullOrEmpty(ac.AssetPath))
                continue;
            TimelineAudioClipItem.DrawClip(ac, ref attrs);
        }
    }

    public void DeleteSelectedClips(Instance compositionOp)
    {
        if (SelectedClipIds.Count == 0)
            return;

        var allClips = compositionOp.Symbol.CompositionSettings.Playback.AudioClips;
        var toDelete = new List<TimelineAudioClip>();
        foreach (var clip in allClips)
        {
            if (SelectedClipIds.Contains(clip.Id))
                toDelete.Add(clip);
        }

        if (toDelete.Count == 0)
            return;

        UndoRedoStack.AddAndExecute(new DeleteTimelineAudioClipsCommand(compositionOp, toDelete));
        SelectedClipIds.Clear();
    }

    public void ClearSelection() => SelectedClipIds.Clear();

    /// <summary>
    /// Handles drag-drop of external audio files onto the clip area. Called once per frame
    /// after the area's group has closed (so the group's bounding rect is the current ImGui
    /// "last item" — that's the drop target).
    /// </summary>
    public void HandleFileDrop(Instance compositionOp,
                               Vector2 minScreenPos,
                               int minLayerIndex,
                               Playback? playback)
    {
        var result = DragAndDropHandling.TryHandleDropOnItem(
            DragAndDropHandling.DragTypes.ExternalFile,
            out var data);

        if (result != DragAndDropHandling.DragInteractionResult.Dropped || string.IsNullOrEmpty(data))
            return;

        if (playback == null)
            return;

        var package = compositionOp.Symbol.SymbolPackage;
        if (package == null)
        {
            Log.Warning("Cannot resolve composition's resource package for audio drop.");
            return;
        }

        var mousePos = ImGui.GetMousePos();
        var dropTimeBars = (float)_context.TimeCanvas.InverseTransformX(mousePos.X);

        int dropLayerIndex;
        if (minLayerIndex == int.MaxValue)
        {
            dropLayerIndex = 0;
        }
        else
        {
            var layerOffset = (mousePos.Y - minScreenPos.Y - ClipArea.LayerHeight * 0.5f) / ClipArea.LayerHeight;
            dropLayerIndex = minLayerIndex + (int)Math.Round(layerOffset);
        }

        // The drop payload may carry multiple files separated by "|" (the graph drop handler
        // splits on it); import each in turn.
        var commands = new List<ICommand>();
        foreach (var path in data.Split('|'))
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".wav" && ext != ".mp3" && ext != ".ogg")
                continue;

            if (!FileImport.TryImportDroppedFile(path, package, null, out var asset))
            {
                Log.Warning($"Failed to import audio file: {path}");
                continue;
            }

            // Probe the source duration so the new clip's TimeRange.End matches the file's
            // natural length at the current BPM. Audio plays at native rate regardless of
            // later BPM changes.
            var durationSecs = AudioMixerManager.TryProbeAudioDurationSecs(asset.FullPath);
            var durationBars = durationSecs > 0
                                   ? (float)playback.BarsFromSeconds(durationSecs)
                                   : 4f;

            var newClip = new TimelineAudioClip
                              {
                                  AssetPath = asset.Address,
                                  TimeRange = new TimeRange(dropTimeBars, dropTimeBars + durationBars),
                                  LayerIndex = dropLayerIndex,
                                  IsMainSoundtrack = false,
                              };

            commands.Add(new AddTimelineAudioClipCommand(compositionOp, newClip));
            // Stack subsequent drops on new layers so multi-drop doesn't pile clips on top of each other.
            dropLayerIndex++;
        }

        if (commands.Count == 0)
            return;

        if (commands.Count == 1)
            UndoRedoStack.AddAndExecute(commands[0]);
        else
            UndoRedoStack.AddAndExecute(new MacroCommand("Drop audio clips", commands));
    }

    private readonly ClipArea.LayerContext _context;
}
