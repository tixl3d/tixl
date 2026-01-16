using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Dialogs;

public sealed class EditNodeOutputDialog : ModalDialog
{
    public void Draw()
    {
        if (BeginDialog("Edit node output"))
        {
            ImGui.TextUnformatted("Define the dirty flag behavior for " + _outputDefinition.Name);

            var symbolChild = _symbolChildUi.SymbolChild;
            var outputEntry = symbolChild.Outputs[_outputDefinition.Id];
                
            var enumType = typeof(DirtyFlagTrigger);
            var values = Enum.GetValues(enumType);
            var valueNames = Enum.GetNames(enumType);
            var currentValueName = Enum.GetName(enumType, outputEntry.DirtyFlagTrigger);

            int index = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (valueNames[i] == currentValueName)
                {
                    index = i;
                    break;
                }
            }

            if (ImGui.Combo("##dirtyFlagTriggerDropDownParam", ref index, valueNames, valueNames.Length))
            {
                var trigger = (DirtyFlagTrigger)Enum.Parse(enumType, valueNames[index]);
                outputEntry.DirtyFlagTrigger = trigger;

                foreach (var instance in _compositionSymbol.InstancesOfSelf)
                {
                    ApplyTriggerToInstanceOutputs(instance);
                }

                void ApplyTriggerToInstanceOutputs(Instance instance)
                {
                    var outputSlot = (from output in instance.Outputs
                                      where output.Id == _outputDefinition.Id
                                      select output).Single();
                    outputSlot.DirtyFlag.Trigger = trigger;
                }
            }

                
            var connectionStyles = new[] { "Default", "Faded Out" };
            var connectionStyleIndex = _symbolChildUi.ConnectionStyleOverrides.ContainsKey(_outputDefinition.Id) ? 1 : 0;
            if (ImGui.Combo("Connection Style##connectionStyle", ref connectionStyleIndex, connectionStyles, connectionStyles.Length))
            {
                if (connectionStyleIndex == 0)
                {
                    _symbolChildUi.ConnectionStyleOverrides.Remove(_outputDefinition.Id);
                }
                else
                {
                    _symbolChildUi.ConnectionStyleOverrides.Add(_outputDefinition.Id, SymbolUi.Child.ConnectionStyles.FadedOut);
                }
            }
                
            if (ImGui.Button("Close"))
            {
                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }

        EndDialog();
    }

    public void OpenForOutput(Symbol compositionSymbol, SymbolUi.Child symbolChildUi, Symbol.OutputDefinition outputDefinition)
    {
        _compositionSymbol = compositionSymbol;
        _symbolChildUi = symbolChildUi;
        _outputDefinition = outputDefinition;
        ShowNextFrame();
    }

    private SymbolUi.Child _symbolChildUi;
    private Symbol.OutputDefinition _outputDefinition;
    private Symbol _compositionSymbol;
}