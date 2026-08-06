#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// All interaction concerns for op-backed <see cref="TimeClip"/>s in the timeline-clip
/// area: selection, drag command lifecycle, delete-with-children, split-at-time, snap.
///
/// Per-clip rendering + handle drag lives in <see cref="TimeClipItem"/>; this class owns
/// the cross-frame drag command state and the operations that span multiple clips
/// (delete / split / select-all / get-bounds).
/// </summary>
internal sealed class TimeClipInteractions
{
    public TimeClipInteractions(ClipArea.LayerContext context, Func<Instance> getCompositionOp)
    {
        _context = context;
        _getCompositionOp = getCompositionOp;
    }

    public void SetPlayback(Playback? playback) => _playback = playback;

    public void DrawClips(Instance compositionOp, ImRect layerRect, int minLayerIndex, ImDrawListPtr drawList)
    {
        var clips = _context.ClipSelection.CompositionTimeClips.Values;
        if (clips.Count == 0)
            return;

        var compositionSymbolUi = compositionOp.GetSymbolUi();
        var drawAttributes = new TimeClipItem.ClipDrawingAttributes(
            _context, layerRect, minLayerIndex, compositionOp, compositionSymbolUi,
            _moveClipsCommand, drawList);

        // Cleanup on changes
        if (compositionSymbolUi.VersionCounter != _lastOpVersion)
        {
            _lastOpVersion = compositionSymbolUi.VersionCounter;

            // Avoid overlaps of selected clips (probably newly created or duplicated) first
            foreach (var clip in clips)
            {
                if (!_context.ClipSelection.SelectedClipsIds.Contains(clip.Id))
                    continue;

                while (clip.IsClipOverlappingOthers(clips))
                {
                    clip.LayerIndex--;
                }
            }

            foreach (var clip in clips)
            {
                if (clip.MakeConform())
                    Log.Debug($"Corrected malformed timing for {clip.Id}");
            }
        }

        foreach (var clip in clips)
            TimeClipItem.DrawClip(clip, ref drawAttributes);
    }

    public void DrawContextMenuItems(Instance compositionOp)
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
                // Reset stretch only — keep the user's source-side trim. The source slice
                // continues to start at its existing SourceRange.Start (so the event sitting
                // at the trimmed-in edge stays put), and the End is pulled to match the
                // timeline duration so the rate becomes 1. Pinning SourceRange.Start to 0
                // here would silently undo the trim and snap content the user had pushed
                // off-screen back into view.
                foreach (var clip in selectedClips)
                    clip.SourceRange.End = clip.SourceRange.Start + clip.TimeRange.Duration;

