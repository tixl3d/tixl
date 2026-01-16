using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.Gui.Dialogs;

public sealed class AddOutputDialog : ModalDialog
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
            //ImGui.SetKeyboardFocusHere();
            _ = SymbolModificationInputs.DrawFieldInputs(symbol, ref _parameterName, ref _selectedType, out var isValid);
            FormInputs.AddCheckBox("Is time clip", ref _isTimeClip);
                
            FormInputs.ApplyIndent();
            if (CustomComponents.DisablableButton("Add", isValid))
            {
                InputsAndOutputs.AddOutputToSymbol(_parameterName, _isTimeClip, _selectedType, symbol);
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
        return result;
    }

    private bool _isTimeClip;
    private string _parameterName = string.Empty;
    private Type _selectedType;                                                                        
}