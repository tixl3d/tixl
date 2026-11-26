#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.Gui.Graph.Dialogs;

/// <summary>
/// Confirms switching an int parameter shown with an enum back to a plain value, which removes the mapping from the
/// operator's source and can't be undone.
/// </summary>
internal sealed class ShowAsValueDialog : ModalDialog
{
    public ShowAsValueDialog()
    {
        DialogSize = new Vector2(440, 220);
    }

    public void Draw()
    {
        if (BeginDialog("Show as Value"))
        {
            DrawContent();
            EndDialogContent();
        }

        EndDialog();
    }

    public void ShowNextFrame(Symbol symbol, Guid inputId, string inputName, Type enumType)
    {
        _symbolId = symbol.Id;
        _inputId = inputId;
        _description = enumType.DeclaringType == symbol.InstanceType
                           ? $"{inputName} will show a plain number. The enum {enumType.Name} is removed from {symbol.Name} unless its code still uses it."
                           : $"{inputName} will show a plain number. The enum {enumType.Name} is declared elsewhere and stays.";
        _error = null;
        ShowNextFrame();
    }

    private void DrawContent()
    {
        FormInputs.SetIndentToLeft();
        ImGui.TextWrapped(_description);
        FormInputs.AddVerticalSpace(5);
        CustomComponents.StylizedText("This is not undoable.", Fonts.FontBold, UiColors.StatusAttention);

        if (_error != null)
        {
            FormInputs.AddVerticalSpace(5);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
            ImGui.TextWrapped(_error);
            ImGui.PopStyleColor();
        }

        FormInputs.AddVerticalSpace();
        AlignCtaButtons("Cancel", "Show as Value");
        if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Emphasized))
            ImGui.CloseCurrentPopup();

        ImGui.SameLine();
        if (CustomComponents.DrawCtaButton("Show as Value", true))
        {
            var command = MapInputToEnumCommand.ForRemoval(_symbolId, _inputId);
            if (command.TryDo(out var error))
            {
                // Not undoable, so adding it clears the undo stack.
                UndoRedoStack.Add(command);
                ImGui.CloseCurrentPopup();
            }
            else
            {
                Log.Warning(error);
                _error = error;
            }
        }
    }

    private Guid _symbolId;
    private Guid _inputId;
    private string _description = string.Empty;
    private string? _error;
}
