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
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.MagGraph.Ui;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Migrations.AudioClips;
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
    /** A swap handle submitted this frame, drawn once the clip bodies are drawn. */
    private readonly record struct SwapHandle(Guid ClipId, bool IsHighlighted);

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

        // ImGui gives the hover to the first item submitted under the mouse, so the swap handles must be
        // submitted before the clip bodies to win the click — and drawn after them to stay visible.
        SubmitSwapHandles(compositionOp, layerRect, minLayerIndex);

        foreach (var clip in clips)
            TimeClipItem.DrawClip(clip, ref drawAttributes);

        DrawSwapHandleCircles(layerRect, minLayerIndex, drawList);
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

            if (DrawClipMenuItem(_duplicateClipsToTimeId, "Duplicate to Current Time", UserActions.DuplicateWithConnections.ListShortcuts()))
            {
                DuplicateSelectedClipsToCurrentTime(compositionOp);
            }

            if (DrawClipMenuItem(_removeGapsId, "Remove Gaps"))
            {
                RemoveGaps(compositionOp);
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
                // Keyed to the explicit designation: a lone AutoPlay clip is main implicitly and has nothing to unset.
                var isMain = audioProvider!.GetResourceHandle().Clip.Display == AudioClipDisplay.BackgroundImage;
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
    /// The clip actions of the app menu's Edit menu. They mirror the clip context menu, minus the rows the
    /// graph part of the Edit menu already offers for the selected ops (Duplicate, Delete).
    /// </summary>
    public void DrawEditMenuItems(Instance compositionOp)
    {
        if (!HasAnyClips)
            return;

        var hasSelection = _context.ClipSelection.Count > 0;
        var hasPlayback = _playback != null;

        CustomComponents.SeparatorLine();
        CustomComponents.DrawMenuGroupLabel("Timeline Clips");

        if (AppMenuBar.MenuItem("Select Following Clips", UserActions.SelectFollowingClips.ListShortcuts(), isEnabled: hasPlayback)
            && _playback != null)
        {
            SelectClipsStartingAfter(_playback.TimeInBars, _playback.BarsFromSeconds(1 / 30.0));
        }

        if (AppMenuBar.MenuItem("Cut at Time", UserActions.SplitSelectedOrHoveredClips.ListShortcuts(), isEnabled: hasPlayback))
        {
            SplitClipsAtTime(compositionOp);
        }

        if (AppMenuBar.MenuItem("Duplicate to Current Time", UserActions.DuplicateWithConnections.ListShortcuts(),
                                isEnabled: hasSelection && hasPlayback))
        {
            DuplicateSelectedClipsToCurrentTime(compositionOp);
        }

        if (AppMenuBar.MenuItem("Remove Gaps"))
        {
            RemoveGaps(compositionOp);
        }

        if (AppMenuBar.MenuItem("Clear Time Stretch"))
        {
            ClearTimeStretchOfSelectedClips(compositionOp);
        }
    }

    /// <summary>
    /// Closes the gaps between clips on each layer by moving every clip left onto the end of its predecessor.
    /// With several clips selected only those move, and the earliest selected clip of each layer stays in place;
    /// otherwise all clips are packed. Clips that don't move still block the ones after them.
    /// </summary>
    public void RemoveGaps(Instance compositionOp)
    {
        var onlySelected = _context.ClipSelection.Count > 1;
        var sortedClips = _context.ClipSelection.CompositionTimeClips.Values.ToList();
        sortedClips.Sort(_byLayerAndStart);

        var movableClips = new List<TimeClip>();
        foreach (var clip in sortedClips)
        {
            if (!onlySelected || _context.ClipSelection.Contains(clip))
                movableClips.Add(clip);
        }

        if (movableClips.Count < 2)
            return;

        var command = new MoveTimeClipsCommand(compositionOp, movableClips);
        var anyMoved = false;
        var layerIndex = int.MinValue;
        var hasPredecessor = false;
        var predecessorEnd = 0f;

        foreach (var clip in sortedClips)
        {
            if (clip.LayerIndex != layerIndex)
            {
                layerIndex = clip.LayerIndex;
                hasPredecessor = false;
            }

            var isMovable = !onlySelected || _context.ClipSelection.Contains(clip);
            if (!hasPredecessor)
            {
                // A gap in front of the first movable clip is not between clips.
                if (!isMovable)
                    continue;

                hasPredecessor = true;
                predecessorEnd = clip.TimeRange.End;
                continue;
            }

            if (isMovable && clip.TimeRange.Start > predecessorEnd + GapToleranceInBars)
            {
                MoveClipInTime(clip, predecessorEnd - clip.TimeRange.Start);
                anyMoved = true;
            }

            predecessorEnd = MathF.Max(predecessorEnd, clip.TimeRange.End);
        }

        if (!anyMoved)
            return;

        command.StoreCurrentValues();
        UndoRedoStack.AddAndExecute(command);
    }

    /// <summary>
    /// Moves a clip like a plain body drag: a clip that doesn't map its source region keeps its content
    /// locked to timeline time, so its source range moves along.
    /// </summary>
    private static void MoveClipInTime(TimeClip clip, float deltaInBars)
    {
        clip.TimeRange.Start += deltaInBars;
        clip.TimeRange.End += deltaInBars;
        if (clip.UsedForRegionMapping)
            return;

        clip.SourceRange.Start += deltaInBars;
        clip.SourceRange.End += deltaInBars;
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
        // off-screen back into view. The source duration is expressed in the clip's source unit
        // (seconds for media clips), so go through seconds.
        var playback = Playback.Current;
        foreach (var clip in selectedClips)
        {
            var sourceDuration = playback != null
                                     ? (float)clip.SecondsToSource(playback.SecondsFromBars(clip.TimeRange.Duration), playback.Bpm)
                                     : clip.TimeRange.Duration;
            clip.SourceRange.End = clip.SourceRange.Start + sourceDuration;
        }

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
            if (!child.Inputs.TryGetValue(AudioClipsToOps.DisplayInputId, out var displayInput))
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
    private void PushDownOpsOverlappedBy(Instance compositionOp, SymbolUi compositionUi, SymbolUi.Child insertedChildUi,
                                         GraphUiContext? graphContext, List<ICommand> commands)
    {
        var moved = new List<(SymbolUi.Child ChildUi, Vector2 OriginalPos)>();
        var handled = new HashSet<Guid> { insertedChildUi.Id };
        var movers = new Queue<SymbolUi.Child>();
        movers.Enqueue(insertedChildUi);

        var sizes = new Dictionary<Guid, Vector2>(compositionUi.ChildUis.Count);
        foreach (var childUi in compositionUi.ChildUis.Values)
            sizes[childUi.Id] = GetSizeOnCanvas(compositionOp, childUi, graphContext);

        while (movers.Count > 0)
        {
            var mover = movers.Dequeue();
            var moverRect = ImRect.RectWithSize(mover.PosOnCanvas, sizes[mover.Id]);

            foreach (var other in compositionUi.ChildUis.Values)
            {
                if (handled.Contains(other.Id))
                    continue;

                var otherRect = ImRect.RectWithSize(other.PosOnCanvas, sizes[other.Id]);
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

    /** The stored child size is a legacy default; with a graph showing the composition, use the size it draws the op at. */
    private static Vector2 GetSizeOnCanvas(Instance compositionOp, SymbolUi.Child childUi, GraphUiContext? graphContext)
    {
        if (graphContext == null)
            return childUi.Size;

        if (graphContext.Layout.Items.TryGetValue(childUi.Id, out var item) && item.Variant == MagGraphItem.Variants.Operator)
            return item.Size;

        // Not laid out yet, e.g. an op copied earlier in this action.
        return compositionOp.Children.TryGetChildInstance(childUi.Id, out var instance)
                   ? MagGraphLayout.ComputeOperatorSize(graphContext, childUi, instance)
                   : childUi.Size;
    }

    private static GraphUiContext? GetGraphContextShowing(Instance compositionOp)
    {
        return ProjectView.Focused?.GraphView is MagGraphView { Context: { } context }
               && context.CompositionInstance.Symbol.Id == compositionOp.Symbol.Id
                   ? context
                   : null;
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

        // Directly below the original's full height: one fixed row down would cover ops with several input rows.
        var graphContext = GetGraphContextShowing(compositionOp);
        var newPos = symbolChildUi.PosOnCanvas;
        newPos.Y += Math.Max(MagGraphItem.GridSize.Y, GetSizeOnCanvas(compositionOp, symbolChildUi, graphContext).Y);

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

        // After the connections: a connected secondary output adds a row to the copy.
        PushDownOpsOverlappedBy(compositionOp, compositionSymbolUi, newSymbolChildUi, graphContext, commands);
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

    /// <summary>
    /// Duplicates the selected clips like <see cref="DuplicateSelectedClips"/>, but moves the copies so the earliest
    /// one starts at the current time. Durations, source ranges and the clips' offsets to each other are kept.
    /// </summary>
    public void DuplicateSelectedClipsToCurrentTime(Instance compositionOp)
    {
        Debug.Assert(_playback != null);

        // Materialized because copying adds children to the composition while we iterate the selection.
        var selectedClips = _context.ClipSelection.GetSelectedClips().ToList();
        if (selectedClips.Count == 0)
            return;

        var earliestStart = float.PositiveInfinity;
        foreach (var clip in selectedClips)
            earliestStart = MathF.Min(earliestStart, clip.TimeRange.Start);

        var offset = (float)_playback.TimeInBars - earliestStart;

        var commands = new List<ICommand>();
        var newClips = new List<TimeClip>();
        foreach (var clip in selectedClips)
        {
            if (!TryCopyClipOp(compositionOp, clip, commands, out var newTimeClip))
                continue;

            // The copy's ranges as just copied are the undo state; the shifted ones the redo state.
            var moveCommand = new MoveTimeClipsCommand(compositionOp, [newTimeClip]);
            newTimeClip.TimeRange = new TimeRange(clip.TimeRange.Start + offset, clip.TimeRange.End + offset);
            moveCommand.StoreCurrentValues();
            commands.Add(moveCommand);
            newClips.Add(newTimeClip);
        }

        if (commands.Count == 0)
            return;

        UndoRedoStack.Add(new MacroCommand("Duplicate clips to current time", commands));

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
                continue;

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
    // Swap handles: reorder back-to-back clips on a layer
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Holding Alt shows a hollow circle on the hovered clip — or on the selected clips, if they sit back to
    /// back on one layer — when it touches neighboring clips. Dragging a circle moves that block within its
    /// run of touching clips: it hops past a neighbor once its leading edge passes the neighbor's center, so the
    /// run keeps its span and stays gapless; only the order changes.
    /// </summary>
    private void SubmitSwapHandles(Instance compositionOp, ImRect layerRect, int minLayerIndex)
    {
        _swapHandleCount = 0;
        if (_swapCommand == null)
        {
            // Alt alone: Ctrl+Alt is the slip drag.
            var io = ImGui.GetIO();
            var showHandles = io.KeyAlt && !io.KeyCtrl && !io.KeyShift && _moveClipsCommand == null;
            if (!showHandles || !TryFindSwapBlock(layerRect, minLayerIndex))
                return;
        }

        var clips = _context.ClipSelection.CompositionTimeClips;
        for (var runIndex = _swapBlockStart; runIndex < _swapBlockStart + _swapBlockCount; runIndex++)
        {
            if (!clips.TryGetValue(_swapRunIds[runIndex], out var clip))
            {
                // The block changed under a running drag (e.g. an undo); continuing would act on stale ids.
                CancelSwapDrag();
                _swapHandleCount = 0;
                return;
            }

            SubmitSwapHandle(compositionOp, clip, layerRect, minLayerIndex);
        }
    }

    private void SubmitSwapHandle(Instance compositionOp, TimeClip clip, ImRect layerRect, int minLayerIndex)
    {
        var isSwapping = _swapCommand != null;
        GetSwapHandleGeometry(clip, layerRect, minLayerIndex, out var center, out var radius, out var fits);

        // A running drag keeps its handle even on a narrow clip, so ImGui doesn't end the drag.
        if (!fits && !isSwapping)
            return;

        ImGui.PushID(clip.Id.GetHashCode());
        ImGui.SetCursorScreenPos(center - new Vector2(radius));
        ImGui.InvisibleButton("swapHandle", new Vector2(radius * 2));
        var isHovered = ImGui.IsItemHovered();
        var isActive = ImGui.IsItemActive();

        if (ImGui.IsItemActivated())
        {
            StartSwapDrag(compositionOp);
        }
        else if (isActive && _swapCommand != null)
        {
            UpdateSwapDrag();
        }

        if (ImGui.IsItemDeactivated())
        {
            CompleteSwapDrag();
        }

        ImGui.PopID();

        if (isHovered || isActive)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        if (isHovered && !isActive)
            CustomComponents.TooltipForLastItem("Drag to reorder");

        if (_swapHandleCount < _swapHandles.Length)
            _swapHandles[_swapHandleCount++] = new SwapHandle(clip.Id, isHovered || isActive);
    }

    private void DrawSwapHandleCircles(ImRect layerRect, int minLayerIndex, ImDrawListPtr drawList)
    {
        var clips = _context.ClipSelection.CompositionTimeClips;
        var thickness = MathF.Max(1, MathF.Round(1.5f * T3Ui.UiScaleFactor));
        for (var handleIndex = 0; handleIndex < _swapHandleCount; handleIndex++)
        {
            var handle = _swapHandles[handleIndex];

            // Re-resolved: a swap update this frame has already moved the clips.
            if (!clips.TryGetValue(handle.ClipId, out var clip))
                continue;

            GetSwapHandleGeometry(clip, layerRect, minLayerIndex, out var center, out var radius, out _);
            var color = handle.IsHighlighted ? UiColors.ForegroundFull : UiColors.ForegroundFull.Fade(0.7f);

            // The dark halo keeps the circle readable over bright video thumbnails.
            drawList.AddCircle(center, radius, UiColors.BackgroundFull.Fade(0.5f), 0, thickness + 2);
            drawList.AddCircle(center, radius, color, 0, thickness);
        }
    }

    private void GetSwapHandleGeometry(TimeClip clip, ImRect layerRect, int minLayerIndex, out Vector2 center, out float radius, out bool fits)
    {
        GetClipScreenRect(clip, layerRect, minLayerIndex, out var clipMin, out var clipMax);
        var clipHeight = clipMax.Y - clipMin.Y;
        radius = MathF.Max(MathF.Min(SwapHandleRadius * T3Ui.UiScaleFactor, clipHeight * 0.5f - 1), 2);
        fits = clipMax.X - clipMin.X >= radius * 4;
        center = (clipMin + clipMax) * 0.5f;
    }

    /// <summary>Matches the body rect <see cref="TimeClipItem"/> draws.</summary>
    private void GetClipScreenRect(TimeClip clip, ImRect layerRect, int minLayerIndex, out Vector2 min, out Vector2 max)
    {
        var xStart = _context.TimeCanvas.TransformX(clip.TimeRange.Start) + 1;
        var xEnd = _context.TimeCanvas.TransformX(clip.TimeRange.End) + 1;
        min = new Vector2(xStart, layerRect.Min.Y + (clip.LayerIndex - minLayerIndex) * ClipArea.LayerHeight);
        max = new Vector2(MathF.Max(xEnd, xStart + 1) - 1, min.Y + ClipArea.LayerHeight - 2);
    }

    /// <summary>
    /// Finds the run of touching clips around the hovered clip (or, without one, around the selection) and the
    /// block within it that a drag would move: the selected clips if they form one unbroken stretch of the run
    /// and include the anchor, otherwise the hovered clip alone. Fills <see cref="_swapRunIds"/> in time order.
    /// </summary>
    private bool TryFindSwapBlock(ImRect layerRect, int minLayerIndex)
    {
        _swapRunIds.Clear();
        var selection = _context.ClipSelection;
        var clips = selection.CompositionTimeClips;

        TimeClip? anchor = null;
        var anchorIsHovered = false;
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
        {
            foreach (var clip in clips.Values)
            {
                GetClipScreenRect(clip, layerRect, minLayerIndex, out var clipMin, out var clipMax);
                if (!ImGui.IsMouseHoveringRect(clipMin, clipMax))
                    continue;

                anchor = clip;
                anchorIsHovered = true;
                break;
            }
        }

        if (anchor == null)
        {
            foreach (var id in selection.SelectedClipsIds)
            {
                if (clips.TryGetValue(id, out anchor))
                    break;
            }
        }

        if (anchor == null)
            return false;

        _swapLayerClips.Clear();
        foreach (var clip in clips.Values)
        {
            if (clip.LayerIndex == anchor.LayerIndex)
                _swapLayerClips.Add(clip);
        }

        _swapLayerClips.Sort(_byStart);
        var anchorIndex = _swapLayerClips.IndexOf(anchor);

        var runFirst = anchorIndex;
        while (runFirst > 0 && AreTouching(_swapLayerClips[runFirst - 1], _swapLayerClips[runFirst]))
            runFirst--;

        var runLast = anchorIndex;
        while (runLast < _swapLayerClips.Count - 1 && AreTouching(_swapLayerClips[runLast], _swapLayerClips[runLast + 1]))
            runLast++;

        var blockFirst = anchorIndex;
        var blockLast = anchorIndex;
        if (selection.Contains(anchor))
        {
            var selectedInRun = 0;
            var firstSelected = int.MaxValue;
            var lastSelected = -1;
            for (var index = runFirst; index <= runLast; index++)
            {
                if (!selection.Contains(_swapLayerClips[index]))
                    continue;

                selectedInRun++;
                firstSelected = Math.Min(firstSelected, index);
                lastSelected = Math.Max(lastSelected, index);
            }

            var selectionIsBlock = selectedInRun == selection.Count && lastSelected - firstSelected + 1 == selectedInRun;
            if (selectionIsBlock)
            {
                blockFirst = firstSelected;
                blockLast = lastSelected;
            }
            else if (!anchorIsHovered)
            {
                _swapLayerClips.Clear();
                return false;
            }
        }

        // Nothing to swap with when the block is the whole run.
        var hasNeighbor = blockLast - blockFirst < runLast - runFirst;
        if (hasNeighbor)
        {
            for (var index = runFirst; index <= runLast; index++)
                _swapRunIds.Add(_swapLayerClips[index].Id);

            _swapBlockStart = blockFirst - runFirst;
            _swapBlockCount = blockLast - blockFirst + 1;
        }

        _swapLayerClips.Clear();
        return hasNeighbor;
    }

    private static bool AreTouching(TimeClip earlier, TimeClip later)
    {
        return MathF.Abs(later.TimeRange.Start - earlier.TimeRange.End) <= TouchToleranceInBars;
    }

    private void StartSwapDrag(Instance compositionOp)
    {
        var clips = _context.ClipSelection.CompositionTimeClips;
        var runClips = new List<TimeClip>(_swapRunIds.Count);
        foreach (var id in _swapRunIds)
        {
            if (clips.TryGetValue(id, out var clip))
                runClips.Add(clip);
        }

        if (runClips.Count != _swapRunIds.Count)
            return;

        _swapRunStart = runClips[0].TimeRange.Start;
        _swapOtherCenters.Clear();
        var blockStartTime = runClips[_swapBlockStart].TimeRange.Start;
        var blockEndTime = runClips[_swapBlockStart + _swapBlockCount - 1].TimeRange.End;
        for (var runIndex = 0; runIndex < runClips.Count; runIndex++)
        {
            if (runIndex >= _swapBlockStart && runIndex < _swapBlockStart + _swapBlockCount)
                continue;

            var range = runClips[runIndex].TimeRange;
            _swapOtherCenters.Add((range.Start + range.End) * 0.5);
        }

        _swapBlockStartOnDragStart = blockStartTime;
        _swapBlockEndOnDragStart = blockEndTime;
        _swapMouseTimeOnStart = _context.TimeCanvas.InverseTransformX(ImGui.GetMousePos().X);
        _swapInsertIndex = _swapBlockStart;
        _swapCommand = new MoveTimeClipsCommand(compositionOp, runClips);
    }

    private void UpdateSwapDrag()
    {
        _context.TimeCanvas.RequestEdgeScrolling();

        var mouseTime = _context.TimeCanvas.InverseTransformX(ImGui.GetMousePos().X);
        var dragDelta = mouseTime - _swapMouseTimeOnStart;

        // The leading edge decides: a long block hops past a short neighbor as soon as it covers half of it.
        var insertIndex = _swapBlockStart;
        for (var otherIndex = 0; otherIndex < _swapOtherCenters.Count; otherIndex++)
        {
            var isLeftOfBlock = otherIndex < _swapBlockStart;
            if (isLeftOfBlock && _swapBlockStartOnDragStart + dragDelta < _swapOtherCenters[otherIndex])
                insertIndex--;
            else if (!isLeftOfBlock && _swapBlockEndOnDragStart + dragDelta > _swapOtherCenters[otherIndex])
                insertIndex++;
        }

        if (insertIndex == _swapInsertIndex)
            return;

        _swapInsertIndex = insertIndex;

        var clips = _context.ClipSelection.CompositionTimeClips;
        var cursor = _swapRunStart;
        for (var orderIndex = 0; orderIndex < _swapRunIds.Count; orderIndex++)
        {
            var isBlock = orderIndex >= insertIndex && orderIndex < insertIndex + _swapBlockCount;
            int runIndex;
            if (isBlock)
            {
                runIndex = _swapBlockStart + orderIndex - insertIndex;
            }
            else
            {
                var otherIndex = orderIndex < insertIndex ? orderIndex : orderIndex - _swapBlockCount;
                runIndex = otherIndex < _swapBlockStart ? otherIndex : otherIndex + _swapBlockCount;
            }

            if (!clips.TryGetValue(_swapRunIds[runIndex], out var clip))
                continue;

            var duration = clip.TimeRange.Duration;
            MoveClipInTime(clip, cursor - clip.TimeRange.Start);
            cursor += duration;
        }
    }

    private void CompleteSwapDrag()
    {
        if (_swapCommand == null)
            return;

        if (_swapInsertIndex != _swapBlockStart)
        {
            _swapCommand.StoreCurrentValues();
            UndoRedoStack.Add(_swapCommand);
            _getCompositionOp().GetSymbolUi().FlagAsModified();
        }

        _swapCommand = null;
    }

    private void CancelSwapDrag()
    {
        _swapCommand?.Undo();
        _swapCommand = null;
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
    private const float GapToleranceInBars = 0.0001f;
    private const float TouchToleranceInBars = 0.001f;
    private const float SwapHandleRadius = 6;

    private static readonly Comparison<TimeClip> _byStart = static (a, b) => a.TimeRange.Start.CompareTo(b.TimeRange.Start);

    private static readonly Comparison<TimeClip> _byLayerAndStart = static (a, b) => a.LayerIndex != b.LayerIndex
                                                                                          ? a.LayerIndex.CompareTo(b.LayerIndex)
                                                                                          : a.TimeRange.Start.CompareTo(b.TimeRange.Start);

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

    private MoveTimeClipsCommand? _swapCommand;
    private readonly SwapHandle[] _swapHandles = new SwapHandle[32];
    private int _swapHandleCount;
    private readonly List<TimeClip> _swapLayerClips = new(64);
    private readonly List<Guid> _swapRunIds = new(16);
    private int _swapBlockStart;
    private int _swapBlockCount;
    private readonly List<double> _swapOtherCenters = new(16);
    private float _swapRunStart;
    private double _swapBlockStartOnDragStart;
    private double _swapBlockEndOnDragStart;
    private double _swapMouseTimeOnStart;
    private int _swapInsertIndex;

    private static readonly int _selectFollowingClipsId = nameof(_selectFollowingClipsId).GetHashCode();
    private static readonly int _cutAtTimeId = nameof(_cutAtTimeId).GetHashCode();
    private static readonly int _duplicateClipsId = nameof(_duplicateClipsId).GetHashCode();
    private static readonly int _duplicateClipsToTimeId = nameof(_duplicateClipsToTimeId).GetHashCode();
    private static readonly int _removeGapsId = nameof(_removeGapsId).GetHashCode();
    private static readonly int _editClipTimesId = nameof(_editClipTimesId).GetHashCode();
    private static readonly int _clearTimeStretchId = nameof(_clearTimeStretchId).GetHashCode();
    private static readonly int _resetSourceToExtentId = nameof(_resetSourceToExtentId).GetHashCode();
    private static readonly int _deleteClipsId = nameof(_deleteClipsId).GetHashCode();
    private static readonly int _mainSoundtrackId = nameof(_mainSoundtrackId).GetHashCode();
    private static readonly int _showClipDataId = nameof(_showClipDataId).GetHashCode();
}
