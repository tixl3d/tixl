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
/// Handles renaming annotation titles
/// </summary>
internal static class AnnotationRenaming
{
    public static void Draw(GraphUiContext context)
    {
        var shouldClose = false;
        var annotationId = context.ActiveAnnotationId;

        if (!context.Layout.Annotations.TryGetValue(annotationId, out var magAnnotation))
        {
            context.ActiveAnnotationId = Guid.Empty;
            context.StateMachine.SetState(GraphStates.Default, context);
            return;
        }

        var annotation = magAnnotation.Annotation;
        var screenArea = context.Canvas.TransformRect(ImRect.RectWithSize(annotation.PosOnCanvas, annotation.Size));

        var justOpened = _focusedAnnotationId != annotationId;
        if (justOpened)
        {
            ImGui.SetKeyboardFocusHere();
            _focusedAnnotationId = annotationId;
            _changeAnnotationTextCommand = new ChangeAnnotationTextCommand(annotation, annotation.Title);
        }

        // Edit label
        var positionInScreen = screenArea.Min;
        {
            var labelPos = positionInScreen + new Vector2(2, 2); // - new Vector2(2, Fonts.FontNormal.FontSize + 8);
            ImGui.SetCursorScreenPos(labelPos);

            ImGui.SetNextItemWidth(200);
            ImGui.InputText("##renameAnnotationLabel", ref annotation.Label, 256, ImGuiInputTextFlags.AutoSelectAll);

            if (ImGui.IsItemDeactivated() && ImGui.IsKeyPressed((ImGuiKey)Key.Return))
            {
                shouldClose = true;
            }
            
            // Label Placeholder
            if (string.IsNullOrEmpty(annotation.Label))
            {
                ImGui.GetWindowDrawList().AddText(Fonts.FontNormal,
                                                  Fonts.FontNormal.FontSize,
                                                  ImGui.GetItemRectMin() + new Vector2(7, 7),
                                                  UiColors.ForegroundFull.Fade(0.3f),
                                                  "Label...");
            }
            
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().Y));

        // Edit description
        {
            var text = annotation.Title;

            // Note: As of imgui 1.89 AutoSelectAll might not be supported for InputTextMultiline
            ImGui.InputTextMultiline("##renameAnnotation", 
                                     ref text, 
                                     256,
                                     screenArea.GetSize() - new Vector2(0, ImGui.GetItemRectSize().Y) - Vector2.One *3,
                                     ImGuiInputTextFlags.AutoSelectAll);
            if (!ImGui.IsItemDeactivated())
                annotation.Title = text;

            // Placeholder
            if (string.IsNullOrEmpty(annotation.Title))
            {
                ImGui.GetWindowDrawList().AddText(Fonts.FontNormal,
                                                  Fonts.FontNormal.FontSize,
                                                  ImGui.GetItemRectMin() + new Vector2(7, 7),
                                                  UiColors.ForegroundFull.Fade(0.3f),
                                                  "Description...");
            }
        }
        if (justOpened || _changeAnnotationTextCommand == null)
            return;

        var clickedOutside = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !screenArea.Contains(ImGui.GetMousePos());
        
        shouldClose |= ImGui.IsItemDeactivated() || ImGui.IsKeyPressed((ImGuiKey)Key.Esc) || clickedOutside;
        if (!shouldClose)
            return;

        _focusedAnnotationId = Guid.Empty;
        _changeAnnotationTextCommand.NewText = annotation.Title;
        context.ActiveAnnotationId = Guid.Empty;

        UndoRedoStack.AddAndExecute(_changeAnnotationTextCommand);
        context.StateMachine.SetState(GraphStates.Default, context);
    }

    private static Guid _focusedAnnotationId;
    private static ChangeAnnotationTextCommand? _changeAnnotationTextCommand;
}