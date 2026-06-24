#nullable enable

using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Bottom-of-timeline detail pane that hosts either the inline curve editor or the
/// inline DataClip editor. Mode priority: ClipEditing &gt; CurveEditing &gt; Hidden.
/// Owns the shared chrome (resize handle, close button); each inner renderer owns its
/// own <c>BeginChild</c> + content and calls <see cref="DrawCloseButton"/> from inside.
/// </summary>
[HelpUiID("AnimationArea")]
internal sealed class TimelineDetailsArea
{
    public enum DetailMode
    {
        Hidden,
        ClipEditing,
        CurveEditing,
    }

    public TimelineDetailsArea(TimeLineCanvas timeLineCanvas,
                               InlineCurveArea curveArea,
                               InlineDataClipArea clipArea)
    {
        _timeLineCanvas = timeLineCanvas;
        _curveArea = curveArea;
        _clipArea = clipArea;
    }

    public DetailMode ResolveMode(Instance compositionOp)
    {
        if (_timeLineCanvas.Mode != TimeLineCanvas.Modes.DopeView)
            return DetailMode.Hidden;

        // Clip editing wins when both are eligible — explicit toggle beats implicit.
        if (_timeLineCanvas.InlineDataClipEditEnabled
            && InlineDataClipArea.HasSelectedDataClipInstance(_timeLineCanvas, compositionOp))
        {
            return DetailMode.ClipEditing;
        }

        if (_timeLineCanvas.CurveEditingParamHashes.Count > 0)
            return DetailMode.CurveEditing;

        return DetailMode.Hidden;
    }

    public bool IsActive(Instance compositionOp) => ResolveMode(compositionOp) != DetailMode.Hidden;

    /// <summary>Drag-resize handle between the top pane and the detail pane.</summary>
    public void DrawResizeHandle(float reserveHeight)
    {
        ImGui.InvisibleButton("##timelineDetailsResize", new Vector2(-1, reserveHeight));

        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);

        if (ImGui.IsItemActivated())
        {
            _heightOnDragStart = _timeLineCanvas.DetailsAreaHeight;
            _mouseYOnDragStart = ImGui.GetMousePos().Y;
        }

        if (ImGui.IsItemActive())
        {
            var dy = ImGui.GetMousePos().Y - _mouseYOnDragStart;
            _timeLineCanvas.DetailsAreaHeight = _heightOnDragStart - dy;
        }

        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        var color = (ImGui.IsItemHovered() || ImGui.IsItemActive())
                        ? UiColors.StatusActivated
                        : UiColors.GridLines.Fade(0.6f);
        var dl = ImGui.GetWindowDrawList();
        var centerY = (int)((rectMin.Y + rectMax.Y) * 0.5f);
        dl.AddRectFilled(new Vector2(rectMin.X, centerY),
                         new Vector2(rectMax.X, centerY + 1),
                         color);
    }

    /// <summary>Dispatches to the active mode's renderer. Caller sizes the outer child.</summary>
    public void Draw(Instance compositionOp,
                     List<TimeLineCanvas.AnimationParameter> animationParameters,
                     float height,
                     float fitReferenceHeight,
                     bool modeChanged)
    {
        switch (ResolveMode(compositionOp))
        {
            case DetailMode.ClipEditing:
                _clipArea.Draw(compositionOp, height);
                break;
            case DetailMode.CurveEditing:
                _curveArea.Draw(compositionOp, animationParameters, height, fitReferenceHeight, modeChanged);
                break;
        }
    }

    /// <summary>
    /// Shared close button — called by each renderer from inside its own pane.
    /// Clears both gates so the pane goes away regardless of which mode was active.
    /// </summary>
    public static void DrawCloseButton(TimeLineCanvas timeLineCanvas)
    {
        var scale = T3Ui.UiScaleFactor;
        var iconSize = 15f * scale;
        var hitSize = new Vector2(iconSize + 4 * scale, iconSize + 4 * scale);
        var padding = 4 * scale;

        var paneMin = ImGui.GetWindowPos();
        var paneMax = paneMin + ImGui.GetWindowSize();
        // Avoid overlapping the vertical scrollbar — when present it eats the rightmost
        // ScrollbarSize px and the close button would render under it.
        var scrollbarOffset = ImGui.GetScrollMaxY() > 0 ? ImGui.GetStyle().ScrollbarSize : 0f;
        var closePos = new Vector2(paneMax.X - padding - hitSize.X - scrollbarOffset, paneMin.Y + padding);

        ImGui.SetCursorScreenPos(closePos);
        if (ImGui.InvisibleButton("##timelineDetailsClose", hitSize))
        {
            timeLineCanvas.InlineDataClipEditEnabled = false;
            timeLineCanvas.CurveEditingParamHashes.Clear();
            timeLineCanvas.VisibleComponentMask.Clear();
        }

        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered)
            dl.AddRectFilled(closePos, closePos + hitSize, UiColors.BackgroundFull.Fade(0.6f));

        var iconColor = UiColors.ForegroundFull.Fade(hovered ? 1f : 0.6f);
        var iconPos = closePos + (hitSize - new Vector2(iconSize, iconSize)) * 0.5f;
        Icons.DrawIconAtScreenPosition(Icon.Close, iconPos, dl, iconColor);
    }

    private readonly TimeLineCanvas _timeLineCanvas;
    private readonly InlineCurveArea _curveArea;
    private readonly InlineDataClipArea _clipArea;

    private float _heightOnDragStart;
    private float _mouseYOnDragStart;
}
