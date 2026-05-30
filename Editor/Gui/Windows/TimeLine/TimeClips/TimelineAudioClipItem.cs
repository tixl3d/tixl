#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ImGuiNET;
using SharpDX.Direct3D11;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Resource;
using T3.Editor.Gui.Audio;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Renderer + interaction handler for symbol-level <see cref="TimelineAudioClip"/>s in
/// <see cref="ClipArea"/>. These clips are not backed by a <c>SymbolChild</c>; they live in
/// <c>composition.Symbol.CompositionSettings.Playback.AudioClips</c> and are drawn
/// alongside op-backed <c>TimeClip</c>s by <c>TimeClipItem</c>.
///
/// Supports: click-select, multi-select (shift/ctrl), body-drag (move along time + layer),
/// start-/end-handle drag (trim), delete via the layers-area delete-key path. Drag state
/// is held on <see cref="AudioClipInteractions.ActiveMoveCommand"/> so it survives across frames.
/// </summary>
internal static class TimelineAudioClipItem
{
    public record struct DrawAttrs(
        ImRect LayerRect,
        int MinLayerIndex,
        ImDrawListPtr DrawList,
        HashSet<Guid> SelectedAudioClipIds,
        TimeLineCanvas TimeCanvas,
        Instance Composition,
        TimeClipInteractions OpInteractions,
        ValueSnapHandler SnapHandler);

    internal static void DrawClip(TimelineAudioClip clip, ref DrawAttrs attr)
    {
        // Compute the clip's right edge. When TimeRange.End is the "no explicit end"
        // sentinel (End <= Start), derive it from LengthInSeconds at the current BPM
        // so the body width matches what the engine actually plays.
        var hasExplicitEnd = clip.TimeRange.End > clip.TimeRange.Start;
        var endBars = hasExplicitEnd
                          ? clip.TimeRange.End
                          : clip.TimeRange.Start
                            + (float)attr.TimeCanvas.Playback.BarsFromSeconds(clip.LengthInSeconds);

        var xStart = attr.TimeCanvas.TransformX(clip.TimeRange.Start) + 1;
        var xEnd = attr.TimeCanvas.TransformX(endBars) + 1;
        var clipWidth = xEnd - xStart;
        // Clamp to a minimum visible width so a clip dragged to MinDuration stays
        // visible + clickable. The clip data itself isn't clamped here.
        if (clipWidth < 1)
            clipWidth = 1;

        var showHandles = clipWidth > 4 * HandleWidth;
        var bodyWidth = showHandles ? clipWidth - 2 * HandleWidth : clipWidth;

        var pos = new Vector2(xStart,
                              attr.LayerRect.Min.Y
                              + (clip.LayerIndex - attr.MinLayerIndex) * ClipArea.LayerHeight);
        var clipSize = new Vector2(clipWidth, ClipArea.LayerHeight - 2);
        var bodySize = new Vector2(bodyWidth, ClipArea.LayerHeight - 2);
        var maxPos = pos + clipSize;

        const float rounding = 4.5f;
        var isSelected = attr.SelectedAudioClipIds.Contains(clip.Id);

        // Mute fade — silenced clips render at ~40 % opacity so the user can tell at a
        // glance which clips won't be heard. Applied to fill, waveform image and label;
        // selection border + audio icon stay full opacity so the clip is still findable.
        var muteFade = clip.IsMuted ? 0.4f : 1f;

        // Fixed audio-tinted fill — distinct from the random-per-id colour of op-backed clips.
        var fill = UiColors.ColorForValues.Fade(0.4f * muteFade);
        attr.DrawList.AddRectFilled(pos, maxPos, fill, rounding);

        // Waveform image (lazy load + cache). Map UVs to the audible source portion so
        // start-trim (SourceOffsetSecs > 0) reveals the later part of the source and
        // end-trim (shorter clip duration) crops to the remaining tail.
        if (TryGetWaveformSrv(clip, attr.Composition, out var srv))
        {
            var lengthSecs = clip.LengthInSeconds;
            Vector2 uvMin, uvMax;
            if (lengthSecs > 0)
            {
                var clipDurationBars = endBars - clip.TimeRange.Start;
                var clipDurationSecs = attr.TimeCanvas.Playback.SecondsFromBars(clipDurationBars);
                var audibleEndSecs = clip.SourceOffsetSecs + clipDurationSecs;
                uvMin = new Vector2((float)(clip.SourceOffsetSecs / lengthSecs), 0);
                uvMax = new Vector2(Math.Min(1f, (float)(audibleEndSecs / lengthSecs)), 1);
            }
            else
            {
                // Length not yet populated by BASS — fall back to full-image stretch until loaded.
                uvMin = Vector2.Zero;
                uvMax = Vector2.One;
            }

            attr.DrawList.PushClipRect(pos, maxPos, true);
            attr.DrawList.AddImage((IntPtr)srv,
                                   pos + new Vector2(1, 1),
                                   maxPos - new Vector2(1, 1),
                                   uvMin,
                                   uvMax,
                                   UiColors.ForegroundFull.Fade(0.6f * muteFade));
            attr.DrawList.PopClipRect();
        }

        if (isSelected)
            attr.DrawList.AddRect(pos, maxPos, UiColors.Selection, rounding);

        // Audio icon in top-left.
        var iconPos = pos + new Vector2(3, 1) * T3Ui.UiScaleFactor;
        Icons.DrawIconAtScreenPosition(Icon.FileAudio, iconPos, attr.DrawList, UiColors.ForegroundFull);

        // Label — falls back to "(recording…)" while the AssetPath isn't set yet (an
        // in-progress recording owns its TimelineAudioClip entry with an empty path
        // until Stop finalises it).
        if (ClipArea.LayerHeight > Fonts.FontSmall.FontSize)
        {
            var label = string.IsNullOrEmpty(clip.AssetPath)
                            ? "(recording…)"
                            : Path.GetFileNameWithoutExtension(clip.AssetPath);
            ImGui.PushFont(Fonts.FontSmall);
            var labelSize = ImGui.CalcTextSize(label);
            var labelPos = pos + new Vector2(18, 1) * T3Ui.UiScaleFactor;
            var needsClipping = labelSize.X + 18 * T3Ui.UiScaleFactor > clipSize.X;
            if (needsClipping)
                ImGui.PushClipRect(pos, maxPos - new Vector2(3, 0), true);

            var labelColor = isSelected
                                 ? UiColors.Selection
                                 : UiColors.ForegroundFull.Fade(muteFade);
            attr.DrawList.AddText(labelPos, labelColor, label);

            if (needsClipping)
                ImGui.PopClipRect();
            ImGui.PopFont();
        }

        ImGui.PushID(clip.Id.GetHashCode());

        // Body button (between handles when wide enough).
        var bodyPos = showHandles ? pos + new Vector2(HandleWidth, 0) : pos;
        ImGui.SetCursorScreenPos(bodyPos);
        var bodyClicked = ImGui.InvisibleButton("body", bodySize);

        // Tooltip on body hover
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
            ImGui.BeginTooltip();
            {
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.TextUnformatted(Path.GetFileName(clip.AssetPath));
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextUnformatted(clip.AssetPath);
                if (clip.LengthInSeconds > 0)
                    ImGui.TextUnformatted($"Duration: {clip.LengthInSeconds:0.00}s");
                ImGui.TextUnformatted($"Volume: {clip.Volume:0.00}");
                if (clip.IsMainSoundtrack)
                    ImGui.TextUnformatted("(main soundtrack — also drawn as timeline background)");
                ImGui.PopStyleColor();
                ImGui.PopFont();
            }
            ImGui.EndTooltip();
            ImGui.PopStyleVar();
        }

