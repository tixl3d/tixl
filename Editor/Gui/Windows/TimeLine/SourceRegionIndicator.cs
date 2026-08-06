#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.TimeLine.TimeClips;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Draws the footage extent of the hovered or single-selected media clip as a region in the ruler, behind
/// the selection-range indicator, and implements the slip ("slide content") edit: dragging the region parts
/// outside the selection range shifts the clip's SourceRange while its timeline window and speed stay
/// fixed. Replaces the earlier on-clip footage outline, which was hard to read against neighboring clips.
/// </summary>
internal sealed class SourceRegionIndicator
{
    public SourceRegionIndicator(TimeLineCanvas canvas, ValueSnapHandler snapHandler)
    {
        _canvas = canvas;
        _snapHandler = snapHandler;
    }

    public void Draw(Instance composition, ImDrawListPtr drawList, bool hasRange,
                     float rangeStartX, float rangeEndX, float lineY,
                     Vector2 rulerPos, Vector2 rulerSize, float scale)
    {
        // While a slip drag runs the mouse leaves the clip, so the published hover info vanishes —
        // keep showing the dragged clip until release.
        TimeClip? clip;
        double footageBars;
        if (_dragClip != null)
        {
            clip = _dragClip;
            footageBars = _dragFootageBars;
        }
        else if (!MediaClipSourceRegion.TryGetCurrent(out clip, out footageBars))
        {
            return;
        }

        var rate = clip.Speed;
        if (Math.Abs(rate) < 1e-6 || footageBars <= 0)
            return;

        var footageStart = clip.TimeRange.Start - clip.SourceRange.Start / rate;
        var footageEnd = clip.TimeRange.Start + ((float)footageBars - clip.SourceRange.Start) / rate;
        if (footageEnd < footageStart)
            (footageStart, footageEnd) = (footageEnd, footageStart);

        var left = MathF.Max(_canvas.TransformX(footageStart), rulerPos.X);
        var right = MathF.Min(_canvas.TransformX(footageEnd), rulerPos.X + rulerSize.X);
        if (right - left < 1)
            return;

        var top = lineY - 3 * scale;
        var bottom = lineY + 4 * scale;
        drawList.AddRectFilled(new Vector2(left, top), new Vector2(right, bottom),
                               UiColors.BackgroundFull.Fade(0.5f), 2 * scale);
        drawList.AddRect(new Vector2(left, top), new Vector2(right, bottom),
                         UiColors.ForegroundFull.Fade(0.25f), 2 * scale);

        // Slip-drag zones: the region parts left and right of the selection range — or the whole region
        // when no range is shown.
        if (hasRange)
        {
            EmitSlipButton("##SourceRegionLeft", left, MathF.Min(rangeStartX, right), top, bottom, clip, footageBars, composition);
            EmitSlipButton("##SourceRegionRight", MathF.Max(rangeEndX, left), right, top, bottom, clip, footageBars, composition);
        }
        else
        {
            EmitSlipButton("##SourceRegion", left, right, top, bottom, clip, footageBars, composition);
        }
    }

    private void EmitSlipButton(string id, float xMin, float xMax, float top, float bottom,
                                TimeClip clip, double footageBars, Instance composition)
    {
        var width = xMax - xMin;
        if (width < 2)
            return;

        ImGui.SetCursorScreenPos(new Vector2(xMin, top));
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton(id, new Vector2(width, bottom - top));

        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (ImGui.IsItemActivated())
            StartDrag(clip, footageBars, composition);

        if (_dragClip == clip && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            UpdateDrag(clip);

        if (ImGui.IsItemDeactivated() && _dragClip == clip)
            CompleteDrag();
    }

    private void StartDrag(TimeClip clip, double footageBars, Instance composition)
    {
        _dragClip = clip;
        _dragFootageBars = footageBars;
        _dragRate = clip.Speed;
        _origSourceStart = clip.SourceRange.Start;
        _origSourceEnd = clip.SourceRange.End;
        _origFootageStart = clip.TimeRange.Start - _origSourceStart / _dragRate;
        _origFootageEnd = clip.TimeRange.Start + ((float)footageBars - _origSourceStart) / _dragRate;
        _pressU = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);

        _scratchClipList[0] = clip;
        _command = new MoveTimeClipsCommand(composition, _scratchClipList);
    }

    private void UpdateDrag(TimeClip clip)
    {
        FrameStats.Current.OpenedPopupCapturedMouse = true;
        var u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
        var d = (double)u - _pressU;

        // Snap the moved footage boundaries to time anchors (other clips, playhead, …); the stronger
        // snap wins, Shift bypasses.
        if (!ImGui.GetIO().KeyShift)
        {
            var bestDelta = 0.0;
            var hasSnap = false;
            if (_snapHandler.TryCheckForSnapping(_origFootageStart + d, out var snappedStart, _canvas.Scale.X))
            {
                bestDelta = snappedStart - (_origFootageStart + d);
                hasSnap = true;
            }

            if (_snapHandler.TryCheckForSnapping(_origFootageEnd + d, out var snappedEnd, _canvas.Scale.X))
            {
                var delta = snappedEnd - (_origFootageEnd + d);
                if (!hasSnap || Math.Abs(delta) < Math.Abs(bestDelta))
                    bestDelta = delta;
            }

            d += bestDelta;
        }

        clip.SourceRange.Start = (float)(_origSourceStart - d * _dragRate);
        clip.SourceRange.End = (float)(_origSourceEnd - d * _dragRate);
    }

    private void CompleteDrag()
    {
        if (_command != null)
        {
            _command.StoreCurrentValues();
            UndoRedoStack.Add(_command);
            _command = null;
        }

        _dragClip = null;
    }

    private readonly TimeLineCanvas _canvas;
    private readonly ValueSnapHandler _snapHandler;

    // Drag-scoped state; the TimeClip reference only lives for the duration of one slip drag.
    private TimeClip? _dragClip;
    private double _dragFootageBars;
    private float _dragRate;
    private float _origSourceStart;
    private float _origSourceEnd;
    private double _origFootageStart;
    private double _origFootageEnd;
    private double _pressU;
    private MoveTimeClipsCommand? _command;
    private readonly TimeClip[] _scratchClipList = new TimeClip[1];
}
