#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Resource.Assets;
using T3.Editor.Gui.Interaction.Snapping;
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

        // Horizontal off-screen cull. A clip entirely left or right of the visible layer
        // area draws nothing and can't be interacted with — the item being dragged stays
        // under the mouse, hence on-screen, so the active drag is never the culled one.
        // This is the main win for compositions with many clips but only a few in view:
        // it skips the body, the per-event DataClip/audio overlays, the label, and the
        // interaction buttons entirely.
        if (xEndTime < attr.LayerRect.Min.X || xStartTime > attr.LayerRect.Max.X)
            return;

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

        // Live Instance for this clip — drives the clip color, the body renderers below and the
        // filename label further down. Missing = null; all consumers handle.
        attr.CompositionOp.Children.TryGetChildInstance(timeClip.Id, out var clipInstance);

        // Media clips share their type color (audio-graph / texture) instead of a per-clip random hue,
        // keeping audio and video clip styling aligned.
        var isAudioClip = clipInstance is IAudioClipProvider;
        var isVideoClip = clipInstance != null && clipInstance.Symbol.Id == _videoClipSymbolId;
        var randomColor = isAudioClip
                              ? UiColors.ColorForAudioGraph
                              : isVideoClip
                                  ? UiColors.ColorForTextures
                                  : DrawUtils.RandomColorForHash(timeClip.Id.GetHashCode());

        var timeRemapped = timeClip.TimeRange != timeClip.SourceRange;
        var playbackSpeed = timeClip.GetPlaybackSpeed(attr.LayerContext.TimeCanvas.Playback.Bpm);
        var timeStretched = Math.Abs(playbackSpeed - 1) > 0.001;
        var showsSpeed = timeStretched;

        // Body and outline
        var isConnected = attr.CompositionSymbolUi.Symbol.Connections.Any(c => c.SourceParentOrChildId == timeClip.Id);

        var isWithinPlaybackTime = timeClip.TimeRange.Contains(attr.LayerContext.TimeCanvas.Playback.TimeInBars);
        var fadeIfInActive = (isConnected && isWithinPlaybackTime) ? 1 : 0.8f;
        
        var fadeIfNotConnected = isConnected ? 1f : 0.4f;

        // Media clips carry visual content (thumbnails, waveforms), so they never fade for connection or
        // activity state — dimming made the content hard to read. Their opacity only responds to
        // hover/selection (0.8 → 1). Muted audio still fades as a status indication.
        var isClipHovered = ImGui.IsMouseHoveringRect(position, itemRectMax);
        var isMediaClip = isAudioClip || isVideoClip;
        var mediaFade = isClipHovered || isSelected ? 1f : 0.8f;
        if (isMediaClip && clipInstance is IAudioClipProvider audioProvider && audioProvider.GetResourceHandle().Clip.IsMuted)
            mediaFade *= 0.4f;

        var innerColor = Color.Mix(UiColors.BackgroundFull, randomColor, 0.5f)
                              .Fade(isMediaClip ? mediaFade : 0.8f * fadeIfNotConnected * fadeIfInActive);
        attr.DrawList.AddRectFilled(position, itemRectMax, innerColor, rounding);

        // Per-event tick overlay for ops that publish a DataClip; waveform for [AudioClip] ops.
        // Each no-ops for op kinds it doesn't handle.
        var thumbLayout = default(VideoThumbLayout);
        if (clipInstance != null)
        {
            DataClipBodyRenderer.TryDraw(clipInstance, timeClip, position, itemRectMax,
                                         attr.LayerRect.Min.X, attr.LayerRect.Max.X, attr.DrawList);
            AudioClipBodyRenderer.TryDraw(clipInstance, timeClip, position, itemRectMax, attr.DrawList);

            if (isVideoClip)
                thumbLayout = DrawVideoClipThumbnails(ref attr, timeClip, clipInstance, position, itemRectMax,
                                                      mediaFade, innerColor);
        }

        // Disabled indicator — same X cross the graph draws on disabled ops.
        if (symbolChildUi.SymbolChild.IsDisabled)
        {
            DrawUtils.DrawOverlayLine(attr.DrawList, 1, Vector2.Zero, Vector2.One, position, itemRectMax);
            DrawUtils.DrawOverlayLine(attr.DrawList, 1, new Vector2(1, 0), new Vector2(0, 1), position, itemRectMax);
        }

        if (isSelected)
            attr.DrawList.AddRect(position, itemRectMax, UiColors.Selection, rounding);


        // Label — for ops that load from a file (LoadDataClip, MidiClip etc.), use
        // the loaded filename instead of the op's symbol name so the user can tell which
        // recording a clip references without opening the parameter window.
        if(ClipArea.LayerHeight > Fonts.FontSmall.FontSize){
            var nameSource = symbolChildUi.SymbolChild.ReadableName;
            if (clipInstance is T3.Core.Operator.Interfaces.IDescriptiveFilename descriptive)
            {
                var path = descriptive.SourcePathSlot.TypedInputValue.Value;
                if (!string.IsNullOrEmpty(path))
                {
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                    // A renamed op shows just its custom name in quotes (the file it references moves to
                    // the tooltip); an unnamed op shows the filename.
                    nameSource = symbolChildUi.SymbolChild.HasCustomName
                                     ? $"\"{symbolChildUi.SymbolChild.Name}\""
                                     : fileName;
                }
            }

            var label = showsSpeed
                            ? nameSource + $" ({playbackSpeed*100:0.0}%)"
                            : nameSource;

            ImGui.PushFont(Fonts.FontSmall);
            var labelSize = ImGui.CalcTextSize(label);

            // Keep the title readable when the clip starts off the left edge of the view: pin the text to the
            // visible area's left edge, but never push it past the clip's own right edge. When single-row
            // thumbnails are visible, the label sits between them, vertically centered.
            var labelMaxX = thumbLayout.HasThumbnails && !thumbLayout.TwoRows ? thumbLayout.LabelMaxX : itemRectMax.X - 3;
            var labelMinX = thumbLayout.HasThumbnails && !thumbLayout.TwoRows ? thumbLayout.LabelMinX : position.X + 4;
            var labelX = Math.Min(Math.Max(labelMinX, attr.LayerRect.Min.X + 4), labelMaxX);
            var labelY = thumbLayout.HasThumbnails && !thumbLayout.TwoRows
                             ? position.Y + (clipSize.Y - labelSize.Y) * 0.5f
                             : position.Y + 1;
            var labelPos = new Vector2(labelX, labelY);

            // Narrow media clips fade their title out instead of hard-clipping it: full opacity above 4×
            // the small-font height, gone below 2×.
            var labelAlpha = isMediaClip
                                 ? Math.Clamp((clipWidth / Fonts.FontSmall.FontSize - 2) / 2f, 0f, 1f)
                                 : fadeIfNotConnected;

            if (labelAlpha > 0.01f)
            {
                // Mixing the type color toward the foreground keeps labels readable on the tinted bodies;
                // hover brightens further, and selection keeps its distinct color.
                var labelColor = isSelected
                                     ? UiColors.Selection.Fade(labelAlpha)
                                     : Color.Mix(randomColor, UiColors.ForegroundFull, isClipHovered ? 0.9f : 0.7f).Fade(labelAlpha);

                if (isVideoClip)
                {
                    // Video clips truncate with an ellipsis instead of hard-clipping at the edge.
                    attr.DrawList.AddText(labelPos, labelColor, TruncateLabel(label, labelSize.X, labelMaxX - labelX));
                }
                else
                {
                    var needsClipping = labelPos.X + labelSize.X > labelMaxX;
                    if (needsClipping)
                        ImGui.PushClipRect(position, itemRectMax - new Vector2(3, 0), true);

                    attr.DrawList.AddText(labelPos, labelColor, label);

                    if (needsClipping)
                        ImGui.PopClipRect();
                }
            }

            ImGui.PopFont();
        }

        // Stretch indicators — media clips skip them: their thumbnails/waveform already show the content,
        // and a time-remap is the norm for them, not a state worth flagging.
        if (!isMediaClip)
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
                                            UiColors.ForegroundFull.Fade(0.3f * fadeIfNotConnected));
            }
        }

        // Remap source curves into the ruler — hidden for media clips (too distracting next to their content).
        if (!isMediaClip && isSelected && timeRemapped && attr.LayerContext.ClipSelection.Count == 1)
        {
            var estimatedRulerHeight = 40;
            var verticalOffset = ImGui.GetWindowPos().Y  + estimatedRulerHeight - position.Y - ClipArea.LayerHeight;
            var horizontalOffset = attr.LayerContext.TimeCanvas.TransformDirection(new Vector2(timeClip.SourceRange.Start - timeClip.TimeRange.Start, 0)).X;
            var startPosition = position;
            attr.DrawList.AddBezierCubic(startPosition,
                                         startPosition + new Vector2(0, verticalOffset),
                                         startPosition + new Vector2(horizontalOffset, 0),
                                         startPosition + new Vector2(horizontalOffset, verticalOffset),
                                         _timeRemappingColor, 1);

            horizontalOffset = attr.LayerContext.TimeCanvas.TransformDirection(new Vector2(timeClip.SourceRange.End - timeClip.TimeRange.End, 0)).X;
            var endPosition = position + new Vector2(clipSize.X, 0);
            attr.DrawList.AddBezierCubic(endPosition,
                                         endPosition + new Vector2(0, verticalOffset),
                                         endPosition + new Vector2(horizontalOffset, 0),
                                         endPosition + new Vector2(horizontalOffset, verticalOffset),
                                         _timeRemappingColor, 1);
        }

        // Interaction and dragging
        ImGui.SetCursorScreenPos(showSizeHandles ? (position + _handleOffset) : position);

        var wasClickedDown = ImGui.InvisibleButton("body", bodySize);
        var bodyHovered = ImGui.IsItemHovered();
        var bodyActive = ImGui.IsItemActive();

        if (bodyHovered)
        {
            var hasContentExtent = TryGetContentExtent(ref attr, timeClip, clipInstance, out var contentExtent);

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

                // The referenced asset — renamed clips no longer show it in their label.
                if (clipInstance is T3.Core.Operator.Interfaces.IDescriptiveFilename tooltipFile)
                {
                    var assetPath = tooltipFile.SourcePathSlot.TypedInputValue.Value;
                    if (!string.IsNullOrEmpty(assetPath))
                        ImGui.TextUnformatted(System.IO.Path.GetFileName(assetPath));
                }

                ImGui.TextUnformatted($"Visible: {timeClip.TimeRange.Start:0.00} ... {timeClip.TimeRange.End:0.00}");
                if (timeRemapped)
                {
                    ImGui.TextUnformatted($"Source {timeClip.SourceRange.Start:0.00} ... {timeClip.SourceRange.End:0.00}");
                }

                if (hasContentExtent)
                {
                    var readsPastFootage = timeClip.SourceRange.Start < contentExtent.Start - 0.001f
                                           || timeClip.SourceRange.End > contentExtent.End + 0.001f;
                    ImGui.TextUnformatted(readsPastFootage
                                              ? $"Footage: {contentExtent.Start:0.00} ... {contentExtent.End:0.00} (reads past end — loops/freezes)"
                                              : $"Footage: {contentExtent.Start:0.00} ... {contentExtent.End:0.00}");
                }

                if (showsSpeed)
                {
                    ImGui.TextUnformatted($"Speed: {playbackSpeed*100:0.0}%");
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
        var startHandleActive = ImGui.IsItemHovered() || ImGui.IsItemActive();
        if (startHandleActive)
        {
            attr.DrawList.AddRectFilled(ImGui.GetItemRectMin() + new Vector2(2, 3),
                                        ImGui.GetItemRectMax() - new Vector2(1, 4),
                                        UiColors.ForegroundFull.Fade(0.3f),
                                        5);
        }

        HandleDragging(attr, timeClip, isSelected, false, HandleDragMode.Start);

        ImGui.SetCursorScreenPos(position + new Vector2(bodyWidth + HandleWidth, 0));
        aHandleClicked |= ImGui.InvisibleButton("endHandle", handleSize);
        var endHandleActive = ImGui.IsItemHovered() || ImGui.IsItemActive();
        if (endHandleActive)
        {
            attr.DrawList.AddRectFilled(ImGui.GetItemRectMin() + new Vector2(0, 3),
                                        ImGui.GetItemRectMax() - new Vector2(3, 4),
                                        UiColors.ForegroundFull.Fade(0.3f),
                                        5);
        }

        HandleDragging(attr, timeClip, isSelected, false, HandleDragMode.End);

        // The footage extent renders as a source region in the ruler (see SourceRegionIndicator), not as an
        // on-clip outline — that was hard to read against neighboring clips. Publish while hovering or while
        // any drag of this clip is active (IsItemActive holds even when the mouse outruns the clip), and for
        // the single selected media clip.
        if ((bodyHovered || bodyActive || startHandleActive || endHandleActive)
            && TryGetContentExtent(ref attr, timeClip, clipInstance, out var hoverExtent))
        {
            MediaClipSourceRegion.PublishHovered(timeClip, hoverExtent);
        }

        if (isSelected && attr.LayerContext.ClipSelection.Count == 1
            && TryGetContentExtent(ref attr, timeClip, clipInstance, out var selectedExtent))
        {
            MediaClipSourceRegion.PublishSelected(timeClip, selectedExtent);
        }

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
    private static void HandleDragging(ClipDrawingAttributes attr, TimeClip timeClip, bool isSelected, bool wasClicked, HandleDragMode mode)
    {
        var isDeactivated = ImGui.IsItemDeactivated();
        var isActive = ImGui.IsItemActive();

        // Keep the cursor stable through the whole drag: during a trim the mouse regularly outruns the
        // narrow handle rect, and hover-only cursor setting made it flicker between resize and arrow.
        if (ImGui.IsItemHovered() || isActive)
        {
            ImGui.SetMouseCursor(mode == HandleDragMode.Body
                                     ? ImGuiMouseCursor.Hand
                                     : ImGuiMouseCursor.ResizeEW);
        }
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
                    // Full clear before adding — ClipSelection.Select only touches the
                    // op-clip side, so without this an audio clip selected before this
                    // press would remain selected alongside the new op clip.
                    attr.LayerContext.TimeCanvas.ClearSelection();
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
                // Snap the in-point to the first frame of the available footage (SourceRange.Start == 0).
                var startFootageAttractor = TryGetFootageBoundaryTimes(ref attr, timeClip, out var footageStartTime, out _)
                                                ? UseFootageSnapAnchor(footageStartTime)
                                                : null;
                if (allowSnapping && attr.LayerContext.SnapHandler.TryCheckForSnapping(newDragStartTime, out var snappedValue3, attr.LayerContext.TimeCanvas.Scale.X, snapExclusions, startFootageAttractor))
                {
                    newDragStartTime = (float)snappedValue3;
                }

                attr.LayerContext.TimeCanvas.UpdateDragAtStartPointCommand(newDragStartTime - timeClip.TimeRange.Start, 0);
                break;

            case HandleDragMode.End:
                var newDragTime = attr.LayerContext.TimeCanvas.InverseTransformX(mousePos.X);
                // Snap the out-point to the last frame of the available footage (SourceRange.End == duration).
                var endFootageAttractor = TryGetFootageBoundaryTimes(ref attr, timeClip, out _, out var footageEndTime)
                                              ? UseFootageSnapAnchor(footageEndTime)
                                              : null;
                if (allowSnapping && attr.LayerContext.SnapHandler.TryCheckForSnapping(newDragTime, out var snappedValue4, attr.LayerContext.TimeCanvas.Scale.X, snapExclusions, endFootageAttractor))
                {
                    newDragTime = (float)snappedValue4;
                }

                attr.LayerContext.TimeCanvas.UpdateDragAtEndPointCommand(newDragTime - timeClip.TimeRange.End, 0);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    /// <summary>How the in/out-point thumbnails were laid out, so the label can flow around them:
    /// single-row mode centers the title between the thumbnails, two-row mode keeps it in its own
    /// top row above them.</summary>
    private readonly record struct VideoThumbLayout(bool HasThumbnails, bool TwoRows, float LabelMinX, float LabelMaxX);

    /// <summary>
    /// Draws thumbnails of the source's first and last visible frame inside the clip body. Flat layers show
    /// them at full clip height with the label between them; on layers taller than two small-text rows the
    /// label gets its own top row and the thumbnails fill the rest. When the clip is too narrow for both,
    /// the end thumbnail is drawn first (behind) and fades out with growing overlap.
    /// </summary>
    private static VideoThumbLayout DrawVideoClipThumbnails(ref ClipDrawingAttributes attr, TimeClip timeClip, Instance clipInstance,
                                                            Vector2 position, Vector2 itemRectMax, float fade, Color bodyColor)
    {
        var clipHeight = itemRectMax.Y - position.Y;
        var twoRows = clipHeight > 3f * Fonts.FontSmall.FontSize;
        var thumbTop = twoRows ? position.Y + Fonts.FontSmall.FontSize + 2 : position.Y + 1;
        var thumbHeight = itemRectMax.Y - 1 - thumbTop;
        if (thumbHeight < 8 * T3Ui.UiScaleFactor)
            return default;

        var thumbWidth = thumbHeight * UiHelpers.Thumbnails.ThumbnailManager.AspectRatio;
        var innerWidth = itemRectMax.X - position.X - 2;
        if (innerWidth < thumbWidth * 0.3f)
            return default;

        if (clipInstance is not T3.Core.Operator.Interfaces.IDescriptiveFilename descriptive)
            return default;

        var assetPath = descriptive.SourcePathSlot.TypedInputValue.Value;
        if (string.IsNullOrEmpty(assetPath))
            return default;

        if (!VideoClipDurationCache.TryGetDurationSecs(assetPath, clipInstance, out var durationSecs))
            return default;

        var atlasSrv = UiHelpers.Thumbnails.ThumbnailManager.AtlasSrv;
        if (atlasSrv == null)
            return default;

        // While a drag/trim is running SourceRange changes every frame — don't flood the decode queue with
        // requests for transient times; already-ready thumbnails still draw.
        var allowRequest = attr.MoveClipsCommand == null;

        var playbackBpm = attr.LayerContext.TimeCanvas.Playback.Bpm;
        var startSecs = QuantizeThumbnailTime(Math.Clamp(timeClip.SourceToSeconds(timeClip.SourceRange.Start, playbackBpm), 0, durationSecs));
        var endSecs = QuantizeThumbnailTime(Math.Clamp(timeClip.SourceToSeconds(timeClip.SourceRange.End, playbackBpm), 0, durationSecs));

        var startMin = new Vector2(position.X + 1, thumbTop);
        var endMin = new Vector2(itemRectMax.X - 1 - thumbWidth, thumbTop);
        var thumbSize = new Vector2(thumbWidth, thumbHeight);

        // Once the clip is narrower than two thumbnails they overlap; the end thumbnail (drawn behind)
        // fades from 1 down to 0.2 as the overlap approaches a full thumbnail width.
        var overlap = 2 * thumbWidth - innerWidth;
        var endFade = overlap <= 0 ? 1f : 1f - 0.8f * Math.Clamp(overlap / thumbWidth, 0f, 1f);

        var drewStart = false;
        var drewEnd = false;

        attr.DrawList.PushClipRect(position, itemRectMax, true);

        if (VideoClipThumbnailCache.TryGetThumbnail(assetPath, clipInstance, endSecs, allowRequest, out var endRect))
        {
            attr.DrawList.AddImage(atlasSrv.NativePointer, endMin, endMin + thumbSize,
                                   endRect.UvMin, endRect.UvMax, Color.White.Fade(fade * endFade));
            DrawThumbnailBorder(attr.DrawList, endMin, endMin + thumbSize, bodyColor);
            drewEnd = true;
        }

        if (VideoClipThumbnailCache.TryGetThumbnail(assetPath, clipInstance, startSecs, allowRequest, out var startRect))
        {
            attr.DrawList.AddImage(atlasSrv.NativePointer, startMin, startMin + thumbSize,
                                   startRect.UvMin, startRect.UvMax, Color.White.Fade(fade));
            DrawThumbnailBorder(attr.DrawList, startMin, startMin + thumbSize, bodyColor);
            drewStart = true;
        }

        attr.DrawList.PopClipRect();

        if (!drewStart && !drewEnd)
            return default;

        return new VideoThumbLayout(true, twoRows,
                                    drewStart ? startMin.X + thumbWidth + 3 : position.X + 4,
                                    drewEnd ? endMin.X - 3 : itemRectMax.X - 3);
    }

    // Fakes rounded thumbnail corners: a 2px stroke in the clip's body color with a 3px radius over the
    // square image covers the corner pixels.
    private static void DrawThumbnailBorder(ImDrawListPtr drawList, Vector2 min, Vector2 max, Color bodyColor)
    {
        drawList.AddRect(min, max, bodyColor, 3 * T3Ui.UiScaleFactor, ImDrawFlags.RoundCornersAll, 2 * T3Ui.UiScaleFactor);
    }

    /// <summary>Shortens the label with a trailing ".." when it doesn't fit. Only allocates for
    /// clips whose title actually overflows.</summary>
    private static string TruncateLabel(string label, float fullWidth, float maxWidth)
    {
        if (fullWidth <= maxWidth || label.Length == 0)
            return label;

        var ellipsisWidth = ImGui.CalcTextSize("..").X;
        var low = 0;
        var high = label.Length - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(label[..mid]).X + ellipsisWidth <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }

        return low <= 0 ? string.Empty : label[..low] + "..";
    }

    private static double QuantizeThumbnailTime(double seconds) => Math.Round(seconds * 10) / 10;

    /// <summary>Source-time span of the clip's content, if known: media footage (0-based, from the
    /// per-asset duration caches) or the clip symbol's authored source extent
    /// (<see cref="TimelineState.SourceExtent"/>, may start non-zero). False for clips without either.</summary>
    private static bool TryGetContentExtent(ref ClipDrawingAttributes attr, TimeClip timeClip, Instance? clipInstance, out TimeRange contentExtent)
    {
        if (TryGetVideoFootageBars(ref attr, timeClip, clipInstance, out var footageBars))
        {
            contentExtent = new TimeRange(0, footageBars);
            return true;
        }

        if (clipInstance?.Symbol.GetSymbolUi()?.TimelineState?.SourceExtent is { } authoredExtent
            && authoredExtent.Duration > 0)
        {
            contentExtent = authoredExtent;
            return true;
        }

        contentExtent = default;
        return false;
    }

    /// <summary>Full source length of a video or audio clip in bars, or false (with -1) for other /
    /// unknown-duration clips. Duration is resolved through the per-asset duration caches (probed once).</summary>
    private static bool TryGetVideoFootageBars(ref ClipDrawingAttributes attr, TimeClip timeClip, Instance? clipInstance, out float footageBars)
    {
        footageBars = -1f;
        if (clipInstance is not T3.Core.Operator.Interfaces.IDescriptiveFilename describedFile)
            return false;

        var path = describedFile.SourcePathSlot.TypedInputValue.Value;
        if (string.IsNullOrEmpty(path) || !AssetType.TryGetForFilePath(path, out var assetType, out _))
            return false;

        double fullDurationSecs;
        switch (assetType.Name)
        {
            case "Video":
                if (!VideoClipDurationCache.TryGetDurationSecs(path, clipInstance, out fullDurationSecs))
                    return false;
                break;
            case "Audio":
                if (!AudioClipDurationCache.TryGetDurationSecs(path, clipInstance, out fullDurationSecs))
                    return false;
                break;
            default:
                return false;
        }

        if (fullDurationSecs <= 0)
            return false;

        footageBars = (float)timeClip.SecondsToSource(fullDurationSecs, attr.LayerContext.TimeCanvas.Playback.Bpm);
        return true;
    }

    /// <summary>Timeline positions (bars) where the clip's source reads the first and last frame of its media.
    /// These are stable while trimming (a slip-trim preserves speed), so they make good snap targets. False for
    /// non-video clips or a degenerate (zero-speed) clip.</summary>
    private static bool TryGetFootageBoundaryTimes(ref ClipDrawingAttributes attr, TimeClip timeClip,
                                                   out double footageStartTime, out double footageEndTime)
    {
        footageStartTime = 0;
        footageEndTime = 0;
        if (!attr.CompositionOp.Children.TryGetChildInstance(timeClip.Id, out var clipInstance)
            || !TryGetContentExtent(ref attr, timeClip, clipInstance, out var contentExtent))
            return false;

        var rate = timeClip.Speed;
        if (Math.Abs(rate) < 1e-6)
            return false;

        footageStartTime = timeClip.TimeRange.Start + (contentExtent.Start - timeClip.SourceRange.Start) / rate;
        footageEndTime = timeClip.TimeRange.Start + (contentExtent.End - timeClip.SourceRange.Start) / rate;
        return true;
    }

    // Arms the shared single-anchor attractor for one TryCheckForSnapping call, returning it as a reusable
    // one-element list so the snap check stays allocation-free during a trim drag.
    private static IValueSnapAttractor[] UseFootageSnapAnchor(double anchorTime)
    {
        _footageSnapAttractor.AnchorTime = anchorTime;
        return _footageSnapAttractorList;
    }

    private sealed class FootageSnapAttractor : IValueSnapAttractor
    {
        public double AnchorTime;
        public void CheckForSnap(ref SnapResult snapResult) => snapResult.TryToImproveWithAnchorValue(AnchorTime);
    }

    // [VideoClip] — media clips get their type color instead of the per-clip random hue.
    private static readonly Guid _videoClipSymbolId = new("04c1a6dc-3042-48a8-81d2-0a5a162016dc");

    private const float HandleWidth = 7;
    private static float _timeWithinDraggedClip;

    // Drag-start snapshots. Body drag computes the target position from these +
    // the absolute mouse-time delta, then derives the per-frame increment by subtracting
    // the cumulative delta already committed. Avoids drift across snap boundaries.
    private static double _dragStartMouseTime;
    private static double _originalDraggedClipStart;
    private static double _lastAppliedDeltaTime;
    private static readonly Vector2 _handleOffset = new(HandleWidth, 0);
    private static readonly Color _timeRemappingColor = UiColors.StatusAnimated.Fade(0.25f);
    private static float _posPosYOnDragStart;

    private static readonly FootageSnapAttractor _footageSnapAttractor = new();
    private static readonly IValueSnapAttractor[] _footageSnapAttractorList = [_footageSnapAttractor];
}