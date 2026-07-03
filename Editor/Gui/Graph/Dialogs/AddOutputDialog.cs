using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.Gui.Graph.Dialogs;

internal sealed class AddOutputDialog : ModalDialog
{
    internal AddOutputDialog()
    {
        Flags = ImGuiWindowFlags.NoResize;
    }

    internal ChangeSymbol.SymbolModificationResults Draw(Symbol symbol)
    {
        var result = ChangeSymbol.SymbolModificationResults.Nothing;
        if (BeginDialog("Add output"))
        {
            FormInputs.SetIndent(100);
            if(ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();
            
            _ = SymbolModificationInputs.DrawFieldInputs(symbol,  "Output name" ,"Output", 
                ref _parameterName, 
                ref _selectedType, 
                out var isValid);
            
            FormInputs.AddCheckBox("Is time clip", ref _isTimeClip);
                
            FormInputs.ApplyIndent();
            if (CustomComponents.DrawCtaButton("Add", isValid))
            {
                UndoRedoStack.AddAndExecute(new AddOutputCommand(symbol.Id, _parameterName, _selectedType, _isTimeClip));
                _parameterName = string.Empty;
            }

            ImGui.SameLine();
            if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Emphasized))
            {
                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }

        EndDialog();
        return result;
    }

    private bool _isTimeClip;
    private string _parameterName = string.Empty;
    private Type _selectedType = typeof(float);                                                                        
}