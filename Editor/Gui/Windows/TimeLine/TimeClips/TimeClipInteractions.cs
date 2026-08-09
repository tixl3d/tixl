#nullable enable
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6);
        if (ImGui.BeginPopupContextWindow("windows_context_menu"))
        {
            _contextMenuIsOpen = true;

            // Ordered by how often the action follows a right-click, destructive last.
            if (DrawClipMenuItem(_selectFollowingClipsId, "Select Following Clips", UserActions.SelectFollowingClips.ListShortcuts())
                && _playback != null)
            {
                // The playhead anchor is exact (e.g. right after a cut) — a frame of tolerance suffices.
                SelectClipsStartingAfter(_playback.TimeInBars, _playback.BarsFromSeconds(1 / 30.0));
            }

            if (DrawClipMenuItem(_cutAtTimeId, "Cut at Time", UserActions.SplitSelectedOrHoveredClips.ListShortcuts()))
            {
                SplitClipsAtTime(compositionOp);
            }

            if (DrawClipMenuItem(_duplicateClipsId, "Duplicate", UserActions.Duplicate.ListShortcuts()))
            {
                DuplicateSelectedClips(compositionOp);
            }

            if (DrawClipMenuItem(_editClipTimesId, "Edit Clip Times"))
            {
                ClipTimingEditor.TimeClipEditorRequested = true;
            }

            if (DrawClipMenuItem(_clearTimeStretchId, "Clear Time Stretch"))
            {
                ClearTimeStretchOfSelectedClips(compositionOp);
            }

            // Only offered when a selected clip's symbol declares an authored source extent.
            if (AnySelectedClipHasSourceExtent(compositionOp)
                && DrawClipMenuItem(_resetSourceToExtentId, "Reset Source to Extent"))
            {
                ResetSourceToExtentOfSelectedClips(compositionOp);
            }

            if (DrawClipMenuItem(_deleteClipsId, "Delete", UserActions.DeleteSelection.ListShortcuts()))
            {
                DeleteSelectedClips(compositionOp);
            }

            // Only offered when the selection includes a DataClip op - the inline pane has nothing to show otherwise.
            var hasClipDataItem = InlineDataClipArea.HasSelectedDataClipInstance(_context.TimeCanvas, compositionOp);
            var hasSoundtrackItem = TryGetSelectedAudioClip(compositionOp, out var audioClipId, out var audioProvider);

            if (hasClipDataItem || hasSoundtrackItem)
            {
                CustomComponents.SeparatorLine();
            }

            if (hasSoundtrackItem)
            {
                var isMain = audioProvider!.GetResourceHandle().Clip.IsMainSoundtrack;
                if (DrawClipMenuItem(_mainSoundtrackId, isMain ? "Unset Main Soundtrack" : "Set as Main Soundtrack"))
                {
                    SetMainSoundtrackClip(compositionOp, audioClipId, enable: !isMain);
                }
            }

            if (hasClipDataItem)
            {
                var showClipData = _context.TimeCanvas.InlineDataClipEditEnabled;
                if (DrawClipMenuItem(_showClipDataId, "Show Clip Data", null, showClipData))
                {
                    _context.TimeCanvas.InlineDataClipEditEnabled = !showClipData;
                }
            }

            // The keyframe editor appends its own items into this popup.
            CustomComponents.SeparatorLine();

            ImGui.EndPopup();
        }
        else
        {
            _contextMenuIsOpen = false;
        }
        ImGui.PopStyleVar(3);
    }

    /// <summary>
    /// This menu has no icons, so the icon column stays unreserved — matching the keyframe items that the
    /// curve editor appends into the same popup.
    /// </summary>
    private static bool DrawClipMenuItem(int id, string label, string? keyboardShortCut = null, bool isChecked = false)
    {
        return CustomComponents.DrawMenuItem(id, label, keyboardShortCut, isChecked, reserveIconColumn: false);
    }

    private bool AnySelectedClipHasSourceExtent(Instance compositionOp)
    {
        foreach (var clip in _context.ClipSelection.GetAllOrSelectedClips())
        {
            if (TryGetSourceExtentForClip(compositionOp, clip, out _))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Snaps the selected clips' SourceRange back to their symbol's authored source extent
    /// (<see cref="TimelineState.SourceExtent"/>) — the recovery path for instances placed before
    /// the extent was authored, whose SourceRange points far outside the content.
    /// </summary>
    private void ResetSourceToExtentOfSelectedClips(Instance compositionOp)
    {
        var selectedClips = _context.ClipSelection.GetAllOrSelectedClips().ToList();
        var moveTimeClipCommand = new MoveTimeClipsCommand(compositionOp, selectedClips);

        var anyChanged = false;
        foreach (var clip in selectedClips)
        {
            if (!TryGetSourceExtentForClip(compositionOp, clip, out var extent))
                continue;

            clip.SourceRange = extent;
            anyChanged = true;
        }

        if (anyChanged)
        {
            moveTimeClipCommand.StoreCurrentValues();
            UndoRedoStack.AddAndExecute(moveTimeClipCommand);
        }
    }

    private static bool TryGetSourceExtentForClip(Instance compositionOp, TimeClip clip, out TimeRange extent)
    {
        extent = default;
        if (!compositionOp.Children.TryGetChildInstance(clip.Id, out var instance))
            return false;

        if (instance.Symbol.GetSymbolUi()?.TimelineState?.SourceExtent is not { } authoredExtent
            || authoredExtent.Duration <= 0)
            return false;

        extent = authoredExtent;
        return true;
    }

    private void ClearTimeStretchOfSelectedClips(Instance compositionOp)
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
        // action (Edit Clip Times, Cut, drag) is almost always still on them.
    }

    /// <summary>
    /// The "Main Soundtrack" toggle only applies to a single selected [AudioClip], so the row is
    /// omitted entirely for any other selection.
    /// </summary>
    private bool TryGetSelectedAudioClip(Instance compositionOp, out Guid clipId, out IAudioClipProvider? provider)
    {
        clipId = default;
        provider = null;

        if (_context.ClipSelection.Count != 1)
            return false;

        foreach (var id in _context.ClipSelection.SelectedClipsIds)
        {
            clipId = id;
            break;
        }

        if (!compositionOp.Children.TryGetChildInstance(clipId, out var instance)
            || instance is not IAudioClipProvider audioClipProvider)
            return false;

        provider = audioClipProvider;
        return true;
    }

    /// <summary>
    /// Sets the clip's Display to BackgroundImage — which drives the timeline background, FFT routing and
    /// the export duration — and clears the designation from every other audio clip.
    /// </summary>
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

    /// <summary>
    /// Copies a clip's op within its composition: the new node lands one grid row below the original
    /// (pushing whatever it would cover further down) and repeats the original's multi-input connections,
    /// so the copy is wired the same way. The copy starts out with the original's time and source ranges;
    /// callers that need different ones adjust them through their own <see cref="MoveTimeClipsCommand"/>.
    /// </summary>
    private bool TryCopyClipOp(Instance compositionOp, TimeClip clip, List<ICommand> commands,
                               [NotNullWhen(true)] out TimeClip? newTimeClip)
    {
        newTimeClip = null;

        var compositionSymbolUi = compositionOp.GetSymbolUi();
        if (!compositionSymbolUi.ChildUis.TryGetValue(clip.Id, out var symbolChildUi))
            return false;

        var newPos = symbolChildUi.PosOnCanvas;
        newPos.Y += MagGraphItem.GridSize.Y;

        // Pass an empty section list (not null) so the command does not fall back
        // to cloning every section in the composition.
        var copyCommand = new CopySymbolChildrenCommand(compositionSymbolUi,
                                                        [symbolChildUi],
                                                        [],
                                                        compositionSymbolUi,
                                                        newPos);
        copyCommand.Do();
        commands.Add(copyCommand);

        var newChildId = copyCommand.OldToNewChildIds[clip.Id];
        if (!compositionOp.Children.TryGetChildInstance(newChildId, out var newInstance))
            return false;

        newTimeClip = newInstance.Outputs.OfType<ITimeClipProvider>().Single().TimeClip;
        var newSymbolChildUi = compositionSymbolUi.ChildUis[newChildId];

        // Only ops the user actually named get an incremented copy name — an unnamed op keeps showing its
        // symbol name, and turning that into "Layer2" would fake a rename the user never made.
        if (symbolChildUi.SymbolChild.HasCustomName)
        {
            var renameCommand = new ChangeSymbolChildNameCommand(newSymbolChildUi, compositionSymbolUi.Symbol)
                                    {
                                        NewName = symbolChildUi.SymbolChild.Name.AppendOrIncrementVersionNumber()
                                    };
            renameCommand.Do();
            commands.Add(renameCommand);
        }

        PushDownOpsOverlappedBy(compositionSymbolUi, newSymbolChildUi, commands);

        // Repeat the original clip's connections into multi-inputs, so the copy stays wired the same way —
        // regardless of which output carried the connection (an [AudioClip]'s AudioReference into a bus
        // just as much as a TimeClip command into a group).
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

        return true;
    }

    /// <summary>
    /// Duplicates the selected clips in place: the copies keep the original time and source ranges, and the
    /// overlap pass in <see cref="DrawClips"/> moves them onto a free layer.
    /// </summary>
    public void DuplicateSelectedClips(Instance compositionOp)
    {
        var commands = new List<ICommand>();
        var newClips = new List<TimeClip>();

        // Materialized because copying adds children to the composition while we iterate the selection.
        foreach (var clip in _context.ClipSelection.GetSelectedClips().ToList())
        {
            if (TryCopyClipOp(compositionOp, clip, commands, out var newTimeClip))
                newClips.Add(newTimeClip);
        }

        if (commands.Count == 0)
            return;

        UndoRedoStack.Add(new MacroCommand("Duplicate clips", commands));

        SelectClips(newClips);
        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children | ProjectView.ChangeTypes.Connections);
    }

    public void SplitClipsAtTime(Instance compositionOp)
    {
        Debug.Assert(_playback != null);

        var timeInBars = _playback.TimeInBars;
        var newClips = new List<TimeClip>();

        var commands = new List<ICommand>();
        foreach (var clip in _context.ClipSelection.GetAllOrSelectedClips().ToList())
        {
            if (!clip.TimeRange.Contains(timeInBars))
                return;

            if (timeInBars - clip.TimeRange.Start < 0.01
                || clip.TimeRange.End - timeInBars < 0.01)
            {
                Log.Debug("This clip would be too small...");
                continue;
            }

            var orgTimeRangeEnd = clip.TimeRange.End;
            var originalSourceDuration = clip.SourceRange.Duration;
            var normalizedCutPosition = ((float)timeInBars - clip.TimeRange.Start) / clip.TimeRange.Duration;

            if (!TryCopyClipOp(compositionOp, clip, commands, out var newTimeClip))
                continue;

            // Capture the new clip's just-copied TimeRange/SourceRange as the undo state, then mutate to
            // the "second half" ranges and store those as the redo state.
            var adjustNewClipCommand = new MoveTimeClipsCommand(compositionOp, [newTimeClip]);
            newTimeClip.TimeRange = new TimeRange((float)timeInBars, orgTimeRangeEnd);
            newTimeClip.SourceRange.Start = newTimeClip.SourceRange.Start + originalSourceDuration * normalizedCutPosition;
            newTimeClip.SourceRange.End = clip.SourceRange.End;
            adjustNewClipCommand.StoreCurrentValues();
            commands.Add(adjustNewClipCommand);
            newClips.Add(newTimeClip);

            // Adjust first clip end time
            var adjustFirstClipCommand = new MoveTimeClipsCommand(compositionOp, [clip]);
            clip.TimeRange.End = (float)timeInBars;
            clip.SourceRange.Duration = originalSourceDuration * normalizedCutPosition;
            adjustFirstClipCommand.StoreCurrentValues();

            commands.Add(adjustFirstClipCommand);
        }

        if (commands.Count == 0)
        {
            Log.Debug($"There are no time clips to split at current time {timeInBars:0.0}");
            return;
        }

        var macroCommands = new MacroCommand("split clip", commands);
        UndoRedoStack.Add(macroCommands);

        SelectClips(newClips);
        ProjectView.Focused?.FlagChanges(ProjectView.ChangeTypes.Children | ProjectView.ChangeTypes.Connections);
    }

    private void SelectClips(List<TimeClip> clips)
    {
        _context.ClipSelection.Clear();
        foreach (var clip in clips)
            _context.ClipSelection.AddSelection(clip);
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
                // Slip is direct manipulation: the footage follows the mouse (dragging right reveals
                // earlier source), matching the SourceRegionIndicator's drag. Scaled by Speed so the
                // content tracks the cursor 1:1 on stretched clips.
                var slip = (float)dt * clip.Speed;
                clip.SourceRange.Start -= slip;
                clip.SourceRange.End -= slip;
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
    /// them: edges follow the boundary (relative to their drag-start values, so dragging back restores
    /// them) and the source range follows at the clip's rate, preserving playback speed. Clips whose edge
    /// sat at the drag-start boundary (<paramref name="origBoundaryU"/>) extend outward with the handle;
    /// inner clips only get shortened once the boundary passes them.
    /// </summary>
    public void TrimSelectedClipsToBoundary(double boundaryU, double origBoundaryU, bool isStart)
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
                var followsHandle = Math.Abs(orgTime.Start - origBoundaryU) < 0.001;
                var target = followsHandle ? boundaryU : Math.Max(orgTime.Start, boundaryU);
                var newStart = (float)Math.Min(target, orgTime.End - MinDuration);
                clip.TimeRange.Start = newStart;
                clip.SourceRange.Start = (float)(orgSource.Start + (newStart - orgTime.Start) * rate);
            }
            else
            {
                var followsHandle = Math.Abs(orgTime.End - origBoundaryU) < 0.001;
                var target = followsHandle ? boundaryU : Math.Min(orgTime.End, boundaryU);
                var newEnd = (float)Math.Max(target, orgTime.Start + MinDuration);
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

    /// <summary>True while a clip move/trim drag is in flight. Keyframes riding on the dragged clips move
    /// with them in playback time, so snap attractors must not use them as anchors — the clip would chase
    /// its own keys and jitter.</summary>
    internal static bool IsDraggingClips => _moveClipsCommand != null;

    private static MoveTimeClipsCommand? _moveClipsCommand;

    private static readonly int _selectFollowingClipsId = nameof(_selectFollowingClipsId).GetHashCode();
    private static readonly int _cutAtTimeId = nameof(_cutAtTimeId).GetHashCode();
    private static readonly int _duplicateClipsId = nameof(_duplicateClipsId).GetHashCode();
    private static readonly int _editClipTimesId = nameof(_editClipTimesId).GetHashCode();
    private static readonly int _clearTimeStretchId = nameof(_clearTimeStretchId).GetHashCode();
    private static readonly int _resetSourceToExtentId = nameof(_resetSourceToExtentId).GetHashCode();
    private static readonly int _deleteClipsId = nameof(_deleteClipsId).GetHashCode();
    private static readonly int _mainSoundtrackId = nameof(_mainSoundtrackId).GetHashCode();
    private static readonly int _showClipDataId = nameof(_showClipDataId).GetHashCode();
}
