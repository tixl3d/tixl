#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.Animation;
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
            ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(0, 3)); // keep some padding 
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
        }
    }

    private void DrawAllLayers(Instance compositionOp)
    {
        var clips = _context.ClipSelection.CompositionTimeClips.Values;

        if (clips.Count == 0)
        {
            LastHeight = 0;
            return;
        }

        // Adjust height to bounds
        {
            _minLayerIndex = int.MaxValue;
            _maxLayerIndex = int.MinValue;
            foreach (var clip in clips)
            {
                _minLayerIndex = Math.Min(clip.LayerIndex, _minLayerIndex);
                _maxLayerIndex = Math.Max(clip.LayerIndex, _maxLayerIndex);
            }
        }

        // Draw layer lines
        var min = ImGui.GetCursorScreenPos() + new Vector2(0, LayerHeight * 0.5f);
        var max = min + new Vector2(ImGui.GetContentRegionAvail().X,
                                    LayerHeight * (_maxLayerIndex - _minLayerIndex + 1) + 1);
        LastHeight = max.Y - min.Y + 5;
        _drawList.AddRectFilled(new Vector2(min.X, max.Y - 2),
                                new Vector2(max.X, max.Y - 0), UiColors.BackgroundFull.Fade(0.6f));

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

        ImGui.SetCursorScreenPos(min + new Vector2(0, LayerHeight));
    }

    
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

    /// <remarks>
    /// This command is incomplete and likely to lead to inconsistent data
    /// </remarks>
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
            var cmd = new CopySymbolChildrenCommand(compositionSymbolUi,
                                                    [symbolChildUi],
                                                    null,
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

            newTimeClip.TimeRange = new TimeRange((float)_playback.TimeInBars, orgTimeRangeEnd);
            newTimeClip.SourceRange.Start = newTimeClip.SourceRange.Start + originalSourceDuration * normalizedCutPosition;
            newTimeClip.SourceRange.End = clip.SourceRange.End;
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
            var s = _context.ClipSelection.CompositionTimeClips[id];
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