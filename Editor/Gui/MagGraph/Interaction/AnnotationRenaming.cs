#nullable enable
using ImGuiNET;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Annotations;
using T3.SystemUi;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Handles the UI and logic for renaming annotation titles and labels within the graph editor.
/// This class manages the editing state, input buffers, and command execution for undo/redo support.
/// </summary>
internal static class AnnotationRenaming
{
    /// <summary>
    /// Buffer for the annotation label being edited.
    /// </summary>
    private static string _labelBuffer = string.Empty;
    /// <summary>
    /// Buffer for the annotation title/description being edited.
    /// </summary>
    private static string _titleBuffer = string.Empty;
    /// <summary>
    /// Stores the original title to detect changes and support undo/redo.
    /// </summary>
    private static string? _originalTitle;
    /// <summary>
    /// Tracks the currently focused annotation for renaming.
    /// </summary>
    private static Guid _focusedAnnotationId;
    /// <summary>
    /// Command used for undo/redo of annotation text changes.
    /// </summary>
    private static ChangeAnnotationTextCommand? _changeAnnotationTextCommand;

    /// <summary>
    /// Draws the annotation renaming UI and handles user interaction.
    /// </summary>
    /// <param name="context">The current graph UI context.</param>
    public static void Draw(GraphUiContext context)
    {
        var shouldClose = false;
        var annotationId = context.ActiveAnnotationId;

        // If the annotation is not found, reset state and exit
        if (!context.Layout.Annotations.TryGetValue(annotationId, out var magAnnotation))
        {
            context.ActiveAnnotationId = Guid.Empty;
            context.StateMachine.SetState(GraphStates.Default, context);
            return;
        }

        var annotation = magAnnotation.Annotation;
        var screenArea = context.View.TransformRect(ImRect.RectWithSize(annotation.PosOnCanvas, annotation.Size));

        // Initialize buffers and command when dialog is first opened
        var justOpened = _focusedAnnotationId != annotationId;
        if (justOpened)
        {
            ImGui.SetKeyboardFocusHere();
            _focusedAnnotationId = annotationId;
            _changeAnnotationTextCommand = new ChangeAnnotationTextCommand(annotation, annotation.Title);
            _labelBuffer = annotation.Label ?? string.Empty;
            _titleBuffer = annotation.Title ?? string.Empty;
            _originalTitle = annotation.Title;
        }

        // --- Label editing UI ---
        ImGui.SetCursorScreenPos(screenArea.Min);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
        ImGui.SetNextItemWidth(350);
        ImGui.InputText("##renameAnnotationLabel", ref _labelBuffer, 256, ImGuiInputTextFlags.AutoSelectAll);
        ImGui.PopStyleVar();
        ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), UiColors.ForegroundFull.Fade(0.1f));
        // Draw placeholder if label is empty
        if (string.IsNullOrEmpty(_labelBuffer))
        {
            ImGui.GetWindowDrawList().AddText(Fonts.FontNormal, Fonts.FontNormal.FontSize, ImGui.GetItemRectMin() + new Vector2(3, 4), UiColors.ForegroundFull.Fade(0.3f), "Label...");
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y));

        // --- Title/description editing UI ---
        ImGui.InputTextMultiline("##renameAnnotation", ref _titleBuffer, 1024, screenArea.GetSize() - new Vector2(0, ImGui.GetItemRectSize().Y) - Vector2.One * 3, ImGuiInputTextFlags.AutoSelectAll);
        // Draw placeholder if title is empty
        if (string.IsNullOrEmpty(_titleBuffer))
        {
            ImGui.GetWindowDrawList().AddText(Fonts.FontNormal, Fonts.FontNormal.FontSize, ImGui.GetItemRectMin() + new Vector2(7, 7), UiColors.ForegroundFull.Fade(0.3f), "Description...");
        }

        // Update annotation with buffer values
        annotation.Label = _labelBuffer;
        annotation.Title = _titleBuffer;

        // Wait for user to finish editing before closing
        if (justOpened || _changeAnnotationTextCommand == null)
            return;

        // Detect if the user clicked outside, pressed Esc, or deactivated the item
        var clickedOutside = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !screenArea.Contains(ImGui.GetMousePos());
        shouldClose |= ImGui.IsItemDeactivated() || ImGui.IsKeyPressed((ImGuiKey)Key.Esc) || clickedOutside;
        if (!shouldClose)
            return;

        // Reset state and close dialog
        _focusedAnnotationId = Guid.Empty;
        context.ActiveAnnotationId = Guid.Empty;

        // Only execute command if text changed and not cancelled
        if (_titleBuffer != _originalTitle && !ImGui.IsKeyPressed((ImGuiKey)Key.Esc))
        {
            _changeAnnotationTextCommand.NewText = annotation.Title;
            UndoRedoStack.AddAndExecute(_changeAnnotationTextCommand);
        }

        context.StateMachine.SetState(GraphStates.Default, context);
    }
}