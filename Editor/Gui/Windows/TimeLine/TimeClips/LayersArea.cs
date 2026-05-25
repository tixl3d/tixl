#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Shows a list of Layers with <see cref="TimeClip"/>s
/// </summary>
internal sealed class LayersArea : ITimeObjectManipulation, IValueSnapAttractor
{
    public LayersArea(TimeLineCanvas timeLineCanvas, Func<Instance> getCompositionOp, Func<Guid, bool> requestChildCompositionFunc,
                      ValueSnapHandler snapHandlerForU)
    {
        _getCompositionOp = getCompositionOp;
        _context = new LayerContext(new ClipSelection(timeLineCanvas.NodeSelection),
                                               requestChildCompositionFunc,
                                               timeLineCanvas,
                                               snapHandlerForU);
    }

    /// <summary>
    /// Attributes that are required for drawing elements like <see cref="TimeClipItem"/>s.
    /// </summary>
    internal sealed record LayerContext(
        ClipSelection ClipSelection,
        Func<Guid, bool> RequestChildComposition,
        TimeLineCanvas TimeCanvas,
        ValueSnapHandler SnapHandler);

    private readonly LayerContext _context;

    public void Draw(Instance compositionOp, Playback playback, ValueSnapHandler snapHandler)
    {
        _drawList = ImGui.GetWindowDrawList();
        _playback = playback;

        ImGui.BeginGroup();
        {
            _context.ClipSelection.UpdateForComposition(compositionOp);
            _minScreenPos = ImGui.GetCursorScreenPos();

            DrawAllLayers(compositionOp);
            DrawContextMenuItems(compositionOp);
            HandleKeyboardActions(compositionOp);
            ClipTimingEditor.DrawPopUp(_context);
            if (_context.ClipSelection.AllClipIds.Count > 0)
            {
                FormInputs.AddVerticalSpace(1);
            }
        }
        ImGui.EndGroup();

        HandleAudioFileDrop(compositionOp);

        // Drag Layer area height
        if (_context.ClipSelection.AllClipIds.Count > 0)
        {
            ImGui.SetCursorPosY( ImGui.GetCursorPosY() - 2);
            ImGui.Button("##layerHeight", new Vector2(20, 3) * T3Ui.UiScaleFactor);
            if (ImGui.IsItemActivated())
            {
                _layerHeightOnDragStart = UserSettings.Config.LayerHeight;
                _mouseYOnDragStart = ImGui.GetMousePos().Y;
            }
            if (ImGui.IsItemActive())
            {
                var layerCount = (_maxLayerIndex - _minLayerIndex + 1).Clamp(1,999999);
                var mouseDeltaY = ImGui.GetMousePos().Y - _mouseYOnDragStart;
                var heightDelta = mouseDeltaY / layerCount;
                UserSettings.Config.LayerHeight = (_layerHeightOnDragStart + heightDelta).Clamp(4,50);
            }
        }
    }

    private static float _layerHeightOnDragStart;
    private static float _mouseYOnDragStart;

    private void HandleKeyboardActions(Instance compositionOp)
    {
        if (UserActions.SplitSelectedOrHoveredClips.Triggered())
        {
            SplitClipsAtTime(compositionOp);
        }

        if (UserActions.DeleteSelection.Triggered())
        {
            DeleteSelectedClips(compositionOp);
            DeleteSelectedAudioClips(compositionOp);
        }
    }

    private void DeleteSelectedAudioClips(Instance compositionOp)
    {
        if (_selectedAudioClipIds.Count == 0)
            return;

        var allClips = compositionOp.Symbol.CompositionSettings.Playback.AudioClips;
        var toDelete = new List<TimelineAudioClip>();
        foreach (var clip in allClips)
        {
            if (_selectedAudioClipIds.Contains(clip.Id))
                toDelete.Add(clip);
        }

        if (toDelete.Count == 0)
            return;

        UndoRedoStack.AddAndExecute(new DeleteTimelineAudioClipsCommand(compositionOp, toDelete));
        _selectedAudioClipIds.Clear();
    }

