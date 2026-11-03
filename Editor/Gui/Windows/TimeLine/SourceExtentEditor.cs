#nullable enable
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Draws and edits the composition symbol's authored source extent
/// (<see cref="TimelineState.SourceExtent"/>) as a band in the timeline ruler with grab handles at
/// its start and end. Shown while the composition is itself used as a time clip or already has an
/// authored extent; without one, the band previews a derived fallback (the entered clip instance's
/// SourceRange, else the extent of all keyframes) and dragging a handle materializes the authored
/// value. The extent is authored in the composition's local time while the canvas is in playback time;
/// drawing, snapping and dragging go through the composition's time-clip mapping, so the band sits where
/// the entered instance actually plays that content. Emitted before the <see cref="SelectionRangeIndicator"/>
/// so the SRI wins overlapping hits.
/// </summary>
internal sealed class SourceExtentEditor : IValueSnapAttractor
{
    public SourceExtentEditor(TimeLineCanvas canvas)
    {
        _canvas = canvas;
        _selfExclusion = [this];
    }

    public void Draw(Instance composition, ImDrawListPtr drawList)
    {
        _visibleExtent = null;

        var symbolUi = composition.Symbol.GetSymbolUi();
        if (symbolUi == null)
            return;

        var authoredExtent = symbolUi.TimelineState?.SourceExtent;
        var compositionTimeClip = Structure.GetCompositionTimeClip(composition);
        if (authoredExtent == null && compositionTimeClip == null)
            return;

        var mapping = TimeLineCanvas.ParamTimeMapping.For(composition);
        if (!TryResolveLocalExtent(authoredExtent, compositionTimeClip, mapping, out var extent))
            return;

        var globalStart = mapping.ToGlobal(extent.Start);
        var globalEnd = mapping.ToGlobal(extent.End);
        _visibleExtent = globalStart <= globalEnd
                             ? new TimeRange((float)globalStart, (float)globalEnd)
                             : new TimeRange((float)globalEnd, (float)globalStart);

        var rulerPos = ImGui.GetWindowPos();
        var rulerSize = ImGui.GetWindowSize();
        var scale = T3Ui.UiScaleFactor;

        var top = rulerPos.Y + rulerSize.Y - 14 * scale;
        var bottom = rulerPos.Y + rulerSize.Y;

        var xStart = _canvas.TransformX((float)globalStart);
        var xEnd = _canvas.TransformX((float)globalEnd);

        // Band — clamped to the ruler so the outline doesn't run off-screen at high zoom.
        // A reversed clip (negative speed) puts the local start on the right.
        var left = MathF.Max(MathF.Min(xStart, xEnd), rulerPos.X);
        var right = MathF.Min(MathF.Max(xStart, xEnd), rulerPos.X + rulerSize.X);
        if (right > left)
        {
            drawList.AddRectFilled(new Vector2(left, top), new Vector2(right, bottom),
                                   UiColors.BackgroundFull.Fade(0.4f), 2 * scale);
            drawList.AddRect(new Vector2(left, top), new Vector2(right, bottom),
                             UiColors.ForegroundFull.Fade(authoredExtent != null ? 0.25f : 0.12f), 2 * scale);
        }

        EmitHandle("##SourceExtentStart", symbolUi, extent, mapping, xStart, top, bottom, scale, drawList, isStart: true);
        EmitHandle("##SourceExtentEnd", symbolUi, extent, mapping, xEnd, top, bottom, scale, drawList, isStart: false);
    }

    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        if (_visibleExtent is not { } extent)
            return;

