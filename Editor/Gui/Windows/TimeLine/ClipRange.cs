#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using Color = T3.Core.DataTypes.Vector.Color;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Shades everything outside the window in which the entered clip instance plays. The timeline canvas is
/// in playback time, while the clip's SourceRange is in the composition's local time — so the range is
/// mapped through the composition's time-clip chain. In playback time the SourceRange lands exactly on
/// the clip's TimeRange; shading therefore means "this instance is not playing here".
/// Bottom-aligned handles on the window edges edit the entered instance, matching the clip edges in the parent
/// timeline: drag trims, Alt + drag stretches (changes the clip's speed). The symbol's authored source extent
/// (<see cref="SourceExtentEditor"/>) is the template-side counterpart. The edges stay registered as snap anchors.
/// </summary>
internal sealed class ClipRange : IValueSnapAttractor
{
    public void Draw(TimeLineCanvas canvas, Instance composition, TimeClip timeClip, ImDrawListPtr drawlist)
    {
        if (timeClip == null)
            return;

        var mapping = TimeLineCanvas.ParamTimeMapping.For(composition);
        var windowStart = mapping.ToGlobal(timeClip.SourceRange.Start);
        var windowEnd = mapping.ToGlobal(timeClip.SourceRange.End);
        if (windowEnd < windowStart)
            (windowStart, windowEnd) = (windowEnd, windowStart);

        _windowStart = windowStart;
        _windowEnd = windowEnd;
        _lastDrawFrame = ImGui.GetFrameCount();

        // Shade outside — the shading edge itself marks the window; the grab handles sit at the bottom.
        var xStart = canvas.TransformX((float)windowStart);
        var xEnd = canvas.TransformX((float)windowEnd);
        var windowMaxX = ImGui.GetContentRegionAvail().X + canvas.WindowPos.X;

        drawlist.AddRectFilled(new Vector2(0, 0), new Vector2(xStart, OutsideHeight), _timeRangeOutsideColor);
        if (xEnd < windowMaxX)
            drawlist.AddRectFilled(new Vector2(xEnd, 0), new Vector2(windowMaxX, OutsideHeight), _timeRangeOutsideColor);
    }

    /// <summary>
    /// Emits the trim/stretch handles on the window edges into the current (dope-sheet) window. Call after
    /// keyframes and clips are emitted — those win overlapping hovers, so a key sitting on the edge stays
    /// clickable — and before the selection fence, which then won't start on a handle.
    /// </summary>
    public void DrawEdgeHandles(TimeLineCanvas canvas, Instance composition, TimeClip timeClip)
    {
        var parent = composition.Parent;
        if (timeClip == null || parent == null)
            return;

        // TimeRange lives in the parent's local time; this mapping stays fixed while the clip is edited.
        var parentMapping = TimeLineCanvas.ParamTimeMapping.For(parent);
        if (Math.Abs(parentMapping.Rate) < 1e-9)
            return;

        // Bottom-aligned handles shaped like the source extent handles in the ruler. The hit rect ends at the
        // content edge (inside the window padding) so it doesn't grow the window's content size.
        var scale = T3Ui.UiScaleFactor;
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var bottom = windowPos.Y + windowSize.Y - ImGui.GetStyle().WindowPadding.Y - 1;
        var top = bottom - HandleHeight * scale;
        if (top < windowPos.Y)
            return;

        var cursorBefore = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        EmitEdge("##clipWindowStart", canvas, composition, timeClip, parentMapping, isStart: true, top, bottom, scale, drawList);
        EmitEdge("##clipWindowEnd", canvas, composition, timeClip, parentMapping, isStart: false, top, bottom, scale, drawList);

        ImGui.SetCursorScreenPos(cursorBefore);
    }