                moveTimeClipCommand.StoreCurrentValues();
                UndoRedoStack.AddAndExecute(moveTimeClipCommand);
                // Keep the selection — the user is mid-edit on these clips and the next
                // action (Edit clip times, Cut, drag) is almost always still on them.
            }

            if (ImGui.MenuItem("Edit clip times", null, false, _context.ClipSelection.Count > 0))
            {
                ClipTimingEditor.TimeClipEditorRequested = true;
            }

            if (ImGui.MenuItem("Cut at time", UserActions.SplitSelectedOrHoveredClips.ListShortcuts()))
            {
                SplitClipsAtTime(compositionOp);
            }

            if (ImGui.MenuItem("Select following clips", UserActions.SelectFollowingClips.ListShortcuts())
                && _playback != null)
            {
                // The playhead anchor is exact (e.g. right after a cut) — a frame of tolerance suffices.
                SelectClipsStartingAfter(_playback.TimeInBars, _playback.BarsFromSeconds(1 / 30.0));
            }

            DrawMainSoundtrackMenuItem(compositionOp);

            // Only offered when the selection includes a DataClip op - the inline pane has nothing to show otherwise.
            if (InlineDataClipArea.HasSelectedDataClipInstance(_context.TimeCanvas, compositionOp))
            {
                var showClipData = _context.TimeCanvas.InlineDataClipEditEnabled;
                if (ImGui.MenuItem("Show Clip Data", null, showClipData))
                {
                    _context.TimeCanvas.InlineDataClipEditEnabled = !showClipData;
                }
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

    /// <summary>
    /// "Set as main soundtrack" for a single selected [AudioClip]: sets its Display to BackgroundImage
    /// (timeline background, FFT routing, export duration) and clears the designation from any other
    /// audio clip — one main soundtrack per composition. Toggles off when the clip already is it.
    /// </summary>
    private void DrawMainSoundtrackMenuItem(Instance compositionOp)
    {
        if (_context.ClipSelection.Count != 1)
            return;

        Guid selectedId = default;
        foreach (var id in _context.ClipSelection.SelectedClipsIds)
        {
            selectedId = id;
            break;
        }

        if (!compositionOp.Children.TryGetChildInstance(selectedId, out var instance)
            || instance is not IAudioClipProvider provider)
            return;

        var isMain = provider.GetResourceHandle().Clip.IsMainSoundtrack;
        if (ImGui.MenuItem(isMain ? "Unset main soundtrack" : "Set as main soundtrack"))
        {
            SetMainSoundtrackClip(compositionOp, selectedId, enable: !isMain);
        }
    }

    private static void SetMainSoundtrackClip(Instance compositionOp, Guid clipChildId, bool enable)
    {
        var symbol = compositionOp.Symbol;
        var commands = new List<ICommand>();

        foreach (var (childId, child) in symbol.Children)
        {
            // The Display input identifies [AudioClip] ops; all others are cleared so only one clip
            // carries the designation.
            if (!child.Inputs.TryGetValue(LegacyAudioClipMigration.DisplayInputId, out var displayInput))
                continue;

            var targetValue = childId == clipChildId && enable
                                  ? (int)AudioClipDisplay.BackgroundImage
                                  : (int)AudioClipDisplay.Clip;

            var currentValue = (displayInput.Value as InputValue<int>)?.Value ?? 0;
            if (currentValue == targetValue)
                continue;

            var command = new ChangeInputValueCommand(symbol, childId, displayInput, new InputValue<int>(targetValue));
            command.Do();
            commands.Add(command);
        }

        if (commands.Count == 0)
            return;

        UndoRedoStack.Add(new MacroCommand("Set main soundtrack", commands));
        compositionOp.GetSymbolUi().FlagAsModified();
    }

    /// <summary>
    /// The split's new op is placed directly below the original, where it can land exactly on whatever
    /// sat there — nearly invisible. Push covered ops downward instead, cascading so a pushed op doesn't
    /// just cover the next one. Pushes move in whole MagGraph grid rows, so snapped stacks (e.g. clips
    /// feeding a multi-input) keep their alignment and stay magnetically connected.
    /// </summary>
    private void PushDownOpsOverlappedBy(SymbolUi compositionUi, SymbolUi.Child insertedChildUi, List<ICommand> commands)
    {
        var moved = new List<(SymbolUi.Child ChildUi, Vector2 OriginalPos)>();
        var handled = new HashSet<Guid> { insertedChildUi.Id };
        var movers = new Queue<SymbolUi.Child>();
        movers.Enqueue(insertedChildUi);

        while (movers.Count > 0)
        {
            var mover = movers.Dequeue();
            var moverRect = ImRect.RectWithSize(mover.PosOnCanvas, mover.Size);

            foreach (var other in compositionUi.ChildUis.Values)
            {
                if (handled.Contains(other.Id))
                    continue;

                var otherRect = ImRect.RectWithSize(other.PosOnCanvas, other.Size);
                if (!moverRect.Overlaps(otherRect))
                    continue;

                var gridSteps = Math.Max(1, (int)Math.Ceiling((moverRect.Max.Y - otherRect.Min.Y) / MagGraphItem.GridSize.Y));

                handled.Add(other.Id);
                moved.Add((other, other.PosOnCanvas));
                other.PosOnCanvas = new Vector2(other.PosOnCanvas.X, other.PosOnCanvas.Y + gridSteps * MagGraphItem.GridSize.Y);
                movers.Enqueue(other);
            }
        }

        if (moved.Count == 0)
            return;

        // The command snapshots current positions as its undo baseline — briefly revert to the originals,
        // snapshot, then re-apply the pushed positions and store them as the redo state.
        var selectables = new List<ISelectableCanvasObject>(moved.Count);
        var pushedPositions = new List<Vector2>(moved.Count);
        foreach (var (childUi, originalPos) in moved)
        {
            selectables.Add(childUi);
            pushedPositions.Add(childUi.PosOnCanvas);
            childUi.PosOnCanvas = originalPos;
        }

        var moveCommand = new ModifyCanvasElementsCommand(compositionUi.Symbol.Id, selectables, _context.TimeCanvas.NodeSelection);
        for (var i = 0; i < moved.Count; i++)
            moved[i].ChildUi.PosOnCanvas = pushedPositions[i];

        moveCommand.StoreCurrentValues();
        commands.Add(moveCommand);
    }

    /// <summary>
    /// Selects every clip (on all layers) that starts at or after the given time — for ripple edits:
    /// select everything downstream, then drag to open or close a gap. The tolerance includes clips
    /// starting *at* (or shortly before) the anchor, so right after a cut the new right half is part of
    /// the selection even when the mouse sits a bit past the cut.
    /// </summary>
    public void SelectClipsStartingAfter(double timeInBars, double toleranceInBars)
    {
        var selection = _context.ClipSelection;
        selection.Clear();
        foreach (var clip in selection.CompositionTimeClips.Values)
        {
            if (clip.TimeRange.Start >= timeInBars - toleranceInBars)
                selection.AddSelection(clip);
        }
    }

    public void DeleteSelectedClips(Instance compositionOp)
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
        _context.TimeCanvas.NodeSelection.Clear();
        compositionSymbolUi.FlagAsModified();
        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children);
    }

    public void SplitClipsAtTime(Instance compositionOp)
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
                || clip.TimeRange.End - timeInBars < 0.01)
            {
                Log.Debug("This clip would be too small...");
                continue;
            }

            var compositionSymbolUi = compositionOp.GetSymbolUi();
            var symbolChildUi = compositionSymbolUi.ChildUis[clip.Id];

            var originalName = symbolChildUi.SymbolChild.ReadableName;
            var newPos = symbolChildUi.PosOnCanvas;
            newPos.Y += MagGraphItem.GridSize.Y;
            // Pass an empty section list (not null) so the command does not fall back
            // to cloning every section in the composition.
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

            PushDownOpsOverlappedBy(compositionSymbolUi, newSymbolUiChild, commands);

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

            // Copy the original clip's connections into multi-inputs, so the new half stays wired the
            // same way — regardless of which output carried the connection (an [AudioClip]'s
            // AudioReference into a bus just as much as a TimeClip command into a group).
            {
                var connections = compositionOp.Symbol.Connections
                                               .Where(c => c.SourceParentOrChildId == symbolChildUi.Id)
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
                                                                        compositionOp.Symbol.GetMultiInputIndexFor(c) + 1);
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
            _context.ClipSelection.Select(t);

        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children | ProjectView.ChangeTypes.Connections);
    }

    // ---------------------------------------------------------------------------
    // Drag command lifecycle (ITimeObjectManipulation forwarded from ClipArea)
    // ---------------------------------------------------------------------------

    public ICommand StartDragCommand()
    {
        var composition = _getCompositionOp();
        var selection = _context.ClipSelection.SelectedClipsIds.Count > 0
                            ? _context.ClipSelection.GetSelectedClips().ToList()
                            : [];

        _moveClipsCommand = new MoveTimeClipsCommand(composition, selection);
        _layerIndexOnDragStart = 0;

        return _moveClipsCommand;
    }

    public void UpdateDragCommand(double dt, double dy)
    {
        var io = ImGui.GetIO();
        var toggleLinkMode = io.KeyAlt;
        var dragInside = io.KeyCtrl && io.KeyAlt;
        var lockTime = io.KeyCtrl && !io.KeyAlt;

        var indexDelta = _layerIndexOnDragStart - (int)(dy / ClipArea.LayerHeight);
        if (indexDelta != 0)
            _layerIndexOnDragStart -= indexDelta;

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
            else if (clip.UsedForRegionMapping ^ toggleLinkMode)
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

            // Capture the stretch rate BEFORE mutation so trim preserves it. Without
            // this, a clip at e.g. 50% speed gradually equalises toward 100% over a few
            // frames of trim drag because both ends move by the same delta — events
            // inside the clip appear to scale until the rate flattens.
            var rate = ComputeRate(clip);

            var org = clip.TimeRange.Start;
            clip.TimeRange.Start = (float)Math.Min(clip.TimeRange.Start + dt, clip.TimeRange.End - MinDuration);
            var d = clip.TimeRange.Start - org;
            if (trim)
                clip.SourceRange.Start += (float)(d * rate);
        }
    }

    public void UpdateDragAtEndPointCommand(double dt, double dv)
    {
        var trim = !ImGui.GetIO().KeyAlt;
        foreach (var clipId in _context.ClipSelection.SelectedClipsIds)
        {
            var clip = _context.ClipSelection.CompositionTimeClips[clipId];

            var rate = ComputeRate(clip);

            var org = clip.TimeRange.End;
            clip.TimeRange.End = (float)Math.Max(clip.TimeRange.End + dt, clip.TimeRange.Start + MinDuration);
            var d = clip.TimeRange.End - org;
            if (trim)
                clip.SourceRange.End += (float)(d * rate);
        }
    }

    /// <summary>
    /// Source-bars-per-timeline-bar ratio. 1.0 for a clip whose source plays at native
    /// speed; &lt; 1 when the source is stretched (plays slower than the timeline) and
    /// &gt; 1 when compressed (plays faster). Falls back to 1 for the zero-duration edge
    /// case so a fresh clip with TimeRange.Start == TimeRange.End behaves like the
    /// non-stretched path before any drag samples a duration.
    /// </summary>
    private static double ComputeRate(TimeClip clip)
    {
        var timelineDuration = clip.TimeRange.Duration;
        if (Math.Abs(timelineDuration) < 0.0001)
            return 1.0;
        return clip.SourceRange.Duration / (double)timelineDuration;
    }

    public void UpdateDragStretchCommand(double scaleU, double scaleV, double originU, double originV)
    {
        foreach (var clipId in _context.ClipSelection.SelectedClipsIds)
        {
            var clip = _context.ClipSelection.CompositionTimeClips[clipId];
            clip.TimeRange.Start = (float)(originU + (clip.TimeRange.Start - originU) * scaleU);
            clip.TimeRange.End = (float)Math.Max(originU + (clip.TimeRange.End - originU) * scaleU, clip.TimeRange.Start + MinDuration);
        }
    }

    /// <summary>
    /// Selection-range edge drags trim the selected clips at the dragged boundary instead of stretching
    /// them: edges are clamped relative to their drag-start values (so dragging back restores them) and
    /// the source range follows at the clip's rate, preserving playback speed. Clips fully inside the
    /// boundary stay untouched.
    /// </summary>
    public void TrimSelectedClipsToBoundary(double boundaryU, bool isStart)
    {
        if (_moveClipsCommand == null)
            return;

        foreach (var clipId in _context.ClipSelection.SelectedClipsIds)
        {
            if (!_context.ClipSelection.CompositionTimeClips.TryGetValue(clipId, out var clip))
                continue;

            if (!_moveClipsCommand.TryGetOriginalRanges(clipId, out var orgTime, out var orgSource))
                continue;

            var timelineDuration = orgTime.Duration;
            var rate = Math.Abs(timelineDuration) < 0.0001 ? 1.0 : orgSource.Duration / (double)timelineDuration;

            if (isStart)
            {
                var newStart = (float)Math.Min(Math.Max(orgTime.Start, boundaryU), orgTime.End - MinDuration);
                clip.TimeRange.Start = newStart;
                clip.SourceRange.Start = (float)(orgSource.Start + (newStart - orgTime.Start) * rate);
            }
            else
            {
                var newEnd = (float)Math.Max(Math.Min(orgTime.End, boundaryU), orgTime.Start + MinDuration);
                clip.TimeRange.End = newEnd;
                clip.SourceRange.End = (float)(orgSource.End + (newEnd - orgTime.End) * rate);
            }
        }
    }

    public void CompleteDragCommand()
    {
        if (_moveClipsCommand == null)
            return;
        _moveClipsCommand.StoreCurrentValues();
        _moveClipsCommand = null;
    }

    // ---------------------------------------------------------------------------
    // Selection-rectangle + snap
    // ---------------------------------------------------------------------------

    public void UpdateSelectionForArea(ImRect screenArea,
                                       SelectionFence.SelectModes selectMode,
                                       Vector2 minScreenPos,
                                       int minLayerIndex)
    {
        var compositionOp = _getCompositionOp();

        if (selectMode == SelectionFence.SelectModes.Replace)
            _context.ClipSelection.Clear();

        var startTime = _context.TimeCanvas.InverseTransformX(screenArea.Min.X);
        var endTime = _context.TimeCanvas.InverseTransformX(screenArea.Max.X);

        var layerMinIndex = (screenArea.Min.Y - minScreenPos.Y - ClipArea.LayerHeight * 0.5f) / ClipArea.LayerHeight + minLayerIndex;
        var layerMaxIndex = (screenArea.Max.Y - minScreenPos.Y - ClipArea.LayerHeight * 0.5f) / ClipArea.LayerHeight + minLayerIndex;

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

    /// <summary>Snap to all non-selected clips.</summary>
    public void CheckForSnap(ref SnapResult snapResult)
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

    // ---------------------------------------------------------------------------
    // Selection queries
    // ---------------------------------------------------------------------------

    public void ClearSelection() => _context.ClipSelection.Clear();
    public bool HasSelectedClips => _context.ClipSelection.Count > 0;
    public bool HasAnyClips => _context.ClipSelection.CompositionTimeClips.Count > 0;
    public IEnumerable<TimeClip> EnumerateSelectedClips() => _context.ClipSelection.GetSelectedClips();

    public void SelectAllClips()
    {
        foreach (var clip in _context.ClipSelection.CompositionTimeClips.Values)
            _context.ClipSelection.AddSelection(clip);
    }

    public TimeRange GetSelectionTimeRange()
    {
        var timeRange = TimeRange.Undefined;
        foreach (var id in _context.ClipSelection.SelectedClipsIds)
        {
            // Selection can transiently reference clips no longer in the composition
            // (e.g. immediately after a split, before ClipSelection re-syncs).
            if (!_context.ClipSelection.CompositionTimeClips.TryGetValue(id, out var s))
                continue;

            // Defensive: heal broken time ranges. FIXME: prevent these upstream.
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

    public bool TryGetBounds(out ImRect bounds, bool useAllIfNonSelected)
    {
        var isFirst = true;
        bounds = new ImRect();

        var range = useAllIfNonSelected
                        ? _context.ClipSelection.GetAllOrSelectedClips()
                        : _context.ClipSelection.GetSelectedClips();
        foreach (var c in range)
        {
            var clipBound = new ImRect(new Vector2(c.TimeRange.Start, c.LayerIndex * ClipArea.LayerHeight),
                                       new Vector2(c.TimeRange.End, (c.LayerIndex + 1) * ClipArea.LayerHeight));
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

    public ClipSelection Selection => _context.ClipSelection;

    private const float MinDuration = 1 / 60f; // In bars

    private readonly ClipArea.LayerContext _context;
    private readonly Func<Instance> _getCompositionOp;

    private Playback? _playback;
    private int _layerIndexOnDragStart;
    private bool _contextMenuIsOpen;
    private int _lastOpVersion = -1;

    private static MoveTimeClipsCommand? _moveClipsCommand;
}
