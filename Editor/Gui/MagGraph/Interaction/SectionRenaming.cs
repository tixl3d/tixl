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
        }

        // --- Label editing UI ---
        ImGui.SetCursorScreenPos(screenArea.Min);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
        ImGui.SetNextItemWidth(350);
        ImGui.InputText("##renameSectionLabel", ref _labelBuffer, 256, ImGuiInputTextFlags.AutoSelectAll);
        var isLabelActive = ImGui.IsItemActive();
        ImGui.PopStyleVar();
        ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), UiColors.ForegroundFull.Fade(0.1f));
        // Draw placeholder if label is empty
        if (string.IsNullOrEmpty(_labelBuffer))
        {
            ImGui.GetWindowDrawList().AddText(Fonts.FontNormal, Fonts.FontNormal.FontSize, ImGui.GetItemRectMin() + new Vector2(3, 4), UiColors.ForegroundFull.Fade(0.3f), "Label...");
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y));

        // --- Title/description editing UI ---
        ImGui.InputTextMultiline("##renameSection", ref _titleBuffer, 1024, screenArea.GetSize() - new Vector2(0, ImGui.GetItemRectSize().Y) - Vector2.One * 3, ImGuiInputTextFlags.AutoSelectAll);
        var isTitleActive = ImGui.IsItemActive();
        // Draw placeholder if title is empty
        if (string.IsNullOrEmpty(_titleBuffer))
        {
            ImGui.GetWindowDrawList().AddText(Fonts.FontNormal, Fonts.FontNormal.FontSize, ImGui.GetItemRectMin() + new Vector2(7, 7), UiColors.ForegroundFull.Fade(0.3f), "Description...");
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