    private void EmitEdge(string id, TimeLineCanvas canvas, Instance composition, TimeClip timeClip,
                          in TimeLineCanvas.ParamTimeMapping parentMapping, bool isStart,
                          float top, float bottom, float scale, ImDrawListPtr drawList)
    {
        var edgeTime = isStart ? timeClip.TimeRange.Start : timeClip.TimeRange.End;
        var x = canvas.TransformX((float)parentMapping.ToGlobal(edgeTime));
        var hitHalfWidth = 5 * scale;

        ImGui.SetCursorScreenPos(new Vector2(x - hitHalfWidth, top));
        ImGui.InvisibleButton(id, new Vector2(hitHalfWidth * 2, bottom - top));

        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        if (hovered || active)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        if (ImGui.IsItemActivated())
        {
            _dragTimeRangeOrg = timeClip.TimeRange;
            _dragSourceRangeOrg = timeClip.SourceRange;
            _scratchClipList[0] = timeClip;
            _command = new MoveTimeClipsCommand(composition.Parent!, _scratchClipList);
            _wasStretched = false;
        }

        if (active && _command != null && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            canvas.RequestEdgeScrolling();
            UpdateDrag(canvas, timeClip, parentMapping, isStart);
        }

        // Toasts on release — while dragging, attention is on the clip, not the menu bar.
        if (ImGui.IsItemDeactivated() && _command != null)
        {
            if (timeClip.TimeRange.Start != _dragTimeRangeOrg.Start || timeClip.TimeRange.End != _dragTimeRangeOrg.End
                || timeClip.SourceRange.Start != _dragSourceRangeOrg.Start || timeClip.SourceRange.End != _dragSourceRangeOrg.End)
            {
                _command.StoreCurrentValues();
                UndoRedoStack.Add(_command);

                if (_wasStretched)
                    ShowSpeedToast(timeClip);
                else
                    ActionToast.ShowTip(StretchTipId, "{Trimmed this clip}   hold [Alt] to stretch");
            }

            _command = null;
        }

        // Same visual as the source extent handles.
        var handleHalfWidth = 1.5f * scale;
        drawList.AddRectFilled(new Vector2(x - handleHalfWidth, top),
                               new Vector2(x + handleHalfWidth, bottom),
                               UiColors.ForegroundFull.Fade(hovered || active ? 1f : 0.8f), 1 * scale);
    }

    /// <summary>
    /// Mirrors the parent timeline's clip-edge drag, computed absolutely from the drag-start ranges so the
    /// result doesn't drift: trim moves the edge and its source point together at the clip's rate; with Alt
    /// only the edge moves, so the clip's speed changes.
    /// </summary>
    private void UpdateDrag(TimeLineCanvas canvas, TimeClip timeClip, in TimeLineCanvas.ParamTimeMapping parentMapping, bool isStart)
    {
        double u = canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
        if (!ImGui.GetIO().KeyShift
            && canvas.SnapHandlerForU.TryCheckForSnapping(u, out var snapped, canvas.Scale.X, _selfExclusion))
        {
            u = snapped;
        }

        var t = (float)parentMapping.ToLocal(u);
        var stretch = ImGui.GetIO().KeyAlt;
        if (stretch && !_wasStretched)
        {
            _wasStretched = true;
            ActionToast.MarkLearned(StretchTipId);
        }
        var orgT = _dragTimeRangeOrg;
        var orgS = _dragSourceRangeOrg;
        var rate = Math.Abs(orgT.Duration) < 0.0001f ? 1f : orgS.Duration / orgT.Duration;

        var timeRange = orgT;
        var sourceRange = orgS;
        if (isStart)
        {
            timeRange.Start = MathF.Min(t, orgT.End - MinDuration);
            if (!stretch)
                sourceRange.Start = orgS.Start + (timeRange.Start - orgT.Start) * rate;
        }
        else
        {
            timeRange.End = MathF.Max(t, orgT.Start + MinDuration);
            if (!stretch)
                sourceRange.End = orgS.End + (timeRange.End - orgT.End) * rate;
        }

        timeClip.TimeRange = timeRange;
        timeClip.SourceRange = sourceRange;
    }

    /// <summary>A speed change is hard to read from the view, so report the result.</summary>
    private static void ShowSpeedToast(TimeClip timeClip)
    {
        if (Math.Abs(timeClip.TimeRange.Duration) < 0.0001f)
            return;

        var speed = timeClip.SourceRange.Duration / timeClip.TimeRange.Duration;
        ActionToast.Show($"{{Clip speed {speed * 100:0.#}%}}");
    }

    private const float MinDuration = 1 / 60f; // In bars, as in the parent timeline
    private const string StretchTipId = "TimelineClipWindowStretch";
    private bool _wasStretched;

    private TimeRange _dragTimeRangeOrg;
    private TimeRange _dragSourceRangeOrg;
    private MoveTimeClipsCommand? _command;
    private readonly TimeClip[] _scratchClipList = new TimeClip[1];
    private readonly IValueSnapAttractor[] _selfExclusion;

    public ClipRange()
    {
        _selfExclusion = [this];
    }

    private const float OutsideHeight = 9999;
    private const float HandleHeight = 14; // Matches the source extent handles in the ruler

    private static readonly Color _timeRangeOutsideColor = UiColors.StatusAnimated.Fade(0.1f);

    // Window edges in playback time, valid while drawn this or the previous frame — the composition may
    // stop being a clip (or the user may leave it) and stale anchors must not keep attracting snaps.
    private double _windowStart;
    private double _windowEnd;
    private int _lastDrawFrame = -10;

    #region implement snapping interface -----------------------------------
    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        if (ImGui.GetFrameCount() - _lastDrawFrame > 1)
            return;

        snapResult.TryToImproveWithAnchorValue(_windowStart);
        snapResult.TryToImproveWithAnchorValue(_windowEnd);
    }
    #endregion
}
