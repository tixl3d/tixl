#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

internal static class TimeClipItem
{
    /// <summary>
    /// Attributes required and identically for drawing and handling all time clip items of a canvas for the current frame.
    /// </summary>
    public record struct ClipDrawingAttributes(
        ClipArea.LayerContext LayerContext,
        ImRect LayerRect,
        int MinLayerIndex,
        Instance CompositionOp,
        SymbolUi CompositionSymbolUi,
        MoveTimeClipsCommand? MoveClipsCommand,
        ImDrawListPtr DrawList);

    internal static void DrawClip(TimeClip timeClip, ref ClipDrawingAttributes attr)
    {
        var xStartTime = attr.LayerContext.TimeCanvas.TransformX(timeClip.TimeRange.Start) + 1;
        var xEndTime = attr.LayerContext.TimeCanvas.TransformX(timeClip.TimeRange.End) + 1;
        var position = new Vector2(xStartTime,
                                   attr.LayerRect.Min.Y + (timeClip.LayerIndex - attr.MinLayerIndex) * ClipArea.LayerHeight);

        var clipWidth = xEndTime - xStartTime;
        // Clamp so a freshly-created zero-width clip (e.g. a recording in progress with
        // TimeRange.Start == TimeRange.End) still submits a hit-testable body and doesn't
        // trip ImGui's "InvisibleButton size must be non-zero" assert.
        if (clipWidth < 1)
            clipWidth = 1;

        var showSizeHandles = clipWidth > 4 * HandleWidth;
        var bodyWidth = showSizeHandles
                            ? (clipWidth - 2 * HandleWidth)
                            : clipWidth;

        var bodySize = new Vector2(bodyWidth, ClipArea.LayerHeight - 2);
        var clipSize = new Vector2(clipWidth, ClipArea.LayerHeight - 2);

        var symbolChildUi = attr.CompositionSymbolUi.ChildUis[timeClip.Id];

        

        ImGui.PushID(symbolChildUi.Id.GetHashCode());

        var isSelected = attr.LayerContext.ClipSelection.SelectedClipsIds.Contains(timeClip.Id);
        var itemRectMax = position + clipSize - new Vector2(1, 0);

        var rounding = 4.5f;
        var randomColor = DrawUtils.RandomColorForHash(timeClip.Id.GetHashCode());

        var timeRemapped = timeClip.TimeRange != timeClip.SourceRange;
        var timeStretched = Math.Abs(timeClip.TimeRange.Duration - timeClip.SourceRange.Duration) > 0.001;

        // Body and outline
        var isConnected = attr.CompositionSymbolUi.Symbol.Connections.Any(c => c.SourceParentOrChildId == timeClip.Id);

        var isWithinPlaybackTime = timeClip.TimeRange.Contains(attr.LayerContext.TimeCanvas.Playback.TimeInBars);
        var fadeIfInActive = (isConnected && isWithinPlaybackTime) ? 1 : 0.4f;
        
        var fadeIfNotConnected = isConnected ? 1f : 0.2f;
        attr.DrawList.AddRectFilled(position, itemRectMax, randomColor.Fade(0.4f * fadeIfNotConnected * fadeIfInActive), rounding);

        // Live Instance for this clip — used both for the DataClip tick overlay below and
        // for the filename label further down. Missing = null; both consumers handle.
        attr.CompositionOp.Children.TryGetChildInstance(timeClip.Id, out var clipInstance);

        // Per-event tick overlay for ops that publish a DataClip. No-op for everything else.
        if (clipInstance != null)
        {
            DataClipBodyRenderer.TryDraw(clipInstance, timeClip, position, itemRectMax, attr.DrawList);
        }

        if (isSelected)
            attr.DrawList.AddRect(position, itemRectMax, UiColors.Selection, rounding);


        // Label — for ops that load from a file (LoadDataClip, future MidiClip etc.), use
        // the loaded filename instead of the op's symbol name so the user can tell which
        // recording a clip references without opening the parameter window.
        if(ClipArea.LayerHeight > Fonts.FontSmall.FontSize){
            var nameSource = symbolChildUi.SymbolChild.ReadableName;
            if (clipInstance is T3.Core.Operator.Interfaces.IDescriptiveFilename descriptive)
            {
                var path = descriptive.SourcePathSlot.TypedInputValue.Value;
                if (!string.IsNullOrEmpty(path))
                {
                    nameSource = System.IO.Path.GetFileNameWithoutExtension(path);
                }
            }

            var label = timeStretched
                            ? nameSource + $" ({timeClip.Speed*100:0.0}%)"
                            : nameSource;

            ImGui.PushFont(Fonts.FontSmall);
            var labelSize = ImGui.CalcTextSize(label);
            var needsClipping = labelSize.X > clipSize.X;

            if (needsClipping)
                ImGui.PushClipRect(position, itemRectMax - new Vector2(3, 0), true);

            attr.DrawList.AddText(position + new Vector2(4, 1), isSelected ? UiColors.Selection : randomColor.Fade(fadeIfNotConnected), label);

            if (needsClipping)
                ImGui.PopClipRect();

            ImGui.PopFont();
        }

        // Stretch indicators
        {
            if (timeStretched)
            {
                attr.DrawList.AddRectFilled(position + new Vector2(2, clipSize.Y - 4),
                                            position + new Vector2(clipSize.X - 3, clipSize.Y - 2),
                                            UiColors.StatusAttention.Fade(fadeIfNotConnected), rounding);
            }
            else if (timeRemapped)
            {
                attr.DrawList.AddRectFilled(position + new Vector2(2, clipSize.Y - 3),
                                            position + new Vector2(clipSize.X - 3, clipSize.Y - 1),
                                            UiColors.ForegroundFull.Fade(0.3f* fadeIfNotConnected));
            }
        }

        // Draw stretch indicators
        if (isSelected && timeRemapped && attr.LayerContext.ClipSelection.Count == 1)
        {
            var verticalOffset = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - position.Y - ClipArea.LayerHeight;
            var horizontalOffset = attr.LayerContext.TimeCanvas.TransformDirection(new Vector2(timeClip.SourceRange.Start - timeClip.TimeRange.Start, 0)).X;
            var startPosition = position + new Vector2(0, ClipArea.LayerHeight);
            attr.DrawList.AddBezierCubic(startPosition,
                                         startPosition + new Vector2(0, verticalOffset),
                                         startPosition + new Vector2(horizontalOffset, 0),
                                         startPosition + new Vector2(horizontalOffset, verticalOffset),
                                         _timeRemappingColor, 1);

            horizontalOffset = attr.LayerContext.TimeCanvas.TransformDirection(new Vector2(timeClip.SourceRange.End - timeClip.TimeRange.End, 0)).X;
            var endPosition = position + new Vector2(clipSize.X, ClipArea.LayerHeight);
            attr.DrawList.AddBezierCubic(endPosition,
                                         endPosition + new Vector2(0, verticalOffset),
                                         endPosition + new Vector2(horizontalOffset, 0),
                                         endPosition + new Vector2(horizontalOffset, verticalOffset),
                                         _timeRemappingColor, 1);
        }

        // Interaction and dragging
        ImGui.SetCursorScreenPos(showSizeHandles ? (position + _handleOffset) : position);

        var wasClickedDown = ImGui.InvisibleButton("body", bodySize);

        if (ImGui.IsItemHovered())
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4,4));
            ImGui.BeginTooltip();
            {
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.TextUnformatted(symbolChildUi.SymbolChild.ReadableName);
                if (!isConnected)
                {
                    ImGui.TextUnformatted("(Not connected?)");
                }

                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextUnformatted($"Visible: {timeClip.TimeRange.Start:0.00} ... {timeClip.TimeRange.End:0.00}");
                if (timeRemapped)
                {
                    ImGui.TextUnformatted($"Source {timeClip.SourceRange.Start:0.00} ... {timeClip.SourceRange.End:0.00}");
                }

                if (timeStretched)
                {
                    ImGui.TextUnformatted($"Speed: {timeClip.Speed*100:0.0}%");
                }

                ImGui.PopStyleColor();
                ImGui.PopFont();
            }
            ImGui.EndTooltip();
            ImGui.PopStyleVar();
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0))
        {
            if (Structure.TryGetUiAndInstanceInComposition(timeClip.Id, attr.CompositionOp, out _, out var instance))
            {
                if (instance.Symbol.Children.Count > 0)
                    attr.LayerContext.RequestChildComposition(instance.SymbolChildId);
            }
        }

        if (ImGui.IsItemHovered())
        {
            FrameStats.AddHoveredId(symbolChildUi.Id);
        }

        var notClickingOrDragging = !ImGui.IsItemActive() && !ImGui.IsMouseDragging(ImGuiMouseButton.Left);
        if (notClickingOrDragging && attr.MoveClipsCommand != null)
        {
            // Store values and nullify command
            attr.LayerContext.TimeCanvas.CompleteDragCommand();
        }

        if (wasClickedDown)
        {
            FitViewToSelectionHandling.FitViewToSelection();
        }

        HandleDragging(attr, timeClip, isSelected, wasClickedDown, HandleDragMode.Body);

        var handleSize = showSizeHandles ? new Vector2(HandleWidth, ClipArea.LayerHeight) : Vector2.One;

        ImGui.SetCursorScreenPos(position);
        var aHandleClicked = ImGui.InvisibleButton("startHandle", handleSize);
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
        {
            attr.DrawList.AddRectFilled(ImGui.GetItemRectMin() + new Vector2(2, 3),
                                        ImGui.GetItemRectMax() - new Vector2(1, 4),
                                        UiColors.ForegroundFull.Fade(0.3f),
                                        5);
        }

        HandleDragging(attr, timeClip, isSelected, false, HandleDragMode.Start);

        ImGui.SetCursorScreenPos(position + new Vector2(bodyWidth + HandleWidth, 0));
        aHandleClicked |= ImGui.InvisibleButton("endHandle", handleSize);
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
        {
            attr.DrawList.AddRectFilled(ImGui.GetItemRectMin() + new Vector2(0, 3),
                                        ImGui.GetItemRectMax() - new Vector2(3, 4),
                                        UiColors.ForegroundFull.Fade(0.3f),
                                        5);
        }

        HandleDragging(attr, timeClip, isSelected, false, HandleDragMode.End);

        if (aHandleClicked)
        {
            attr.LayerContext.TimeCanvas.CompleteDragCommand();

            if (attr.MoveClipsCommand != null)
            {
                attr.MoveClipsCommand.StoreCurrentValues();
                UndoRedoStack.Add(attr.MoveClipsCommand);
                attr.MoveClipsCommand = null;
            }
        }

        ImGui.PopID();
    }


    // private static double GetSpeed(TimeClip timeClip)
    // {
    //     return Math.Abs(timeClip.TimeRange.Duration) > 0.001
    //                ? Math.Round((timeClip.SourceRange.Duration / timeClip.TimeRange.Duration) * 100)
    //                : 9999;
    // }

    private enum HandleDragMode
    {
        Body = 0,
        Start,
        End,
    }

    /// <summary>
    /// Handles the invocation and update of drag commands. These will be forwarded to the timeline interface and
    /// applied to other selected items like keyframes and other selected time clips
    /// </summary>
    private static void HandleDragging(ClipDrawingAttributes attr, TimeClip timeClip, bool isSelected, bool _, HandleDragMode mode)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(mode == HandleDragMode.Body
                                     ? ImGuiMouseCursor.Hand
                                     : ImGuiMouseCursor.ResizeEW);
        }
        
        var isDeactivated = ImGui.IsItemDeactivated();
        var isActive = ImGui.IsItemActive();
        if (!isActive && !isDeactivated )
            return;
        
        var wasClickRelease = isDeactivated && ImGui.GetMouseDragDelta().Length() < UserSettings.Config.ClickThreshold;
        if (wasClickRelease)
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                if (isSelected)
                {
                    attr.LayerContext.ClipSelection.Deselect(timeClip);
                }

                return;
            }

            if (!isSelected)
            {
                if (!ImGui.GetIO().KeyShift)
                {
                    attr.LayerContext.TimeCanvas.ClearSelection();
                }

                attr.LayerContext.ClipSelection.Select(timeClip);
            }

            return;
        }
        
        var mousePos = ImGui.GetIO().MousePos;
        var currentDragTime = attr.LayerContext.TimeCanvas.InverseTransformX(mousePos.X);
        
        if (attr.MoveClipsCommand == null)
        {
            if (!isSelected)
            {
                if (ImGui.GetIO().KeyShift)
                {
                    attr.LayerContext.ClipSelection.AddSelection(timeClip);
                }
                else
                {
                    attr.LayerContext.ClipSelection.Select(timeClip);
                    
                }
            }
            
            _timeWithinDraggedClip = currentDragTime - timeClip.TimeRange.Start;
            _posPosYOnDragStart = mousePos.Y;
            _dragStartMouseTime = currentDragTime;
            _originalDraggedClipStart = timeClip.TimeRange.Start;
            _lastAppliedDeltaTime = 0;
            attr.LayerContext.TimeCanvas.StartDragCommand(attr.CompositionOp.Symbol.Id);
        }
        
        if (!ImGui.IsMouseDragging(0, UserSettings.Config.ClickThreshold))
            return;
        
        var allowSnapping = !ImGui.GetIO().KeyShift && !(ImGui.GetIO().KeyAlt && ImGui.GetIO().KeyCtrl);

        // SelectionRangeIndicator's anchors are the selected clips' aggregate Start/End —
        // when we drag selected clips, those anchors move along with the clip, so without
        // exclusion the snap handler perpetually "re-snaps to self" and stutters. Same
        // exclusion list keyframe drags already use.
        var snapExclusions = attr.LayerContext.TimeCanvas.SelectionDragSnapExclusions;

        switch (mode)
        {
            case HandleDragMode.Body:
                var dy = _posPosYOnDragStart - mousePos.Y;

                // Derive the unsnapped target from the ORIGINAL drag-start positions, not
                // from incrementally accumulated state. This avoids the slow-drag artefact
                // where snap was sticky for several frames and the cumulative "applied
                // delta" diverged from the absolute mouse motion — leaving the clip stuck
                // or jumping unexpectedly when the mouse finally left the snap range.
                var rawDelta = currentDragTime - _dragStartMouseTime;
                var unsnappedTargetStart = _originalDraggedClipStart + rawDelta;
                var targetStart = unsnappedTargetStart;

                if (allowSnapping && attr.LayerContext.SnapHandler.TryCheckForSnapping(unsnappedTargetStart,
                                                                                       out var snappedClipStartTime,
                                                                                       attr.LayerContext.TimeCanvas.Scale.X,
                                                                                       snapExclusions))
                {
                    targetStart = (float)snappedClipStartTime;
                }
                else if (allowSnapping && attr.LayerContext.SnapHandler.TryCheckForSnapping(unsnappedTargetStart + timeClip.TimeRange.Duration,
                                                                                            out var snappedClipEndTime,
                                                                                            attr.LayerContext.TimeCanvas.Scale.X,
                                                                                            snapExclusions))
                {
                    targetStart = (float)snappedClipEndTime - timeClip.TimeRange.Duration;
                }

                // _lastAppliedDeltaTime stores the cumulative delta-from-original we've
                // committed so far. Compare absolute target → cumulative; emit the
                // increment needed to reach the new cumulative value.
                var finalDelta = targetStart - _originalDraggedClipStart;
                var incrementToApply = finalDelta - _lastAppliedDeltaTime;
                _lastAppliedDeltaTime = finalDelta;

                attr.LayerContext.TimeCanvas.UpdateDragCommand(incrementToApply, dy);
                break;

            case HandleDragMode.Start:
                var newDragStartTime = attr.LayerContext.TimeCanvas.InverseTransformX(mousePos.X);
                if (allowSnapping && attr.LayerContext.SnapHandler.TryCheckForSnapping(newDragStartTime, out var snappedValue3, attr.LayerContext.TimeCanvas.Scale.X, snapExclusions))
                {
                    newDragStartTime = (float)snappedValue3;
                }

                attr.LayerContext.TimeCanvas.UpdateDragAtStartPointCommand(newDragStartTime - timeClip.TimeRange.Start, 0);
                break;

            case HandleDragMode.End:
                var newDragTime = attr.LayerContext.TimeCanvas.InverseTransformX(mousePos.X);
                if (allowSnapping && attr.LayerContext.SnapHandler.TryCheckForSnapping(newDragTime, out var snappedValue4, attr.LayerContext.TimeCanvas.Scale.X, snapExclusions))
                {
                    newDragTime = (float)snappedValue4;
                }

                attr.LayerContext.TimeCanvas.UpdateDragAtEndPointCommand(newDragTime - timeClip.TimeRange.End, 0);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private const float HandleWidth = 7;
    private static float _timeWithinDraggedClip;

    // Drag-start snapshots. Body drag computes the target position from these +
    // the absolute mouse-time delta, then derives the per-frame increment by subtracting
    // the cumulative delta already committed. Avoids drift across snap boundaries.
    private static double _dragStartMouseTime;
    private static double _originalDraggedClipStart;
    private static double _lastAppliedDeltaTime;
    private static readonly Vector2 _handleOffset = new(HandleWidth, 0);
    private static readonly Color _timeRemappingColor = UiColors.StatusAnimated.Fade(0.5f);
    private static float _posPosYOnDragStart;
}