        snapResult.TryToImproveWithAnchorValue(extent.Start);
        snapResult.TryToImproveWithAnchorValue(extent.End);
    }

    /// <summary>Resolves the extent in the composition's local time.</summary>
    private bool TryResolveLocalExtent(TimeRange? authoredExtent, TimeClip? compositionTimeClip,
                                       in TimeLineCanvas.ParamTimeMapping mapping, out TimeRange extent)
    {
        if (authoredExtent is { } authored)
        {
            extent = authored;
            return true;
        }

        if (compositionTimeClip != null && compositionTimeClip.SourceRange.Duration > 0)
        {
            extent = compositionTimeClip.SourceRange;
            return true;
        }

        // Keyframe editors report playback time — bring the range back into local time.
        var keyRange = _canvas.KeyframeEditors.GetAllKeyframesTimeRange();
        if (!keyRange.IsValid || keyRange.Duration <= 0)
        {
            extent = default;
            return false;
        }

        var localA = (float)mapping.ToLocal(keyRange.Start);
        var localB = (float)mapping.ToLocal(keyRange.End);
        extent = new TimeRange(MathF.Min(localA, localB), MathF.Max(localA, localB));
        return extent.Duration > 0;
    }

    private void EmitHandle(string id, SymbolUi symbolUi, TimeRange extent, in TimeLineCanvas.ParamTimeMapping mapping, float handleX,
                            float top, float bottom, float scale, ImDrawListPtr drawList, bool isStart)
    {
        var hitHalfWidth = 5 * scale;
        ImGui.SetCursorScreenPos(new Vector2(handleX - hitHalfWidth, top));
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton(id, new Vector2(hitHalfWidth * 2, bottom - top));

        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        if (ImGui.IsItemActivated())
        {
            _command = new ChangeSourceExtentCommand(symbolUi.Symbol.Id, symbolUi.TimelineState?.SourceExtent);
            _dragExtent = extent;
            _dragStartExtent = extent;
        }

        if (active && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            FrameStats.Current.OpenedPopupCapturedMouse = true;
            // Snap in playback time (all anchors live there), then store in local time.
            double u = _canvas.InverseTransformX(ImGui.GetIO().MousePos.X);
            if (!ImGui.GetIO().KeyShift
                && _canvas.SnapHandlerForU.TryCheckForSnapping(u, out var snapped, _canvas.Scale.X, _selfExclusion))
            {
                u = snapped;
            }

            var localU = (float)mapping.ToLocal(u);

            const float minDuration = 0.01f;
            if (isStart)
            {
                _dragExtent.Start = MathF.Min(localU, _dragExtent.End - minDuration);
            }
            else
            {
                _dragExtent.End = MathF.Max(localU, _dragExtent.Start + minDuration);
            }

            symbolUi.TimelineState ??= new TimelineState();
            symbolUi.TimelineState.SourceExtent = _dragExtent;
        }

        if (ImGui.IsItemDeactivated() && _command != null)
        {
            _command.StoreNewExtent(symbolUi.TimelineState?.SourceExtent);
            UndoRedoStack.AddAndExecute(_command);
            _command = null;

            // The band can sit right on top of the clip window, so dragging it is easily mistaken for trimming
            // the instance. Shown on release — while dragging, attention is on the handle, not the menu bar.
            if (_dragExtent.Start != _dragStartExtent.Start || _dragExtent.End != _dragStartExtent.End)
            {
                ActionToast.ShowTip(ExtentTipId,
                                    "{Template extent}   used for new instances and Reset Source to Extent, doesn't change this clip",
                                    maxTimes: 3);
            }
        }

        var handleColor = UiColors.ForegroundFull.Fade(hovered || active ? 1f : 0.8f);
        var handleHalfWidth = 1.5f * scale;
        drawList.AddRectFilled(new Vector2(handleX - handleHalfWidth, top),
                               new Vector2(handleX + handleHalfWidth, bottom),
                               handleColor, 1 * scale);
    }

    private const string ExtentTipId = "TimelineSourceExtentIsTemplate";

    private readonly TimeLineCanvas _canvas;
    private readonly IValueSnapAttractor[] _selfExclusion;

    private TimeRange? _visibleExtent;
    private TimeRange _dragExtent;
    private TimeRange _dragStartExtent;
    private ChangeSourceExtentCommand? _command;
}
