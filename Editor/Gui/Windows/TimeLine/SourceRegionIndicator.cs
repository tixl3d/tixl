#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
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
    public SourceRegionIndicator(TimeLineCanvas canvas)
    {
        _canvas = canvas;
    }

    public void Draw(Instance composition, ImDrawListPtr drawList, bool hasRange,
                     float rangeStartX, float rangeEndX, float lineY,
                     Vector2 rulerPos, Vector2 rulerSize, float scale)
    {
        // While a slip drag runs the mouse leaves the clip, so the published hover info vanishes —
        // keep showing the dragged clip until release.
        TimeClip? clip;
        TimeRange contentExtent;
        if (_dragClip != null)
        {
            clip = _dragClip;
            contentExtent = _dragContentExtent;
        }
        else if (!MediaClipSourceRegion.TryGetCurrent(out clip, out contentExtent))
        {
            return;
        }

        var rate = clip.Speed;
        if (Math.Abs(rate) < 1e-6 || contentExtent.Duration <= 0)
            return;

        var footageStart = clip.TimeRange.Start + (contentExtent.Start - clip.SourceRange.Start) / rate;
        var footageEnd = clip.TimeRange.Start + (contentExtent.End - clip.SourceRange.Start) / rate;
        if (footageEnd < footageStart)
            (footageStart, footageEnd) = (footageEnd, footageStart);

        var left = MathF.Max(_canvas.TransformX(footageStart), rulerPos.X);
        var right = MathF.Min(_canvas.TransformX(footageEnd), rulerPos.X + rulerSize.X);
        if (right - left < 1)
            return;

        // Taller than the SRI's hit band so the region stays grabbable above the selection range,
        // and flush with the ruler's bottom edge — a gap there makes the mouse cursor flicker
        // through ruler/region/keyset states while moving vertically.
        var top = lineY - 9 * scale;
        var bottom = rulerPos.Y + rulerSize.Y;

        // Slip-drag zones: left and right of the selection range, plus the strip above the SRI's hit
        // band — or the whole region when no range is shown. Emitted before drawing so the outline can
        // respond to hover.
        _anyZoneHovered = false;
        if (_dragClip != null && _dragZoneId != null)
        {
            // While dragging, keep ONE stable button alive under the id the drag started with: the zone
            // rects move (and can collapse below the min width) as the slip changes SourceRange — e.g.
            // the moment a boundary snaps — and a skipped emit drops ImGui's active id, cancelling the drag.
            EmitSlipButton(_dragZoneId, left, MathF.Max(right, left + 4), top, bottom, clip, contentExtent, composition);
        }
        else if (hasRange)
        {
            var sriHitTop = lineY - 2 * scale;
            EmitSlipButton("##SourceRegionLeft", left, MathF.Min(rangeStartX, right), top, bottom, clip, contentExtent, composition);
            EmitSlipButton("##SourceRegionRight", MathF.Max(rangeEndX, left), right, top, bottom, clip, contentExtent, composition);
            EmitSlipButton("##SourceRegionTop", MathF.Max(rangeStartX, left), MathF.Min(rangeEndX, right), top, sriHitTop, clip, contentExtent, composition);
        }
        else
        {
            EmitSlipButton("##SourceRegion", left, right, top, bottom, clip, contentExtent, composition);
        }

        var outlineFade = _anyZoneHovered || _dragClip != null ? 0.25f : 0.17f;
        drawList.AddRectFilled(new Vector2(left, top), new Vector2(right, bottom),
                               UiColors.BackgroundFull.Fade(0.5f), 2 * scale);
        drawList.AddRect(new Vector2(left, top), new Vector2(right, bottom),
                         UiColors.ForegroundFull.Fade(outlineFade), 2 * scale);
    }

    private void EmitSlipButton(string id, float xMin, float xMax, float top, float bottom,
                                TimeClip clip, TimeRange contentExtent, Instance composition)
    {
        var width = xMax - xMin;
        if (width < 2)
            return;

        ImGui.SetCursorScreenPos(new Vector2(xMin, top));
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton(id, new Vector2(width, bottom - top));

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            _anyZoneHovered = true;
        }

        if (ImGui.IsItemActivated())
        {
            _dragZoneId = id;
            StartDrag(clip, contentExtent, composition);
        }

        if (_dragClip == clip && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            UpdateDrag(clip);

        if (ImGui.IsItemDeactivated() && _dragClip == clip)
            CompleteDrag();
    }

    private void StartDrag(TimeClip clip, TimeRange contentExtent, Instance composition)
    {
        _dragClip = clip;
        _dragContentExtent = contentExtent;
        _dragRate = clip.Speed;
        _origSourceStart = clip.SourceRange.Start;
        _origSourceEnd = clip.SourceRange.End;
        _origFootageStart = clip.TimeRange.Start + (contentExtent.Start - _origSourceStart) / _dragRate;
        _origFootageEnd = clip.TimeRange.Start + (contentExtent.End - _origSourceStart) / _dragRate;
        _pressU = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);

        _scratchClipList[0] = clip;
        _command = new MoveTimeClipsCommand(composition, _scratchClipList);
    }

    private void UpdateDrag(TimeClip clip)
    {
        FrameStats.Current.OpenedPopupCapturedMouse = true;
        var u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
        var d = (double)u - _pressU;

        // Slipping only snaps to the clip's own boundaries — footage start onto clip start (source
        // begins at the content extent's start) and footage end onto clip end. Raster / other-element anchors proved unhelpful
        // for slides: the content has no meaningful relation to them. Shift bypasses.
        if (!ImGui.GetIO().KeyShift)
        {
            var thresholdBars = 6 * T3Ui.UiScaleFactor / _canvas.Scale.X;
            var deltaToClipStart = clip.TimeRange.Start - (_origFootageStart + d);
            var deltaToClipEnd = clip.TimeRange.End - (_origFootageEnd + d);
            var bestDelta = Math.Abs(deltaToClipStart) <= Math.Abs(deltaToClipEnd)
                                ? deltaToClipStart
                                : deltaToClipEnd;
            if (Math.Abs(bestDelta) < thresholdBars)
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
        _dragZoneId = null;
    }

    private readonly TimeLineCanvas _canvas;

    // Drag-scoped state; the TimeClip reference only lives for the duration of one slip drag.
    private TimeClip? _dragClip;
    private string? _dragZoneId;
    private TimeRange _dragContentExtent;
    private float _dragRate;
    private float _origSourceStart;
    private float _origSourceEnd;
    private double _origFootageStart;
    private double _origFootageEnd;
    private double _pressU;
    private bool _anyZoneHovered;
    private MoveTimeClipsCommand? _command;
    private readonly TimeClip[] _scratchClipList = new TimeClip[1];
}
