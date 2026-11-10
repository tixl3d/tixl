#nullable enable
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Interaction;

/// <summary>
/// A resizable popup that embeds the <see cref="GradientEditor"/> at a comfortable size, with an optional
/// list of steps for setting positions and colors precisely.
/// </summary>
[HelpUiID("GradientEditorPopup")]
internal static class GradientEditorPopup
{
    /// <summary>
    /// Must be called in the same ImGui ID scope as <see cref="Draw"/>.
    /// </summary>
    public static void Open(ImRect gradientAreaOnScreen)
    {
        ImGui.OpenPopup(PopupId);
        ImGui.SetNextWindowPos(new Vector2(gradientAreaOnScreen.Min.X, gradientAreaOnScreen.Max.Y + 4 * T3Ui.UiScaleFactor));
    }

    public static InputEditStateFlags Draw(ref Gradient gradient, bool cloneIfModified)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8) * scale);
        // NoMove: a press in the corner resizes instead of moving; the header grip moves the popup explicitly.
        var isOpen = ImGui.BeginPopup(PopupId, ImGuiWindowFlags.NoMove);
        ImGui.PopStyleVar();
        if (!isOpen)
            return InputEditStateFlags.Nothing;

        var editResult = InputEditStateFlags.Nothing;
        var width = MathF.Round(_size.X * scale);
        var windowPos = ImGui.GetWindowPos();
        ColorEditPopup.OpenBesideRect = new ImRect(windowPos, windowPos + ImGui.GetWindowSize());

        var originalGradient = gradient;
        var mustClone = cloneIfModified;
        editResult |= DrawHeader(width, ref gradient, ref mustClone);
        FormInputs.AddVerticalSpace(4);

        // The editor's hit areas reach below the gradient, so a fixed-size child keeps them from growing the popup.
        var gradientHeight = MathF.Round(_size.Y * scale);
        var childSize = new Vector2(width, gradientHeight + MathF.Round(3 * scale));
        ImGui.BeginChild("##gradient", childSize, ImGuiChildFlags.None,
                         ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        {
            // The editor's step handles overhang its area by half a handle on either side.
            var handleSize = GradientEditor.StepHandleSize;
            var origin = ImGui.GetCursorScreenPos().Floor();
            var area = new ImRect(origin + new Vector2(handleSize.X * 0.5f, 0),
                                  origin + new Vector2(width - handleSize.X * 0.5f, gradientHeight));
            editResult |= GradientEditor.Draw(ref gradient, ImGui.GetWindowDrawList(), area, cloneIfModified, allowPopup: false,
                                                alwaysShowCurves: _showCurves);
        }
        ImGui.EndChild();

        if (_showStepList)
        {
            FormInputs.AddVerticalSpace(6);
            mustClone = cloneIfModified && ReferenceEquals(gradient, originalGradient);
            editResult |= DrawStepList(ref gradient, mustClone, width);
        }

        ColorEditPopup.OpenBesideRect = null;
        DrawResizeGrip();

        // Escape in a text field only cancels that edit.
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false) && !ImGui.GetIO().WantTextInput)
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
        return editResult;
    }

    private static InputEditStateFlags DrawHeader(float width, ref Gradient gradient, ref bool mustClone)
    {
        var editResult = InputEditStateFlags.Nothing;
        var rowStart = ImGui.GetCursorScreenPos();
        var iconSize = new Vector2(ImGui.GetFrameHeight());

        // Popups open below the parameter and can cover the operator being edited, so the grip lets users move it aside.
        ImGui.InvisibleButton("##moveGradientPopup", iconSize);
        var isHovered = ImGui.IsItemHovered();
        var isActive = ImGui.IsItemActive();
        if (isHovered || isActive)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

        if (isActive)
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);

        var gripColor = isHovered || isActive ? UiColors.Text : UiColors.TextMuted.Fade(0.5f);
        Icons.DrawIconOnLastItem(Icon.DragIndicator, gripColor);

        // Interpolation, shown as a dropdown label.
        ImGui.SameLine(0, 0);
        var label = _interpolationNames[(int)gradient.Interpolation];
        var labelSize = ImGui.CalcTextSize(label);
        var chevronWidth = Icons.FontSize;
        var labelPadding = MathF.Round(4 * T3Ui.UiScaleFactor);
        ImGui.InvisibleButton("##interpolation", new Vector2(labelPadding + labelSize.X + chevronWidth, iconSize.Y));
        var isLabelHovered = ImGui.IsItemHovered();
        var labelMin = ImGui.GetItemRectMin();
        var labelColor = isLabelHovered ? UiColors.Text : UiColors.TextMuted;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(new Vector2(labelMin.X + labelPadding, labelMin.Y + (iconSize.Y - labelSize.Y) * 0.5f).Floor(), labelColor, label);
        Icons.DrawIconAtScreenPosition(Icon.ChevronDown,
                                       new Vector2(labelMin.X + labelPadding + labelSize.X, labelMin.Y + (iconSize.Y - Icons.FontSize) * 0.5f).Floor(),
                                       drawList, labelColor);
        if (isLabelHovered)
            CustomComponents.TooltipForLastItem("Interpolation", "How colors blend between steps.");

        _currentInterpolation = gradient.Interpolation;
        _pickedInterpolation = null;
        CustomComponents.ContextMenuForItem(DrawInterpolationMenu, null, "##interpolationMenu", ImGuiPopupFlags.MouseButtonLeft);
        if (_pickedInterpolation is { } picked && picked != gradient.Interpolation)
        {
            EnsureEditable(ref gradient, ref mustClone);
            gradient.Interpolation = picked;
            editResult |= InputEditStateFlags.Started | InputEditStateFlags.ModifiedAndFinished;
        }

        ImGui.SameLine(0, 0);
        // The actions sit a little apart from the view toggles to their right.
        var groupGap = MathF.Round(6 * T3Ui.UiScaleFactor);
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + width - iconSize.X * 4 - groupGap, rowStart.Y));
        if (CustomComponents.IconButton(Icon.Refresh, iconSize))
        {
            EnsureEditable(ref gradient, ref mustClone);
            GradientEditor.Reverse(gradient);
            editResult |= InputEditStateFlags.Started | InputEditStateFlags.ModifiedAndFinished;
        }

        CustomComponents.TooltipForLastItem("Reverse");

        ImGui.SameLine(0, 0);
        var canDistribute = GradientEditor.CanDistributeEvenly(gradient);
        if (CustomComponents.IconButton(Icon.Grid, iconSize,
                                        canDistribute ? CustomComponents.ButtonStates.Default : CustomComponents.ButtonStates.Disabled)
            && canDistribute)
        {
            EnsureEditable(ref gradient, ref mustClone);
            GradientEditor.DistributeEvenly(gradient);
            editResult |= InputEditStateFlags.Started | InputEditStateFlags.ModifiedAndFinished;
        }

        CustomComponents.TooltipForLastItem("Distribute evenly", canDistribute ? null : "The steps are already evenly spaced.");

        ImGui.SameLine(0, 0);
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + width - iconSize.X * 2, rowStart.Y));
        if (CustomComponents.IconButton(Icon.InterpolateSmooth, iconSize,
                                        _showCurves ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
        {
            _showCurves = !_showCurves;
        }

        CustomComponents.TooltipForLastItem(_showCurves ? "Show curves on hover only" : "Always show curves",
                                            "The red, green, blue and alpha values across the gradient.");

        ImGui.SameLine(0, 0);
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + width - iconSize.X, rowStart.Y));
        if (CustomComponents.IconButton(Icon.ViewList, iconSize,
                                        _showStepList ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Default))
        {
            _showStepList = !_showStepList;
        }

        CustomComponents.TooltipForLastItem(_showStepList ? "Hide steps" : "Show steps",
                                            "Lists every color step with its exact position.");
        return editResult;
    }

    private static void DrawInterpolationMenu()
    {
        for (var index = 0; index < _interpolationValues.Length; index++)
        {
            var value = _interpolationValues[index];
            if (CustomComponents.DrawMenuItem(_interpolationMenuId + index, _interpolationNames[(int)value], isChecked: value == _currentInterpolation,
                                              reserveIconColumn: false))
            {
                _pickedInterpolation = value;
            }
        }
    }

    private static InputEditStateFlags DrawStepList(ref Gradient gradient, bool mustClone, float width)
    {
        var scale = T3Ui.UiScaleFactor;
        var editResult = InputEditStateFlags.Nothing;
        var rowHeight = FormInputs.ListRowHeight;
        var positionWidth = MathF.Round(PositionColumnWidth * scale);
        var swatchInset = MathF.Round(3 * scale);
        var swatchWidth = MathF.Max(width - positionWidth - rowHeight - swatchInset * 2, rowHeight);
        var drawList = ImGui.GetWindowDrawList();
        var canRemove = gradient.Steps.Count > 1;
        var removeIndex = -1;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, MathF.Round(RowGap * scale)));
        ImGui.PushID("steps");

        var steps = gradient.Steps;
        for (var stepIndex = 0; stepIndex < steps.Count; stepIndex++)
        {
            var step = steps[stepIndex];

            // Keyed by id, so an active drag stays on its step when sorting moves the row.
            ImGui.PushID(step.Id.GetHashCode());

            var rowMin = ImGui.GetCursorScreenPos().Floor();
            var rowMax = rowMin + new Vector2(width, rowHeight);
            FormInputs.DrawListRowBackground(drawList, rowMin, rowMax, isDragging: false, drawGrip: false);

            // Position
            ImGui.SetNextItemWidth(positionWidth);
            var position = step.NormalizedPosition;
            ImGui.DragFloat("##position", ref position, 0.002f, 0f, 1f, "%.3f", ImGuiSliderFlags.AlwaysClamp);
            var positionState = EditStateOfLastItem();
            if ((positionState & InputEditStateFlags.Modified) != 0)
            {
                step = GetEditableStep(ref gradient, ref mustClone, stepIndex);
                step.NormalizedPosition = position;
            }

            editResult |= positionState;
            CustomComponents.TooltipForLastItem("Position", "Drag or double-click to type a value between 0 and 1.");

            // Color
            ImGui.SameLine(0, 0);
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X + swatchInset, rowMin.Y + swatchInset));
            var color = step.Color;
            var colorState = ColorEditButton.Draw(ref color, new Vector2(swatchWidth, rowHeight - swatchInset * 2));
            if ((colorState & InputEditStateFlags.Modified) != 0)
            {
                step = GetEditableStep(ref gradient, ref mustClone, stepIndex);
                step.Color = color;
            }

            editResult |= colorState;

            // Remove
            ImGui.SameLine(0, 0);
            ImGui.SetCursorScreenPos(new Vector2(rowMax.X - rowHeight, rowMin.Y));
            if (CustomComponents.IconButton(Icon.Close, new Vector2(rowHeight),
                                            canRemove ? CustomComponents.ButtonStates.Default : CustomComponents.ButtonStates.Disabled)
                && canRemove)
            {
                removeIndex = stepIndex;
            }

            CustomComponents.TooltipForLastItem(canRemove ? "Remove step" : "A gradient needs at least one step");

            ImGui.PopID();
        }

        ImGui.PopID();
        ImGui.PopStyleVar();

        if (removeIndex >= 0)
        {
            GetEditableStep(ref gradient, ref mustClone, removeIndex);
            gradient.Steps.RemoveAt(removeIndex);
            editResult |= InputEditStateFlags.ModifiedAndFinished;
        }

        return editResult;
    }

    private static Gradient.Step GetEditableStep(ref Gradient gradient, ref bool mustClone, int stepIndex)
    {
        EnsureEditable(ref gradient, ref mustClone);
        return gradient.Steps[stepIndex];
    }

    /** A default gradient is shared with the symbol definition, so the first edit has to switch to a copy. */
    private static void EnsureEditable(ref Gradient gradient, ref bool mustClone)
    {
        if (mustClone)
        {
            var clone = gradient.TypedClone();

            // Keep the step ids, so the control being dragged stays active.
            for (var index = 0; index < clone.Steps.Count; index++)
            {
                clone.Steps[index].Id = gradient.Steps[index].Id;
            }

            gradient = clone;
            mustClone = false;
        }
    }

    private static InputEditStateFlags EditStateOfLastItem()
    {
        var state = InputEditStateFlags.Nothing;
        if (ImGui.IsItemActivated())
            state |= InputEditStateFlags.Started;

        if (ImGui.IsItemEdited())
            state |= InputEditStateFlags.Modified;

        if (ImGui.IsItemDeactivatedAfterEdit())
            state |= InputEditStateFlags.Finished;

        return state;
    }

    /** Popups size to their content, so the grip changes the content size. Drawn into the window padding, it adds no item that would grow the popup. */
    private static void DrawResizeGrip()
    {
        var scale = T3Ui.UiScaleFactor;
        var gripSize = MathF.Round(ResizeGripSize * scale);
        var windowMax = (ImGui.GetWindowPos() + ImGui.GetWindowSize()).Floor();
        var gripMin = windowMax - new Vector2(gripSize);

        var isHovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(gripMin, windowMax);
        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            _isResizing = true;

        if (_isResizing && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            _isResizing = false;

        if (isHovered || _isResizing)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);

        if (_isResizing)
        {
            _size += ImGui.GetIO().MouseDelta / scale;
            _size = Vector2.Max(_size, MinSize);
        }

        // Inset, so the popup's rounded corner doesn't cut the triangle.
        var inset = MathF.Round(2 * scale);
        var triangleMax = windowMax - new Vector2(inset);
        var triangleMin = triangleMax - new Vector2(gripSize - inset * 2);
        var color = isHovered || _isResizing ? UiColors.Text : UiColors.TextMuted.Fade(0.5f);
        ImGui.GetWindowDrawList().AddTriangleFilled(new Vector2(triangleMax.X, triangleMin.Y), triangleMax,
                                                    new Vector2(triangleMin.X, triangleMax.Y), color);
    }

    private const string PopupId = "##gradientEditor";
    private const float PositionColumnWidth = 70;
    private const float RowGap = 2;
    private const float ResizeGripSize = 12;

    /** Unscaled width of the popup content and height of the gradient. */
    private static readonly Vector2 MinSize = new(220, 30);

    private static Vector2 _size = new(420, 60);
    private static bool _showStepList;
    private static bool _showCurves;
    private static bool _isResizing;

    private static readonly Gradient.Interpolations[] _interpolationValues = Enum.GetValues<Gradient.Interpolations>();
    private static readonly string[] _interpolationNames = Enum.GetNames<Gradient.Interpolations>();
    private static readonly int _interpolationMenuId = nameof(_interpolationMenuId).GetHashCode();

    /** Static, so the menu callback reads and writes them without capturing a closure. */
    private static Gradient.Interpolations _currentInterpolation;
    private static Gradient.Interpolations? _pickedInterpolation;
}