    private void HandleAudioFileDrop(Instance compositionOp)
    {
        // The layers-area BeginGroup..EndGroup pair has just closed; the group's bounding
        // rect is the current "last item." TryHandleDropOnItem hangs its drop target on it.
        var result = DragAndDropHandling.TryHandleDropOnItem(
            DragAndDropHandling.DragTypes.ExternalFile,
            out var data);

        if (result != DragAndDropHandling.DragInteractionResult.Dropped || string.IsNullOrEmpty(data))
            return;

        // Filter for audio extensions. The drop payload may carry multiple files separated
        // by "|" (the graph drop handler splits on it); we import each in turn.
        var package = compositionOp.Symbol.SymbolPackage;
        if (package == null)
        {
            Log.Warning("Cannot resolve composition's resource package for audio drop.");
            return;
        }

        var playback = _playback;
        if (playback == null)
            return;

        var mousePos = ImGui.GetMousePos();
        var dropTimeBars = (float)_context.TimeCanvas.InverseTransformX(mousePos.X);

        // Derive target layer index from drop Y. If no clips exist yet, default to layer 0.
        int dropLayerIndex;
        if (_minLayerIndex == int.MaxValue)
        {
            dropLayerIndex = 0;
        }
        else
        {
            var layerOffset = (mousePos.Y - _minScreenPos.Y - LayerHeight * 0.5f) / LayerHeight;
            dropLayerIndex = _minLayerIndex + (int)Math.Round(layerOffset);
        }

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

            // Probe the source duration so the new clip's TimeRange.End matches the
            // file's natural length (option β from the timeline-clips design — clips
            // visually default to native duration at the current BPM; engine plays at
            // native rate regardless of later BPM changes).
            var durationSecs = AudioMixerManager.TryProbeAudioDurationSecs(asset.FullPath);
            var durationBars = durationSecs > 0
                                   ? (float)playback.BarsFromSeconds(durationSecs)
                                   : 4f; // sensible fallback if BASS could not read the file

            var newClip = new TimelineAudioClip
                              {
                                  AssetPath = asset.Address,
                                  TimeRange = new TimeRange(dropTimeBars, dropTimeBars + durationBars),
                                  LayerIndex = dropLayerIndex,
                                  IsMainSoundtrack = false,
                              };

            commands.Add(new AddTimelineAudioClipCommand(compositionOp, newClip));
            // Stack subsequent drops by 1 layer so they don't overlap when multi-dropping.
            dropLayerIndex++;
        }

        if (commands.Count == 0)
            return;

