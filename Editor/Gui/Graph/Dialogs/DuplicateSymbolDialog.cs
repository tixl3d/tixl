#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Modification;

namespace T3.Editor.Gui.Dialogs;

internal sealed class DuplicateSymbolDialog : ModalDialog
{
    internal DuplicateSymbolDialog()
    {
        Flags = ImGuiWindowFlags.NoScrollWithMouse;
        DialogSize = new Vector2(500, 450) * T3Ui.UiScaleFactor;
    }    
    
    public event Action? Closed;
        
    /** returns true if modified */
    public ChangeSymbol.SymbolModificationResults Draw(Guid symbolGuid, IEnumerable<SymbolUi.Child> selectedChildUis2, ref string nameSpace, ref string newTypeName, ref string description, bool isReload = false)
    {
        //DialogSize = new Vector2(500, 450) * T3Ui.UiScaleFactor;
        var result = ChangeSymbol.SymbolModificationResults.Nothing;
        

        
        if(isReload && !_completedReloadPrompt)
        {
            //DialogSize = new Vector2(400, 200);
            if (BeginDialog("Changes made to readonly operator"))
            {
                ImGui.TextWrapped("You've made changes to a read-only operator.\nDo you want to save your changes as a new operator?");
                    
                if(ImGui.Button("Yes"))
                {
                    _completedReloadPrompt = true;
                }
                    
                ImGui.SameLine();
                    
                if(ImGui.Button("No"))
                {
                    ImGui.CloseCurrentPopup();
                    Closed?.Invoke();
                }
                    
                EndDialogContent();
            }
                
            EndDialog();
            return ChangeSymbol.SymbolModificationResults.Nothing;
        }

        DialogSize = new Vector2(600, 400);

        if (BeginDialog("Duplicate as new symbol"))
        {
            var selectedChildUis = selectedChildUis2.ToList();
            
            if(selectedChildUis.Count != 1)
                return result;

            if (selectedChildUis[0]?.SymbolChild?.Symbol == null)
            {
                return result;
            }
        
            var s = selectedChildUis[0].SymbolChild.Symbol;
            var selectionChanged = _selectedSymbolId != s.Id;
            if (selectionChanged)
            {
                _projectToCopyTo = s.SymbolPackage as EditableSymbolProject;
                _selectedSymbolId = s.Id;
            }
            
            _ = SymbolModificationInputs.DrawProjectDropdown(ref nameSpace, ref _projectToCopyTo);

            if (_projectToCopyTo != null)
            {
                _ = SymbolModificationInputs.DrawSymbolNameAndNamespaceInputs(ref newTypeName, ref nameSpace, _projectToCopyTo, out var symbolNamesValid);
                ImGui.Spacing();

                FormInputs.DrawInputLabel("Description");
                ImGui.InputTextMultiline("##description", ref description, 1024, new Vector2(450, 60));
                    
                if (CustomComponents.DisablableButton("Duplicate", symbolNamesValid, enableTriggerWithReturn: false))
                {
                    if(!SymbolUiRegistry.TryGetSymbolUi(symbolGuid, out var compositionSymbolUi))
                        throw new InvalidOperationException($"Failed to find symbol ui for {symbolGuid}");
                    
                    var position = selectedChildUis.First().PosOnCanvas + new Vector2(0, 100);

                    Duplicate.DuplicateAsNewType(compositionSymbolUi, _projectToCopyTo, 
                                                 selectedChildUis.First().SymbolChild.Symbol.Id, newTypeName, nameSpace, description,
                                                 position);
                    
                    result = ChangeSymbol.SymbolModificationResults.StructureChanged;
                    T3Ui.Save(false);
                    ImGui.CloseCurrentPopup();
                    _completedReloadPrompt = false;
                    Closed?.Invoke();
                }

                ImGui.SameLine();
            }

            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
                _completedReloadPrompt = false;
                Closed?.Invoke();
            }

            EndDialogContent();
        }
        else
        {
            _completedReloadPrompt = false;
            Closed?.Invoke();
        }

        EndDialog();
        return result;
    }

    private EditableSymbolProject? _projectToCopyTo;
    private bool _completedReloadPrompt;
    private Guid _selectedSymbolId;
}