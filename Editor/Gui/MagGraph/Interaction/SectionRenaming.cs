#nullable enable
using ImGuiNET;
using T3.Editor.App;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Sections;
using T3.SystemUi;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Handles the UI and logic for renaming section titles and labels within the graph editor.
/// This class manages the editing state, input buffers, and command execution for undo/redo support.
/// </summary>
internal static class SectionRenaming
{
    /// <summary>
    /// Buffer for the section label being edited.
    /// </summary>
    private static string _labelBuffer = string.Empty;
    /// <summary>
    /// Buffer for the section title/description being edited.
    /// </summary>
    private static string _titleBuffer = string.Empty;
    /// <summary>
    /// Stores the original title to detect changes and support undo/redo.
    /// </summary>
    private static string? _originalTitle;
    /// <summary>
    /// Tracks the currently focused section for renaming.
    /// </summary>
    private static Guid _focusedSectionId;
    /// <summary>
    /// Command used for undo/redo of section text changes.
    /// </summary>
    private static ChangeSectionTextCommand? _changeSectionTextCommand;

    /** Leaves room for the collapse toggle left of the label. */
    private const float InputIndent = 34;

    /** Keeps the inputs inside the section's frame stroke. */
    private const float FrameInset = 3;

    /** Shared by the label input and its placeholder so the placeholder sits where typed text starts. */
    private const float LabelPadding = 4;

    /** The label plus the first lines of the description. */
    private const float VisibleRows = 3;

    private const float ViewPadding = 20;

    /// <summary>
    /// Scrolls the graph just enough to show the label field and the start of the description, which sit at
    /// the section's top-left corner and can be off-screen when a large section is opened for editing.
    /// </summary>
    private static void ScrollInputIntoView(GraphUiContext context, Vector2 inputTopLeft, float inputWidth)
    {
        var view = context.View;
        var padding = new Vector2(ViewPadding * T3Ui.UiScaleFactor);
        var viewMin = view.WindowPos + padding;
        var viewMax = view.WindowPos + view.WindowSize - padding;
        var inputBottomRight = inputTopLeft + new Vector2(inputWidth, ImGui.GetFrameHeight() * VisibleRows);

        var delta = new Vector2(GetDistanceOutside(inputTopLeft.X, inputBottomRight.X, viewMin.X, viewMax.X),
                                GetDistanceOutside(inputTopLeft.Y, inputBottomRight.Y, viewMin.Y, viewMax.Y));
        if (delta != Vector2.Zero)
            view.ScrollByScreenPixels(delta);
    }

    /** How far the view has to move so [min, max] fits into [viewMin, viewMax], keeping min visible first. */
    private static float GetDistanceOutside(float min, float max, float viewMin, float viewMax)
    {
        if (min < viewMin)
            return min - viewMin;

        if (max > viewMax)
            return MathF.Min(max - viewMax, min - viewMin);

        return 0;
    }

    /// <summary>
    /// Draws the section renaming UI and handles user interaction.
    /// </summary>
    /// <param name="context">The current graph UI context.</param>
    public static void Draw(GraphUiContext context)
    {
        var shouldClose = false;
        var sectionId = context.ActiveSectionId;

        // If the section is not found, reset state and exit
        if (!context.Layout.Sections.TryGetValue(sectionId, out var magSection))
        {
            context.ActiveSectionId = Guid.Empty;
            context.StateMachine.SetState(GraphStates.Default, context);
            return;
        }

        var section = magSection.Section;
        var screenArea = context.View.TransformRect(ImRect.RectWithSize(section.PosOnCanvas, section.Size));
        var inputTopLeft = screenArea.Min + new Vector2(InputIndent * T3Ui.UiScaleFactor, 0);
        var inputWidth = MathF.Max(screenArea.Max.X - inputTopLeft.X - FrameInset * T3Ui.UiScaleFactor, 1);

        // Initialize buffers and command when dialog is first opened
        var justOpened = _focusedSectionId != sectionId;
        if (justOpened)
        {
            ImGui.SetKeyboardFocusHere();
            _focusedSectionId = sectionId;
            _changeSectionTextCommand = new ChangeSectionTextCommand(section, section.Title);
            _labelBuffer = section.Label ?? string.Empty;
            _titleBuffer = section.Title ?? string.Empty;
            _originalTitle = section.Title;
            ScrollInputIntoView(context, inputTopLeft, inputWidth);
        }

        // --- Label editing UI ---
        ImGui.SetCursorScreenPos(inputTopLeft);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(LabelPadding * T3Ui.UiScaleFactor));
        ImGui.SetNextItemWidth(inputWidth);
        ImGui.InputText("##renameSectionLabel", ref _labelBuffer, 256, ImGuiInputTextFlags.AutoSelectAll);
        var isLabelActive = ImGui.IsItemActive();
        ImGui.PopStyleVar();
        ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), UiColors.ForegroundFull.Fade(0.1f));
        // Draw placeholder if label is empty
        if (string.IsNullOrEmpty(_labelBuffer))
        {
            ImGui.GetWindowDrawList().AddText(Fonts.FontNormal, Fonts.FontNormal.FontSize, ImGui.GetItemRectMin() + new Vector2(LabelPadding * T3Ui.UiScaleFactor), UiColors.ForegroundFull.Fade(0.3f), "Label...");
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y));

        // --- Title/description editing UI ---
        var descriptionHeight = MathF.Max(screenArea.Max.Y - ImGui.GetItemRectMax().Y - FrameInset * T3Ui.UiScaleFactor, 1);
        ImGui.InputTextMultiline("##renameSection", ref _titleBuffer, 1024, new Vector2(inputWidth, descriptionHeight), ImGuiInputTextFlags.AutoSelectAll);
        var isTitleActive = ImGui.IsItemActive();
        // Draw placeholder if title is empty
        if (string.IsNullOrEmpty(_titleBuffer))
        {
            ImGui.GetWindowDrawList().AddText(Fonts.FontNormal, Fonts.FontNormal.FontSize, ImGui.GetItemRectMin() + new Vector2(7, 7) * T3Ui.UiScaleFactor, UiColors.ForegroundFull.Fade(0.3f), "Description...");
        }

        // Update section with buffer values
        section.Label = _labelBuffer;
        section.Title = _titleBuffer;

        // Wait for user to finish editing before closing
        if (justOpened || _changeSectionTextCommand == null)
            return;

        // Close only when focus has left both inputs — not when tabbing/clicking between the
        // label and the title, which keeps one of them active.
        var clickedOutside = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !screenArea.Contains(ImGui.GetMousePos());
        var focusLeftEditor = !isLabelActive && !isTitleActive;
        shouldClose |= focusLeftEditor || ImGui.IsKeyPressed(Key.Esc.ToImGuiKey()) || clickedOutside;
        if (!shouldClose)
            return;

        // Reset state and close dialog
        _focusedSectionId = Guid.Empty;
        context.ActiveSectionId = Guid.Empty;

        // Only execute command if text changed and not cancelled
        if (_titleBuffer != _originalTitle && !ImGui.IsKeyPressed(Key.Esc.ToImGuiKey()))
        {
            _changeSectionTextCommand.NewText = section.Title;
            UndoRedoStack.AddAndExecute(_changeSectionTextCommand);
        }

        context.StateMachine.SetState(GraphStates.Default, context);
    }
}