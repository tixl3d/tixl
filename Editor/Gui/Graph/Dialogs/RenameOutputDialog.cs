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

internal sealed class RenameOutputDialog : ModalDialog
{
    public void Draw()
    {
        if (BeginDialog("Rename output"))
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
        var outputDef = symbol.OutputDefinitions.FirstOrDefault(o => o.Id == _outputId);
        if (outputDef == null)
        {
            ImGui.TextUnformatted("invalid output");
            return;
        }

        if (isWindowAppearing)
        {
            _newOutputName = outputDef.Name;
        }

        var changed = SymbolModificationInputs.DrawFieldNameInput(symbol, "New Output name", "Output", ref _newOutputName, out var isValid);

        if (isValid && (isWindowAppearing || changed))
        {
        }

        if (isWindowAppearing)
        {
            ImGui.SetKeyboardFocusHere();
        }

        FormInputs.ApplyIndent();

        if (CustomComponents.DrawCtaButton("Rename output", isValid))
        {
            // Validate with a dry run before committing the recompile to the undo stack.
            if (InputsAndOutputs.RenameOutput(symbol, _outputId, _newOutputName, dryRun: true, out _))
            {
                UndoRedoStack.AddAndExecute(new RenameSlotCommand(symbol.Id, _outputId, outputDef.Name, _newOutputName, isInput: false));
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Emphasized))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    public void ShowNextFrame(Symbol symbol, Guid outputId)
    {
        ShowNextFrame();
        _symbol = symbol;
        _outputId = outputId;
    }

    private static Symbol _symbol;
    private static Guid _outputId;
    private static string _newOutputName = string.Empty;
}
