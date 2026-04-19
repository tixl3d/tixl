#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.TimeLine.Raster;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Host for the inline curve-edit area shown below the dope sheet when at least one parameter
/// has curve-editing turned on. Owns its child window, fence, floating chrome, and its own V
/// (Y) scope — X is read-only from <see cref="TimeLineCanvas"/> so interactions in this pane
/// never touch the timeline's U state (and vice versa).
/// </summary>
internal sealed class InlineCurveArea : ScalableCanvas
{
    public InlineCurveArea(TimeLineCanvas timeLineCanvas, TimelineCurveEditor curveRenderer, HorizontalRaster horizontalRaster)
    {
        _timeLineCanvas = timeLineCanvas;
        _curveRenderer = curveRenderer;
        _horizontalRaster = horizontalRaster;
    }

    public void Draw(Instance compositionOp, List<TimeLineCanvas.AnimationParameter> animationParameters,
                     float height, bool modeChanged)
    {
        ImGui.BeginChild("##curveEditArea", new Vector2(0, height),
                         ImGuiChildFlags.None,
                          ImGuiWindowFlags.NoScrollbar);
        {
            // Publish this pane's screen rect so TimeLineCanvas knows when the mouse is inside it
            // and can cede its own zoom/pan to this sub-canvas (see TimeLineCanvas.Draw).
            _timeLineCanvas.CurveEditAreaScreenRect =
                new ImRect(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize());

            // Mirror the timeline's U state so Y-fit math, zoom-to-cursor, and fence inverse
            // transforms see consistent X. Pan can still move X inside UpdateCanvas — we write
            // that delta back to the timeline canvas right after so the dope sheet follows.
            SyncXFrom(_timeLineCanvas);
            UpdateCanvas(out _, T3Ui.EditingFlags.AllowHoveredChildWindows);
            SyncXTo(_timeLineCanvas);
            
            var heightChanged = Math.Abs(ImGui.GetWindowHeight() - _lastHeight) > 1f;
            _lastHeight = ImGui.GetWindowHeight();

            var selectionHash = ComputeSelectionHash(animationParameters);
            var selectionChanged = selectionHash != _lastSelectionHash;
            _lastSelectionHash = selectionHash;

            var expandedHash = ComputeExpandedParamsHash();
            var expandedChanged = expandedHash != _lastExpandedHash;
            _lastExpandedHash = expandedHash;

            if (modeChanged || selectionChanged || heightChanged || expandedChanged)
            {
                if (_curveRenderer.TryGetFitBounds(animationParameters,
                                                   _timeLineCanvas.CurveEditingParamHashes,
                                                   out var fitBounds))
                {
                    SetVerticalScopeToCanvasArea(fitBounds, flipY: true, paddingFraction: 0.15f);
                }
            }

            HandleFence();

            // Redirect the outer canvas to this pane's view while the curve renderer and raster
            // draw — they read transforms through TimeLineCanvas.Current. Restore after so code
            // that runs outside this child sees the outer timeline state intact.
            var savedCurrent = _timeLineCanvas.GetCurrentScope();
            var savedTarget = _timeLineCanvas.GetTargetScope();
            var savedPos = _timeLineCanvas.WindowPos;
            var savedSize = _timeLineCanvas.WindowSize;

            _timeLineCanvas.AdoptViewFrom(this);

            _horizontalRaster.Draw(_timeLineCanvas);
            _curveRenderer.Draw(compositionOp, animationParameters,
                                fitVerticalOnly: false,
                                parameterHashFilter: _timeLineCanvas.CurveEditingParamHashes,
                                showParameterList: false);

            DrawFloatingChrome();

            _timeLineCanvas.SetCurrentScope(savedCurrent);
            _timeLineCanvas.SetTargetScope(savedTarget);
            _timeLineCanvas.SetWindowRect(savedPos, savedSize);
        }
        ImGui.EndChild();
    }

    /// <summary>
    /// Match the timeline's U scale range. The default non-timeline range ([0.02, 40]) is
    /// narrower than typical timeline zooms — without this override, the first SyncXFrom copies
    /// a large parent ScaleTarget into this sub-canvas, the end-of-HandleInteraction clamp
    /// pulls it down to 40, and SyncXTo pushes that clamped value back to the parent, producing
    /// a one-time horizontal zoom-out on first CEA open.
    /// </summary>
    protected internal override Vector2 ClampScaleToValidRange(Vector2 scale)
        => new(scale.X.Clamp(0.01f, 5000), scale.Y);

