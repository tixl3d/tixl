using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Dialogs;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.Gui.Graph.Dialogs;

internal sealed class RenameInputDialog : ModalDialog
{
    public void Draw()
    {
        if (BeginDialog("Rename input"))
        {
            DrawContent();
            EndDialogContent();
        }

        EndDialog();
    }

    private static void DrawContent()
    {
        var isWindowAppearing = ImGui.IsWindowAppearing();

        FormInputs.SetIndentToLeft();
        var symbol = _symbol;
        FormInputs.AddHint($"Careful! This operation will modify the definition of {symbol.Name}.");
        if (symbol.Namespace.StartsWith("Lib"))
        {
            FormInputs.AddHint("This is library Operator. Modifying it might prevent migrating your projects to future versions of Tooll");
        }

        FormInputs.SetIndentToParameters();
        var inputDef = symbol.InputDefinitions.FirstOrDefault(i => i.Id == _inputId);
        if (inputDef == null)
        {
            ImGui.TextUnformatted("invalid input");
            return;
        }

        if (isWindowAppearing)
        {
            _newInputName = inputDef.Name;
        }

        // ImGui.SetNextItemWidth(150);

        //var warning = String.Empty;
        var changed = SymbolModificationInputs.DrawFieldNameInput(symbol, "New Input name", "Input", ref _newInputName, out var isValid);

        if (isValid && (isWindowAppearing || changed))
        {
        }

        if (isWindowAppearing)
        {
            ImGui.SetKeyboardFocusHere();
        }
        
        FormInputs.ApplyIndent();

        if (CustomComponents.DrawCtaButton("Rename input", isValid))
        {
            // Validate with a dry run before committing the recompile to the undo stack.
            if (InputsAndOutputs.RenameInput(symbol, _inputId, _newInputName, dryRun: true, out _))
            {
                UndoRedoStack.AddAndExecute(new RenameSlotCommand(symbol.Id, _inputId, inputDef.Name, _newInputName, isInput: true));
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Emphasized))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    public void ShowNextFrame(Symbol symbol, Guid inputId)
    {
        ShowNextFrame();
        _symbol = symbol;
        _inputId = inputId;
    }

    private static Symbol _symbol;
    private static Guid _inputId;
    private static string _newInputName = string.Empty;
}