        HandleDrag(clip, ref attr, DragMode.Body, bodyClicked);

        // Always submit start/end handle buttons (even at 1x1 px when the clip is too
        // narrow to show them) so a trim drag-in-progress survives the clip shrinking.
        // Skipping the InvisibleButton call mid-drag clears ImGui's active-item state
        // for the handle and the drag dies abruptly. Same pattern as TimeClipItem.
        var handleSize = showHandles ? new Vector2(HandleWidth, ClipArea.LayerHeight) : Vector2.One;

        // Start handle
        ImGui.SetCursorScreenPos(pos);
        var startClicked = ImGui.InvisibleButton("startHandle", handleSize);
        if (showHandles && (ImGui.IsItemHovered() || ImGui.IsItemActive()))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            attr.DrawList.AddRectFilled(ImGui.GetItemRectMin() + new Vector2(2, 3),
                                        ImGui.GetItemRectMax() - new Vector2(1, 4),
                                        UiColors.ForegroundFull.Fade(0.3f),
                                        5);
        }
        HandleDrag(clip, ref attr, DragMode.Start, startClicked);

        // End handle
        ImGui.SetCursorScreenPos(pos + new Vector2(bodyWidth + HandleWidth, 0));
        var endClicked = ImGui.InvisibleButton("endHandle", handleSize);
        if (showHandles && (ImGui.IsItemHovered() || ImGui.IsItemActive()))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            attr.DrawList.AddRectFilled(ImGui.GetItemRectMin() + new Vector2(0, 3),
                                        ImGui.GetItemRectMax() - new Vector2(3, 4),
                                        UiColors.ForegroundFull.Fade(0.3f),
                                        5);
        }
        HandleDrag(clip, ref attr, DragMode.End, endClicked);

        ImGui.PopID();
    }

    private enum DragMode
    {
        Body,
        Start,
        End,
    }

    private static void HandleDrag(TimelineAudioClip clip, ref DrawAttrs attr, DragMode mode, bool wasClickRelease)
    {
        var isActive = ImGui.IsItemActive();
        var isDeactivated = ImGui.IsItemDeactivated();

        // Handle plain click (release without drag): manage selection. Plain click clears
        // op-clip selection too so the two clip types share one unified selection from the
        // user's POV — Shift/Ctrl preserves both sides for additive selection.
        if (wasClickRelease)
        {
            var io = ImGui.GetIO();
            if (io.KeyShift || io.KeyCtrl)
            {
                if (!attr.SelectedAudioClipIds.Add(clip.Id))
                    attr.SelectedAudioClipIds.Remove(clip.Id);
            }
            else
            {
                attr.SelectedAudioClipIds.Clear();
                attr.OpInteractions.ClearSelection();
                attr.SelectedAudioClipIds.Add(clip.Id);
            }
        }

        if (!isActive && !isDeactivated)
            return;

        // First active frame for this drag: ensure the clicked clip is in the selection
        // and construct the move command capturing original values.
        if (isActive && AudioClipInteractions.ActiveMoveCommand == null)
        {
            var io = ImGui.GetIO();
            if (!attr.SelectedAudioClipIds.Contains(clip.Id))
            {
                if (!io.KeyShift && !io.KeyCtrl)
                {
                    attr.SelectedAudioClipIds.Clear();
                    attr.OpInteractions.ClearSelection();
                }
                attr.SelectedAudioClipIds.Add(clip.Id);
            }

            var allClips = attr.Composition.Symbol.CompositionSettings.Playback.AudioClips;
            var selected = new List<TimelineAudioClip>();
            foreach (var c in allClips)
            {
                if (attr.SelectedAudioClipIds.Contains(c.Id))
                    selected.Add(c);
            }
            if (selected.Count > 0)
            {
                AudioClipInteractions.ActiveMoveCommand = new MoveTimelineAudioClipsCommand(attr.Composition, selected);
                AudioClipInteractions.LayerShiftOnDragStart = 0;
            }

            // Snapshot the dragged clip's original edges + mouse time so the per-frame
            // drag target is computed from absolute drag-start state, not by accumulating
            // per-frame deltas. The latter pattern lets snap absorb a delta and trap
            // the clip at the snap target — the mouse keeps moving but the per-frame
            // post-snap timeDelta is zero, so the clip never escapes.
            _dragStartMouseTime = attr.TimeCanvas.InverseTransformX(ImGui.GetIO().MousePos.X);
            _originalDraggedClipStart = clip.TimeRange.Start;
            _originalDraggedClipEnd = clip.TimeRange.End;
            _lastAppliedTimeDelta = 0;

            // Cross-type drag: when the user drags an audio clip body, op clips in
            // the op selection move along too.
            if (mode == DragMode.Body)
                attr.OpInteractions.StartCrossDrag();
        }

        if (!ImGui.IsMouseDragging(0, UserSettings.Config.ClickThreshold))
        {
            if (isDeactivated)
                CompleteDrag(ref attr);
            return;
        }

        var playback = attr.TimeCanvas.Playback;

        // Absolute drag target from the drag-start snapshot. Mirrors the body-drag pattern
        // in TimeClipItem: compute target = original + (currentMouseTime - dragStartMouseTime),
        // snap that target, then emit just the increment beyond what's already been applied.
        // Per-frame delta + snap (the old shape) lets snap absorb a delta and trap the clip
        // at the snap anchor — the mouse keeps moving but timeDelta goes to zero so the
        // clip never escapes.
        var currentMouseTime = (float)attr.TimeCanvas.InverseTransformX(ImGui.GetIO().MousePos.X);
        var rawDelta = currentMouseTime - _dragStartMouseTime;

        var allowSnapping = !ImGui.GetIO().KeyShift && !(ImGui.GetIO().KeyAlt && ImGui.GetIO().KeyCtrl);
        var snapScale = attr.TimeCanvas.Scale.X;
        var snapExclusions = attr.TimeCanvas.SelectionDragSnapExclusions;

        float finalDelta;
        switch (mode)
        {
            case DragMode.Body:
            {
                var unsnappedTargetStart = _originalDraggedClipStart + rawDelta;
                var unsnappedTargetEnd = _originalDraggedClipEnd + rawDelta;
                var targetDelta = rawDelta;
                if (allowSnapping)
                {
                    if (attr.SnapHandler.TryCheckForSnapping(unsnappedTargetStart, out var snapped, snapScale, snapExclusions))
                        targetDelta = (float)snapped - _originalDraggedClipStart;
                    else if (attr.SnapHandler.TryCheckForSnapping(unsnappedTargetEnd, out snapped, snapScale, snapExclusions))
                        targetDelta = (float)snapped - _originalDraggedClipEnd;
                }
                finalDelta = targetDelta;
                break;
            }
            case DragMode.Start:
            {
                var unsnappedTarget = _originalDraggedClipStart + rawDelta;
                var snappedTarget = unsnappedTarget;
                if (allowSnapping && attr.SnapHandler.TryCheckForSnapping(unsnappedTarget, out var snapped, snapScale, snapExclusions))
                    snappedTarget = (float)snapped;
                finalDelta = snappedTarget - _originalDraggedClipStart;
                break;
            }
            case DragMode.End:
            {
                var unsnappedTarget = _originalDraggedClipEnd + rawDelta;
                var snappedTarget = unsnappedTarget;
                if (allowSnapping && attr.SnapHandler.TryCheckForSnapping(unsnappedTarget, out var snapped, snapScale, snapExclusions))
                    snappedTarget = (float)snapped;
                finalDelta = snappedTarget - _originalDraggedClipEnd;
                break;
            }
            default:
                finalDelta = 0;
                break;
        }

        var timeDelta = finalDelta - _lastAppliedTimeDelta;
        _lastAppliedTimeDelta = finalDelta;

        // Vertical layer shift (Body drag only) — quantise cumulative-from-drag-start mouse Y
        // into integer LayerIndex deltas and ratchet via LayerShiftOnDragStart so each frame
        // applies only the incremental shift.
        var layerIndexDelta = 0;
        if (mode == DragMode.Body)
        {
            var cumulativeDragY = ImGui.GetMouseDragDelta(0).Y;
            var currentLayerShift = (int)Math.Round(cumulativeDragY / ClipArea.LayerHeight);
            layerIndexDelta = currentLayerShift - AudioClipInteractions.LayerShiftOnDragStart;
            if (layerIndexDelta != 0)
                AudioClipInteractions.LayerShiftOnDragStart = currentLayerShift;
        }

        var allClipsRef = attr.Composition.Symbol.CompositionSettings.Playback.AudioClips;
        foreach (var c in allClipsRef)
        {
            if (!attr.SelectedAudioClipIds.Contains(c.Id))
                continue;

            // Effective end-in-bars: if TimeRange.End is the sentinel (End <= Start), derive
            // from LengthInSeconds. Trim operations need this to clamp correctly; otherwise
            // End - MinDuration sits below Start and the clamp snaps the dragged edge to a
            // nonsense value, making the clip "vanish."
            var hasExplicitEnd = c.TimeRange.End > c.TimeRange.Start;
            var effectiveEnd = hasExplicitEnd
                                   ? c.TimeRange.End
                                   : c.TimeRange.Start + (float)playback.BarsFromSeconds(c.LengthInSeconds);

            switch (mode)
            {
                case DragMode.Body:
                    // Preserve sentinel-state if the clip had one (offsetting both by the same
                    // delta keeps End - Start invariant, so End stays at-or-below Start).
                    c.TimeRange = new TimeRange(
                        c.TimeRange.Start + timeDelta,
                        c.TimeRange.End + timeDelta);
                    if (layerIndexDelta != 0)
                        c.LayerIndex += layerIndexDelta;
                    break;

                case DragMode.Start:
                {
                    // Compute desired source-offset shift first; clamp at 0 so we can't reveal
                    // audio before the source begins. The clip's TimeRange.Start is then
                    // derived from the *actual* (possibly clamped) trim amount — so once the
                    // offset hits 0, further leftward drag does nothing instead of extending
                    // the clip into silence territory (which would stretch the waveform).
                    var desiredTrimSecs = playback.SecondsFromBars(timeDelta);
                    var clampedOffset = Math.Max(0, c.SourceOffsetSecs + desiredTrimSecs);
                    var actualTrimSecs = clampedOffset - c.SourceOffsetSecs;
                    var actualTrimBars = (float)playback.BarsFromSeconds(actualTrimSecs);

                    var newStart = c.TimeRange.Start + actualTrimBars;
                    newStart = Math.Min(newStart, effectiveEnd - MinDuration);

                    // Materialise the End if it was a sentinel — the user is now defining
                    // explicit bounds via trim, so writing the resolved value preserves the
                    // visible width.
                    var endToWrite = hasExplicitEnd ? c.TimeRange.End : effectiveEnd;
                    c.TimeRange = new TimeRange(newStart, endToWrite);
                    c.SourceOffsetSecs = clampedOffset;
                    break;
                }

                case DragMode.End:
                {
                    var newEnd = Math.Max(effectiveEnd + timeDelta, c.TimeRange.Start + MinDuration);

                    // Upper clamp: clip body can't extend past where source content ends, so
                    // we avoid creating silence-territory on the right. For an already-stretched
                    // clip the ceiling is "soft" — it tracks max(natural, current), letting the
                    // user trim leftward back into the natural range and the ceiling tightens
                    // automatically.
                    if (c.LengthInSeconds > 0)
                    {
                        var sourceEndSecs = c.SourceDurationSecs > 0
                                                ? c.SourceOffsetSecs + c.SourceDurationSecs
                                                : c.LengthInSeconds;
                        var remainingSourceSecs = sourceEndSecs - c.SourceOffsetSecs;
                        var naturalMaxEnd = c.TimeRange.Start + (float)playback.BarsFromSeconds(remainingSourceSecs);
                        var ceiling = Math.Max(naturalMaxEnd, effectiveEnd);
                        newEnd = Math.Min(newEnd, ceiling);
                    }

                    c.TimeRange = new TimeRange(c.TimeRange.Start, newEnd);
                    break;
                }
            }
        }

        // Cross-type drag: propagate the body drag delta to op clips in the op
        // selection so a mixed selection moves together. Only Body mode — handle
        // trims stay per-clip-type.
        if (mode == DragMode.Body)
            attr.OpInteractions.ApplyDragDelta(timeDelta, layerIndexDelta);

        if (isDeactivated)
            CompleteDrag(ref attr);
    }

    private static void CompleteDrag(ref DrawAttrs attr)
    {
        if (AudioClipInteractions.ActiveMoveCommand != null)
        {
            AudioClipInteractions.ActiveMoveCommand.StoreCurrentValues();
            UndoRedoStack.Add(AudioClipInteractions.ActiveMoveCommand);
            AudioClipInteractions.ActiveMoveCommand = null;
        }
        // Cross-type drag: push the op-side move command if one was built.
        attr.OpInteractions.CompleteCrossDrag();
    }

    /// <summary>
    /// Returns a (cached) shader-resource view of the clip's waveform image. First call per
    /// asset kicks off a background generation via <see cref="AudioImageFactory"/>; subsequent
    /// calls return the loaded SRV from a static dictionary keyed by image path.
    /// </summary>
    private static bool TryGetWaveformSrv(TimelineAudioClip clip, Instance composition,
                                          [NotNullWhen(true)] out ShaderResourceView? srv)
    {
        srv = null;
        var handle = new AudioClipResourceHandle(clip, composition);
        if (!AudioImageFactory.TryGetOrCreateImagePathForClip(handle, out var imagePath))
            return false;

        if (_imageCache.TryGetValue(imagePath, out var entry))
        {
            srv = entry.Srv;
            return srv is { IsDisposed: false };
        }

        var resource = ResourceManager.CreateTextureResource(imagePath, composition);
        ShaderResourceView? newSrv = null;
        resource.Value?.CreateShaderResourceView(ref newSrv, imagePath);

        _imageCache[imagePath] = new CachedImage(resource, newSrv);
        srv = newSrv;
        return srv is { IsDisposed: false };
    }

    private readonly record struct CachedImage(Resource<Texture2D> Resource, ShaderResourceView? Srv);

    private const float HandleWidth = 7;
    private const float MinDuration = 1f / 60f; // bars

    // Drag-start snapshot — clip edges + mouse time at the moment the drag began. Body /
    // Start / End drags compute their target from these + the current mouse-time delta,
    // then derive the per-frame increment by subtracting the cumulative delta already
    // committed. Avoids snap-trap behaviour when the mouse moves slowly across a snap
    // anchor.
    private static float _dragStartMouseTime;
    private static float _originalDraggedClipStart;
    private static float _originalDraggedClipEnd;
    private static float _lastAppliedTimeDelta;

    // Lifetime: entries live for the editor session.
    private static readonly Dictionary<string, CachedImage> _imageCache = new();
}
