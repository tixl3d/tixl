#nullable enable

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

internal sealed class AddInputDialog : ModalDialog
{
    internal AddInputDialog()
    {
        Flags = ImGuiWindowFlags.NoResize;
    }

    internal ChangeSymbol.SymbolModificationResults  Draw(Symbol symbol)
    {
        var results = ChangeSymbol.SymbolModificationResults.Nothing;
        
        if (BeginDialog("Add parameter input"))
        {
            FormInputs.SetIndent(100);
            
            if(ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();
            
            _ = SymbolModificationInputs.DrawFieldInputs(symbol,  "Input Name", "Input", ref _parameterName, ref _selectedType, out var isValid);
                
            FormInputs.AddCheckBox("Multi-Input", ref _multiInput);
                
            FormInputs.AddVerticalSpace(5);
            FormInputs.ApplyIndent();
            
            if (CustomComponents.DisablableButton("Add", isValid))
            {
                UndoRedoStack.AddAndExecute(new AddInputCommand(symbol.Id, _parameterName, _selectedType!, _multiInput));
                _parameterName = string.Empty;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }

        EndDialog();
        return results;
    }

    private string _parameterName = string.Empty;
    private bool _multiInput;
    private Type _selectedType = typeof(float);

}