        if (commands.Count == 1)
        {
            UndoRedoStack.AddAndExecute(commands[0]);
        }
        else
        {
            UndoRedoStack.AddAndExecute(new MacroCommand("Drop audio clips", commands));
        }
    }

    private void DrawAllLayers(Instance compositionOp)
    {
        var clips = _context.ClipSelection.CompositionTimeClips.Values;

        // Non-op audio clips that live in the symbol's CompositionSettings.Playback.AudioClips.
        // We exclude the main soundtrack — it still renders as the timeline background image
        // via TimeLineImage. Secondary clips are drawn here as TimeClip-shaped items.
        var compositionSettings = compositionOp.Symbol.CompositionSettings;
        var audioClips = compositionSettings.Playback.AudioClips;

        // Count visible audio clips up-front so we can decide whether the layer area is empty.
        var visibleAudioClipCount = 0;
        foreach (var ac in audioClips)
        {
            if (ac.IsMainSoundtrack || string.IsNullOrEmpty(ac.AssetPath))
                continue;
            visibleAudioClipCount++;
        }

        if (clips.Count == 0 && visibleAudioClipCount == 0)
        {
            LastHeight = 0;
            return;
        }

        // Adjust height to bounds — includes both op-backed and audio clips so layer rows extend correctly.
        {
            _minLayerIndex = int.MaxValue;
            _maxLayerIndex = int.MinValue;
            foreach (var clip in clips)
            {
                _minLayerIndex = Math.Min(clip.LayerIndex, _minLayerIndex);
                _maxLayerIndex = Math.Max(clip.LayerIndex, _maxLayerIndex);
            }
            foreach (var ac in audioClips)
            {
                if (ac.IsMainSoundtrack || string.IsNullOrEmpty(ac.AssetPath))
                    continue;
                _minLayerIndex = Math.Min(ac.LayerIndex, _minLayerIndex);
                _maxLayerIndex = Math.Max(ac.LayerIndex, _maxLayerIndex);
            }
        }

        // Draw layer lines
        var min = ImGui.GetCursorScreenPos() + new Vector2(0, LayerHeight * 0.5f);
        var max = min + new Vector2(ImGui.GetContentRegionAvail().X,
                                    LayerHeight * (_maxLayerIndex - _minLayerIndex + 1) + 1);
        LastHeight = max.Y - min.Y + 5;
        _drawList.AddRectFilled(new Vector2(min.X, max.Y - 2),
                                new Vector2(max.X, max.Y - 0), UiColors.GridLines.Fade(0.6f));

        var compositionSymbolUi = compositionOp.GetSymbolUi();
        var drawAttributes = new TimeClipItem.ClipDrawingAttributes(
                                                                    _context,
                                                                    new ImRect(min, max),
                                                                    _minLayerIndex,
                                                                    compositionOp,
                                                                    compositionSymbolUi,
                                                                    _moveClipsCommand,
                                                                    _drawList
                                                                   );

        // Cleanup on changes
        if (compositionSymbolUi.VersionCounter != _lastOpVersion)
        {
            _lastOpVersion = compositionSymbolUi.VersionCounter;
            
            // Avoid overlaps of selected clips (probably newly created or duplicated) first
            foreach(var clip in clips)
            {
                if (!_context.ClipSelection.SelectedClipsIds.Contains(clip.Id))
                    continue;
                
                while (clip.IsClipOverlappingOthers(clips))
                {
                    clip.LayerIndex--;
                }
            }
            
            // All clips in all layers
            foreach (var clip in clips)
            {
                if (clip.MakeConform())
                {
                    Log.Debug($"Corrected malformed timing for {clip.Id}");
                }
            }
        }
        
        // All clips in all layers
        foreach (var clip in clips)
        {
            TimeClipItem.DrawClip(clip, ref drawAttributes);
        }

        // Symbol-level audio clips. Read-only render + click-to-select for Phase D;
        // editing (drag, trim, delete, snap) lands in Phase E.
        if (visibleAudioClipCount > 0)
        {
            var audioAttrs = new TimelineAudioClipItem.DrawAttrs(
                new ImRect(min, max),
                _minLayerIndex,
                _drawList,
                _selectedAudioClipIds,
                _context.TimeCanvas,
                compositionOp);

            foreach (var ac in audioClips)
            {
                if (ac.IsMainSoundtrack || string.IsNullOrEmpty(ac.AssetPath))
                    continue;
                TimelineAudioClipItem.DrawClip(ac, ref audioAttrs);
            }
        }

        ImGui.SetCursorScreenPos(min + new Vector2(0, LayerHeight));
    }

    // Parallel selection store for audio clips. ClipSelection (op-backed) is keyed by
    // TimeClip references — won't accept TimelineAudioClip instances. Kept separate from
    // ClipSelection for now; the asymmetries between op-backed and audio clips are easier
    // to spot one operation at a time. A future tidy may merge them.
    private readonly HashSet<Guid> _selectedAudioClipIds = new();

    /// <summary>
    /// Cross-frame drag state for moving / trimming <see cref="TimelineAudioClip"/>s.
    /// Set by <c>TimelineAudioClipItem</c> on drag start, pushed to the undo stack on
    /// drag completion. Static because there is at most one active drag in flight.
    /// </summary>
    internal static MoveTimelineAudioClipsCommand? ActiveAudioMoveCommand;

    
    private void AvoidOverlaps()
    {

    }
    
    private bool _contextMenuIsOpen;
    private int _lastOpVersion = -1;

    private void DrawContextMenuItems(Instance compositionOp)
    {
        Debug.Assert(_playback != null);

        if (!_contextMenuIsOpen && !ImGui.IsWindowHovered())
            return;

        if (_context.ClipSelection.Count == 0)
            return;

        if (!_contextMenuIsOpen && !UiHelpers.UiHelpers.WasRightMouseClick())
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
        if (ImGui.BeginPopupContextWindow("windows_context_menu"))
        {
            _contextMenuIsOpen = true;
            if (ImGui.MenuItem("Delete", UserActions.DeleteSelection.ListShortcuts(), false, _context.ClipSelection.Count > 0))
            {
                DeleteSelectedClips(compositionOp);
            }

            if (ImGui.MenuItem("Clear Time Stretch", null, false, _context.ClipSelection.Count > 0))
            {
                var selectedClips = _context.ClipSelection.GetAllOrSelectedClips().ToList();
                var moveTimeClipCommand = new MoveTimeClipsCommand(compositionOp, selectedClips);
                foreach (var clip in selectedClips)
                {
                    clip.SourceRange = clip.TimeRange.Clone();
                }

                moveTimeClipCommand.StoreCurrentValues();
                UndoRedoStack.AddAndExecute(moveTimeClipCommand);
                _context.ClipSelection.Clear();
            }
            
            if (ImGui.MenuItem("Edit clip times", null, false, _context.ClipSelection.Count > 0))
            {
                ClipTimingEditor.TimeClipEditorRequested = true;
            }

            if (ImGui.MenuItem("Cut at time", UserActions.SplitSelectedOrHoveredClips.ListShortcuts()))
            {
                SplitClipsAtTime(compositionOp);
            }

            ImGui.Separator();

            ImGui.EndPopup();
        }
        else
        {
            _contextMenuIsOpen = false;
        }
        ImGui.PopStyleVar(2);
    }

    private void DeleteSelectedClips(Instance compositionOp)
    {
        var compositionSymbolUi = compositionOp.GetSymbolUi();
        List<SymbolUi.Child> selectedChildren = [];
        foreach (var id in _context.ClipSelection.SelectedClipsIds)
        {
            if (!compositionSymbolUi.ChildUis.TryGetValue(id, out var child))
                continue;
                    
            selectedChildren.Add(child);                    
        }

        if (selectedChildren.Count == 0)
            return;

        UndoRedoStack.AddAndExecute(new DeleteSymbolChildrenCommand(compositionSymbolUi, selectedChildren));
        //_context.ClipSelection.Clear();
        _context.TimeCanvas.NodeSelection.Clear();
        compositionSymbolUi.FlagAsModified();
        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children);
    }

    private void SplitClipsAtTime(Instance compositionOp)
    {
        Debug.Assert(_playback != null);

        var timeInBars = _playback.TimeInBars;
        var newClips = new List<TimeClip>();

        var commands = new List<ICommand>();
        foreach (var clip in _context.ClipSelection.GetAllOrSelectedClips())
        {
            if (!clip.TimeRange.Contains(timeInBars))
                return;

            if (timeInBars - clip.TimeRange.Start < 0.01 
                ||
                clip.TimeRange.End - timeInBars < 0.01)
            {
                Log.Debug("This clip would be too small...");
                continue;
            }

            
            var compositionSymbolUi = compositionOp.GetSymbolUi();

            var symbolChildUi = compositionSymbolUi.ChildUis[clip.Id];

            var originalName = symbolChildUi.SymbolChild.ReadableName;
            var newPos = symbolChildUi.PosOnCanvas;
            newPos.Y += MagGraphItem.GridSize.Y;
            // Pass an empty annotation list (not null) so the command does not fall back
            // to cloning every annotation in the composition.
            var cmd = new CopySymbolChildrenCommand(compositionSymbolUi,
                                                    [symbolChildUi],
                                                    [],
                                                    compositionSymbolUi,
                                                    newPos);
            commands.Add(cmd);
            cmd.Do();

            // Set new end to the original time clip
            var orgTimeRangeEnd = clip.TimeRange.End;
            var originalSourceDuration = clip.SourceRange.Duration;
            var normalizedCutPosition = ((float)_playback.TimeInBars - clip.TimeRange.Start) / clip.TimeRange.Duration;

            // Apply new time range to newly added instance
            var newChildId = cmd.OldToNewChildIds[clip.Id];
            var newInstance = compositionOp.Children[newChildId];
            var newTimeClip = newInstance.Outputs.OfType<ITimeClipProvider>().Single().TimeClip;

            var newSymbolUiChild = compositionSymbolUi.ChildUis[newChildId];
            var newName = originalName.AppendOrIncrementVersionNumber();
            var renameCommand = new ChangeSymbolChildNameCommand(newSymbolUiChild, compositionSymbolUi.Symbol)
                                    {
                                        NewName = newName
                                    };
            renameCommand.Do();
            commands.Add(renameCommand);

            // Capture the new clip's just-copied TimeRange/SourceRange as the undo state, then mutate to
            // the "second half" ranges and store those as the redo state.
            var adjustNewClipCommand = new MoveTimeClipsCommand(compositionOp, [newTimeClip]);
            newTimeClip.TimeRange = new TimeRange((float)_playback.TimeInBars, orgTimeRangeEnd);
            newTimeClip.SourceRange.Start = newTimeClip.SourceRange.Start + originalSourceDuration * normalizedCutPosition;
            newTimeClip.SourceRange.End = clip.SourceRange.End;
            adjustNewClipCommand.StoreCurrentValues();
            commands.Add(adjustNewClipCommand);
            newClips.Add(newTimeClip);

            // Adjust first clip end time
            var adjustFirstClipCommand = new MoveTimeClipsCommand(compositionOp, [clip]);
            clip.TimeRange.End = (float)_playback.TimeInBars;
            clip.SourceRange.Duration = originalSourceDuration * normalizedCutPosition;
            adjustFirstClipCommand.StoreCurrentValues();
            
            commands.Add(adjustFirstClipCommand);
            
            
            // Copy connection of original clip
            {
                Symbol.Child.Output? timeClipOutput = null;
                foreach (var o in symbolChildUi.SymbolChild.Outputs.Values)
                {
                    if (o.OutputData is TimeClip tc && tc == clip)
                    {
                        timeClipOutput = o;
                        break;
                    }
                }

                if (timeClipOutput == null)
                {
                    Log.Warning($"Can't find timeclip output for {symbolChildUi}?");
                    continue;
                }
                
                // find connections
                var connections = compositionOp.Symbol.Connections
                                               .Where(c => c.SourceParentOrChildId == symbolChildUi.Id
                                                      && c.SourceSlotId == timeClipOutput.OutputDefinition.Id)
                                               .ToList();

                foreach (var c in connections)
                {
                    if (!compositionOp.Symbol.Children.TryGetValue(c.TargetParentOrChildId, out var targetOp))
                        continue;

                    if (!targetOp.Inputs.TryGetValue(c.TargetSlotId, out var targetInput) || !targetInput.IsMultiInput)
                        continue;

                    var addConnectionCommand = new AddConnectionCommand(compositionOp.Symbol, 
                                                                        new Symbol.Connection(newInstance.SymbolChildId,
                                                                                              c.SourceSlotId,
                                                                                              c.TargetParentOrChildId,
                                                                                              c.TargetSlotId), 
                                                                        compositionOp.Symbol.GetMultiInputIndexFor(c)+1);
                    addConnectionCommand.Do();
                    commands.Add(addConnectionCommand);
                }
            }
        }

        if (commands.Count == 0)
        {
            Log.Debug($"There are no time clips to split at current time {timeInBars:0.0}");
            return;
        }

        var macroCommands = new MacroCommand("split clip", commands);
        UndoRedoStack.Add(macroCommands);

        _context.ClipSelection.Clear();
        foreach (var t in newClips)
        {
            _context.ClipSelection.Select(t);
        }
        
        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children|ProjectView.ChangeTypes.Connections );
    }

    private int _minLayerIndex = int.MaxValue;
    private int _maxLayerIndex = int.MinValue;

    public float LastHeight;

    #region implement TimeObject interface --------------------------------------------
    public void ClearSelection()
    {
        _context.ClipSelection.Clear();
    }

    public void UpdateSelectionForArea(ImRect screenArea, SelectionFence.SelectModes selectMode)
    {
        var compositionOp = _getCompositionOp();

        if (selectMode == SelectionFence.SelectModes.Replace)
            _context.ClipSelection.Clear();

        var startTime = _context.TimeCanvas.InverseTransformX(screenArea.Min.X);
        var endTime = _context.TimeCanvas.InverseTransformX(screenArea.Max.X);

        var layerMinIndex = (screenArea.Min.Y - _minScreenPos.Y - LayerHeight * 0.5f) / LayerHeight + _minLayerIndex;
        var layerMaxIndex = (screenArea.Max.Y - _minScreenPos.Y - LayerHeight * 0.5f) / LayerHeight + _minLayerIndex;

        foreach (var clip in Structure.GetAllTimeClips(compositionOp))
        {
            var matches = clip.TimeRange.Start <= endTime
                          && clip.TimeRange.End >= startTime
                          && clip.LayerIndex <= layerMaxIndex
                          && clip.LayerIndex >= layerMinIndex - 1;

            if (!matches)
                continue;

            switch (selectMode)
            {
                case SelectionFence.SelectModes.Add:
                case SelectionFence.SelectModes.Replace:
                    _context.ClipSelection.AddSelection(clip);
                    break;

                case SelectionFence.SelectModes.Remove:
                    _context.ClipSelection.Deselect(clip);
                    break;
            }
        }
    }

    public ICommand StartDragCommand(in Guid compositionSymbolId)
    {
        var composition = _getCompositionOp();
        var selection = _context.ClipSelection.SelectedClipsIds.Count > 0
                            ? _context.ClipSelection.GetSelectedClips().ToList()
                            : [];
        
        _moveClipsCommand = new MoveTimeClipsCommand(composition, selection);
        _layerIndexOnDragStart = 0;
        return _moveClipsCommand;
    }

    void ITimeObjectManipulation.UpdateDragCommand(double dt, double dy)
    {
        var io = ImGui.GetIO();
        var toggleLinkMode = io.KeyAlt;
        var dragInside = io.KeyCtrl && io.KeyAlt;
        var lockTime = io.KeyCtrl && !io.KeyAlt;

        if (_context.ClipSelection.SelectedClipsIds.Count == 0)
            return;
        
        var indexDelta = _layerIndexOnDragStart - (int)(dy / LayerHeight);

        if (indexDelta != 0)
        {
            _layerIndexOnDragStart -= indexDelta;
        }

        foreach (var clipId in _context.ClipSelection.SelectedClipsIds)
        {
            var clip = _context.ClipSelection.CompositionTimeClips[clipId];
            clip.LayerIndex += indexDelta;
                
            if (lockTime)
                continue;
            
            if (dragInside)
            {
                clip.SourceRange.Start += (float)dt;
                clip.SourceRange.End += (float)dt;
            }
            else if (clip.UsedForRegionMapping^toggleLinkMode)
            {
                clip.TimeRange.Start += (float)dt;
                clip.TimeRange.End += (float)dt;
            }
            else
            {
                clip.TimeRange.Start += (float)dt;
                clip.TimeRange.End += (float)dt;
                clip.SourceRange.Start += (float)dt;
                clip.SourceRange.End += (float)dt;
            }
        }
    }

    public void UpdateDragAtStartPointCommand(double dt, double dv)
    {
        var trim = !ImGui.GetIO().KeyAlt;
        foreach (var clipId in _context.ClipSelection.SelectedClipsIds)
        {
            var clip = _context.ClipSelection.CompositionTimeClips[clipId];
            // Keep 1 frame min duration
            var org = clip.TimeRange.Start;
            clip.TimeRange.Start = (float)Math.Min(clip.TimeRange.Start + dt, clip.TimeRange.End - MinDuration);
            var d = clip.TimeRange.Start - org;
            if (trim)
                clip.SourceRange.Start += d;
        }
    }

    public void UpdateDragAtEndPointCommand(double dt, double dv)
    {
        var trim = !ImGui.GetIO().KeyAlt;
        foreach (var clipId in _context.ClipSelection.SelectedClipsIds)
        {
            var clip = _context.ClipSelection.CompositionTimeClips[clipId];
            // Keep 1 frame min duration
            var org = clip.TimeRange.End;
            clip.TimeRange.End = (float)Math.Max(clip.TimeRange.End + dt, clip.TimeRange.Start + MinDuration);
            var d = clip.TimeRange.End - org;
            if (trim)
                clip.SourceRange.End += d;
        }
    }

    void ITimeObjectManipulation.UpdateDragStretchCommand(double scaleU, double scaleV, double originU, double originV)
    {
        foreach (var clipId in _context.ClipSelection.SelectedClipsIds)
        {
            var clip = _context.ClipSelection.CompositionTimeClips[clipId];
            clip.TimeRange.Start = (float)(originU + (clip.TimeRange.Start - originU) * scaleU);
            clip.TimeRange.End = (float)Math.Max(originU + (clip.TimeRange.End - originU) * scaleU, clip.TimeRange.Start + MinDuration);
        }
    }

    private const float MinDuration = 1 / 60f; // In bars

    public TimeRange GetSelectionTimeRange()
    {
        var timeRange = TimeRange.Undefined;
        foreach (var id in _context.ClipSelection.SelectedClipsIds)
        {
            // Selection can transiently reference clips no longer in the composition
            // (e.g. immediately after a split, before ClipSelection re-syncs).
            if (!_context.ClipSelection.CompositionTimeClips.TryGetValue(id, out var s))
                continue;

            // fix broken time ranges
            // FIXME: make sure these don't happen at all
            if (s.TimeRange.Duration <= 0
                || float.IsNaN(s.TimeRange.Start)
                || float.IsNaN(s.TimeRange.End))
            {
                s.TimeRange.Start = 0;
                s.TimeRange.End = s.TimeRange.Start + 1;
            }

            timeRange.Unite(s.TimeRange.Start);
            timeRange.Unite(s.TimeRange.End);
        }

        return timeRange;
    }

    /// <summary>U-range spanning every TimeClip of the current composition.</summary>
    public TimeRange GetAllClipsTimeRange()
    {
        var timeRange = TimeRange.Undefined;
        foreach (var clip in _context.ClipSelection.CompositionTimeClips.Values)
        {
            timeRange.Unite(clip.TimeRange.Start);
            timeRange.Unite(clip.TimeRange.End);
        }
        return timeRange;
    }

    public bool HasSelectedClips => _context.ClipSelection.Count > 0;
    public bool HasAnyClips => _context.ClipSelection.CompositionTimeClips.Count > 0;

    /// <summary>Enumerate currently selected TimeClips.</summary>
    public IEnumerable<TimeClip> EnumerateSelectedClips() => _context.ClipSelection.GetSelectedClips();

    /// <summary>Select every TimeClip of the current composition.</summary>
    public void SelectAllClips()
    {
        foreach (var clip in _context.ClipSelection.CompositionTimeClips.Values)
        {
            _context.ClipSelection.AddSelection(clip);
        }
    }

    void ITimeObjectManipulation.CompleteDragCommand()
    {
        if (_moveClipsCommand == null)
            return;

        // Update reference in macro-command 
        _moveClipsCommand.StoreCurrentValues();
        _moveClipsCommand = null;
    }

    void ITimeObjectManipulation.DeleteSelectedElements(Instance instance)
    {
        //TODO: Implement deleting of layers with delete key
    }
    #endregion

    #region implement snapping interface -----------------------------------
    /// <summary>
    /// Snap to all non-selected Clips
    /// </summary>
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        var currentComp = _getCompositionOp();
        var allClips = Structure.GetAllTimeClips(currentComp);

        foreach (var clip in allClips)
        {
            if (_context.ClipSelection.Contains(clip))
                continue;

            snapResult.TryToImproveWithAnchorValue(clip.TimeRange.Start);
            snapResult.TryToImproveWithAnchorValue(clip.TimeRange.End);
        }
    }
    #endregion

    private Vector2 _minScreenPos;

    private static MoveTimeClipsCommand? _moveClipsCommand;
    internal static float LayerHeight => UserSettings.Config.LayerHeight.Clamp(4,40);

    private ImDrawListPtr _drawList;

    private Playback? _playback;
    private readonly Func<Instance> _getCompositionOp;
    private int _layerIndexOnDragStart;

    public bool TryGetBounds(out ImRect bounds, bool useAllIfNonSelected= true)
    {
        var isFirst = true;

        bounds = new ImRect();

        var range = useAllIfNonSelected 
                        ?_context.ClipSelection.GetAllOrSelectedClips() 
                        : _context.ClipSelection.GetSelectedClips();
        foreach (var c in range)
        {
            var clipBound = new ImRect(new Vector2(c.TimeRange.Start,c.LayerIndex * LayerHeight),
                                       new Vector2(c.TimeRange.End,(c.LayerIndex + 1) * LayerHeight));
            if (isFirst)
            {
                bounds = clipBound;
                isFirst = false;
            }
            else
            {
                bounds.Add(clipBound);
            }
        }

        return !isFirst;
    }
}