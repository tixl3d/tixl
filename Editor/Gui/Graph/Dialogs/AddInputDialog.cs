#nullable enable

using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.Gui.Dialogs;

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
                
            _ = SymbolModificationInputs.DrawFieldInputs(symbol, ref _parameterName, ref _selectedType, out var isValid);
                
            FormInputs.AddCheckBox("Multi-Input", ref _multiInput);
                
            FormInputs.AddVerticalSpace(5);
            FormInputs.ApplyIndent();
            if (CustomComponents.DisablableButton("Add", isValid))
            {
                InputsAndOutputs.AddInputToSymbol(_parameterName, _multiInput, _selectedType!, symbol);
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