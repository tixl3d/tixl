using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.Gui.Dialogs;

public sealed class RenameInputDialog : ModalDialog
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
            _lastWarning = string.Empty;
        }

        // ImGui.SetNextItemWidth(150);

        //var warning = String.Empty;
        var changed = SymbolModificationInputs.DrawFieldNameInput(symbol, ref _newInputName, out var isValid);

        if (isValid && (isWindowAppearing || changed))
        {
            _lastWarning = null;
        }

        if (isWindowAppearing)
        {
            ImGui.SetKeyboardFocusHere();
        }
        
        FormInputs.ApplyIndent();

        if (CustomComponents.DisablableButton("Rename input", isValid))
        {
            // Fix simulate
            if (!InputsAndOutputs.RenameInput(symbol, _inputId, _newInputName, dryRun: true, out var newWarning))
            {
                _lastWarning = newWarning;
            }
            else
            {
                if (!InputsAndOutputs.RenameInput(symbol, _inputId, _newInputName, dryRun: false, out var warning))
                {
                    Log.Warning(warning);
                }

                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
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
    private static string _lastWarning = string.Empty;
}