    /// <summary>V-only zoom; U stays locked to the timeline canvas.</summary>
    protected override void ApplyZoomDelta(Vector2 position, float zoomDelta, out bool zoomed)
    {
        zoomed = false;
        if (Math.Abs(zoomDelta - 1) < 0.001f)
            return;
        var clamped = ClampScaleToValidRange(ScaleTarget * zoomDelta);
        if (clamped == ScaleTarget)
            return;

        var zoom = new Vector2(1f, zoomDelta);
        ScaleTarget *= zoom;
        if (Math.Abs(zoomDelta) > 0.1f)
            zoomed = true;

        var focus = InverseTransformPositionFloat(position);
        ScrollTarget += (focus - ScrollTarget) * (zoom - Vector2.One) / zoom;
    }

    // Pan is both axes — the X component is written back to the timeline canvas after
    // UpdateCanvas (see Draw) so panning horizontally in the curve area still moves time in
    // the dope sheet, matching user expectations. The Y component stays in this sub-canvas.

    private void HandleFence()
    {
        if (T3Ui.IsAnyPopupOpen)
            return;

        switch (_fence.UpdateAndDraw(out var selectMode))
        {
            case SelectionFence.States.Updated:
            case SelectionFence.States.CompletedAsClick:
                DispatchSelectionChange(_fence.BoundsInScreen, selectMode);
                break;
        }
    }

    private void DispatchSelectionChange(ImRect screenArea, SelectionFence.SelectModes selectMode)
    {
        if (selectMode == SelectionFence.SelectModes.Replace)
        {
            _timeLineCanvas.SharedSelectedKeyframes.Clear();
            selectMode = SelectionFence.SelectModes.Add;
        }
        var canvasArea = InverseTransformRect(screenArea).MakePositive();
        _curveRenderer.UpdateSelectionForCanvasArea(canvasArea, selectMode);
    }

    private void DrawFloatingChrome()
    {
        var drawList = ImGui.GetWindowDrawList();
        var scale = T3Ui.UiScaleFactor;
        const float iconPx = 15f;
        var iconSize = iconPx * scale;
        var hitSize = new Vector2(iconSize + 4 * scale, iconSize + 4 * scale);
        var padding = 4 * scale;

        var paneMin = ImGui.GetWindowPos();
        var paneMax = paneMin + ImGui.GetWindowSize();
        var closePos = new Vector2(paneMax.X - padding - hitSize.X, paneMin.Y + padding);
        var normalizePos = closePos - new Vector2(hitSize.X + padding, 0);

        DrawChromeButton("##curvePaneNormalize", normalizePos, hitSize, iconSize,
                         Icon.Scale, _timeLineCanvas.NormalizeCurveView, drawList,
                         () => _timeLineCanvas.NormalizeCurveView = !_timeLineCanvas.NormalizeCurveView);

        DrawChromeButton("##curvePaneClose", closePos, hitSize, iconSize,
                         Icon.ChevronDown, false, drawList,
                         () =>
                         {
                             _timeLineCanvas.CurveEditingParamHashes.Clear();
                             _timeLineCanvas.VisibleComponentMask.Clear();
                         });
    }

    private static void DrawChromeButton(string id, Vector2 screenPos, Vector2 hitSize, float iconSize,
                                         Icon icon, bool isActive, ImDrawListPtr drawList, Action clicked)
    {
        ImGui.SetCursorScreenPos(screenPos);
        if (ImGui.InvisibleButton(id, hitSize))
            clicked();

        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            drawList.AddRectFilled(screenPos, screenPos + hitSize,
                                   UiColors.BackgroundFull.Fade(0.6f));
        }

        var color = isActive ? UiColors.StatusActivated : UiColors.ForegroundFull.Fade(hovered ? 1f : 0.6f);
        var iconPos = screenPos + (hitSize - new Vector2(iconSize, iconSize)) * 0.5f;
        Icons.DrawIconAtScreenPosition(icon, iconPos, drawList, color);
    }

    private int ComputeSelectionHash(List<TimeLineCanvas.AnimationParameter> animationParameters)
    {
        var hash = animationParameters.Count;
        for (var i = 0; i < animationParameters.Count; i++)
            hash = hash * 397 ^ animationParameters[i].Hash;
        return hash;
    }

    private int ComputeExpandedParamsHash()
    {
        var h = _timeLineCanvas.CurveEditingParamHashes.Count;
        foreach (var x in _timeLineCanvas.CurveEditingParamHashes)
            h = h * 397 ^ x;
        return h;
    }

    private readonly TimeLineCanvas _timeLineCanvas;
    private readonly TimelineCurveEditor _curveRenderer;
    private readonly HorizontalRaster _horizontalRaster;
    private readonly SelectionFence _fence = new();

    private float _lastHeight;
    private int _lastSelectionHash;
    private int _lastExpandedHash;
}
