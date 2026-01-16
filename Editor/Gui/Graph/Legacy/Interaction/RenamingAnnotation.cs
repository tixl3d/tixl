#nullable enable
using ImGuiNET;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Annotations;
using T3.SystemUi;

namespace T3.Editor.Gui.Legacy.Interaction;

internal static class RenamingAnnotation
{
    internal static void Draw(Annotation annotation, ImRect screenArea, bool shouldBeOpened)
    {
        var justOpened = false;
        if (_focusedAnnotationId == Guid.Empty)
        {
            if (shouldBeOpened)
            {
                justOpened = true;
                ImGui.SetKeyboardFocusHere();
                _focusedAnnotationId = annotation.Id;
                _changeAnnotationTextCommand = new ChangeAnnotationTextCommand(annotation, annotation.Title);
            }
        }

        if (_focusedAnnotationId == Guid.Empty)
            return;

        if (_focusedAnnotationId != annotation.Id)
            return;

        var positionInScreen = screenArea.Min;
        ImGui.SetCursorScreenPos(positionInScreen);

        var text = annotation.Title;

        ImGui.SetNextItemWidth(150);
        ImGui.InputTextMultiline("##renameAnnotation", ref text, 256, screenArea.GetSize(), ImGuiInputTextFlags.AutoSelectAll);
        if (!ImGui.IsItemDeactivated())
            annotation.Title = text;

        if (!justOpened 
            && (ImGui.IsItemDeactivated() || ImGui.IsKeyPressed((ImGuiKey)Key.Esc))
            && _changeAnnotationTextCommand != null)
        {
            _focusedAnnotationId = Guid.Empty;
            _changeAnnotationTextCommand.NewText = annotation.Title;
            UndoRedoStack.AddAndExecute(_changeAnnotationTextCommand);
        }
    }

    private static Guid _focusedAnnotationId;
    private static ChangeAnnotationTextCommand? _changeAnnotationTextCommand;
}