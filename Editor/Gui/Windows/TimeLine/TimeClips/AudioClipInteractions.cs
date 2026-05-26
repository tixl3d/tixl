#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

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
    public AudioClipInteractions(ClipArea.LayerContext context, Func<Instance> getCompositionOp)
    {
        _context = context;
        _getCompositionOp = getCompositionOp;
    }

    /// <summary>
    /// Back-reference to the op-clip side, set by <see cref="ClipArea"/> at construction.
    /// Used by cross-type drag (when an audio clip is dragged, op clips in the op
    /// selection move along) and by other cross-type operations.
    /// </summary>
    internal TimeClipInteractions? OpInteractions;

    /// <summary>
    /// Cross-frame drag state for moving / trimming <see cref="TimelineAudioClip"/>s.
    /// Set by <see cref="TimelineAudioClipItem"/> on drag start, pushed to the undo stack
    /// on drag completion. Static because there is at most one active drag in flight.
    /// </summary>
    internal static MoveTimelineAudioClipsCommand? ActiveMoveCommand;

    /// <summary>
    /// Accumulator for vertical layer-shift during a body drag. Quantises pixel-level
    /// mouse Y movement into integer <see cref="TimelineAudioClip.LayerIndex"/> changes
    /// so the clip snaps cleanly between rows. Reset to 0 on drag start.
    /// </summary>
    internal static int LayerShiftOnDragStart;

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
            layerRect, minLayerIndex, drawList, SelectedClipIds, _context.TimeCanvas, compositionOp,
            OpInteractions ?? throw new InvalidOperationException("OpInteractions not set"),
            _context.SnapHandler);

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

    // ---------------------------------------------------------------------------
    // Cross-type drag — used when op-clip body drag should also move audio clips
    // in the audio selection (and vice versa via TimelineAudioClipItem calling into
    // TimeClipInteractions).
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds the audio-side <see cref="MoveTimelineAudioClipsCommand"/> for the current
    /// selection so audio clips will follow an op-clip body drag. No-op if no audio
    /// clips are selected.
    /// </summary>
    public void StartCrossDrag()
    {
        if (SelectedClipIds.Count == 0)
            return;

        var compositionOp = _getCompositionOp();
        var allClips = compositionOp.Symbol.CompositionSettings.Playback.AudioClips;
        var selected = new List<TimelineAudioClip>();
        foreach (var c in allClips)
        {
            if (SelectedClipIds.Contains(c.Id))
                selected.Add(c);
        }
        if (selected.Count > 0)
        {
            ActiveMoveCommand = new MoveTimelineAudioClipsCommand(compositionOp, selected);
            LayerShiftOnDragStart = 0;
        }
    }

    /// <summary>
    /// Applies a (dt-in-bars, layerIndexDelta) drag delta to every audio clip in the
    /// current selection. Called either by the audio-clip-driven drag loop or by
    /// <see cref="TimeClipInteractions"/> propagating a cross-type drag.
    /// </summary>
    public void ApplyDragDelta(double dtBars, int layerIndexDelta)
    {
        if (SelectedClipIds.Count == 0)
            return;

        var allClips = _getCompositionOp().Symbol.CompositionSettings.Playback.AudioClips;
        foreach (var c in allClips)
        {
            if (!SelectedClipIds.Contains(c.Id))
                continue;
            c.TimeRange.Start += (float)dtBars;
            c.TimeRange.End += (float)dtBars;
            c.LayerIndex += layerIndexDelta;
        }
    }

    /// <summary>
    /// Pushes the audio-side move command from a cross-type drag onto the undo stack.
    /// </summary>
    public void CompleteCrossDrag()
    {
        if (ActiveMoveCommand == null)
            return;
        ActiveMoveCommand.StoreCurrentValues();
        UndoRedoStack.Add(ActiveMoveCommand);
        ActiveMoveCommand = null;
    }

    // ---------------------------------------------------------------------------
    // Snap + selection-rect — audio clips contribute to the same operations as op
    // clips so the user sees a unified clip area.
    // ---------------------------------------------------------------------------

    public void CheckForSnap(ref SnapResult snapResult)
    {
        var compositionOp = _getCompositionOp();
        var audioClips = compositionOp.Symbol.CompositionSettings.Playback.AudioClips;
        foreach (var clip in audioClips)
        {
            if (clip.IsMainSoundtrack || string.IsNullOrEmpty(clip.AssetPath))
                continue;
            if (SelectedClipIds.Contains(clip.Id))
                continue;
            snapResult.TryToImproveWithAnchorValue(clip.TimeRange.Start);
            snapResult.TryToImproveWithAnchorValue(clip.TimeRange.End);
        }
    }

    public void UpdateSelectionForArea(ImRect screenArea,
                                       SelectionFence.SelectModes selectMode,
                                       Vector2 minScreenPos,
                                       int minLayerIndex)
    {
        if (selectMode == SelectionFence.SelectModes.Replace)
            SelectedClipIds.Clear();

        var startTime = _context.TimeCanvas.InverseTransformX(screenArea.Min.X);
        var endTime = _context.TimeCanvas.InverseTransformX(screenArea.Max.X);

        var layerMinIndex = (screenArea.Min.Y - minScreenPos.Y - ClipArea.LayerHeight * 0.5f) / ClipArea.LayerHeight + minLayerIndex;
        var layerMaxIndex = (screenArea.Max.Y - minScreenPos.Y - ClipArea.LayerHeight * 0.5f) / ClipArea.LayerHeight + minLayerIndex;

        var compositionOp = _getCompositionOp();
        var audioClips = compositionOp.Symbol.CompositionSettings.Playback.AudioClips;
        foreach (var clip in audioClips)
        {
            if (clip.IsMainSoundtrack || string.IsNullOrEmpty(clip.AssetPath))
                continue;

            // Effective right edge accounts for the sentinel-End case (length-derived).
            var hasExplicitEnd = clip.TimeRange.End > clip.TimeRange.Start;
            var clipEnd = hasExplicitEnd
                              ? clip.TimeRange.End
                              : clip.TimeRange.Start + (float)_context.TimeCanvas.Playback.BarsFromSeconds(clip.LengthInSeconds);

            var matches = clip.TimeRange.Start <= endTime
                          && clipEnd >= startTime
                          && clip.LayerIndex <= layerMaxIndex
                          && clip.LayerIndex >= layerMinIndex - 1;

            if (!matches)
                continue;

            switch (selectMode)
            {
                case SelectionFence.SelectModes.Add:
                case SelectionFence.SelectModes.Replace:
                    SelectedClipIds.Add(clip.Id);
                    break;
                case SelectionFence.SelectModes.Remove:
                    SelectedClipIds.Remove(clip.Id);
                    break;
            }
        }
    }

    private readonly ClipArea.LayerContext _context;
    private readonly Func<Instance> _getCompositionOp;
}
