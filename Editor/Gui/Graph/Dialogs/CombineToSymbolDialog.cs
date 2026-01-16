using ImGuiNET;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Dialogs;

internal sealed class CombineToSymbolDialog : ModalDialog
{
    public ChangeSymbol.SymbolModificationResults Draw(Guid symbolGuid, ProjectView projectView, ref string nameSpace,
                                                 ref string combineName, ref string description)
    {
        var result = ChangeSymbol.SymbolModificationResults.Nothing;
        DialogSize = new Vector2(500, 350);

        if (BeginDialog("Combine into symbol"))
        {
            var selectedChildUis = projectView.NodeSelection.GetSelectedChildUis().ToList();
            var selectedAnnotations = projectView.NodeSelection.GetSelectedNodes<Annotation>().ToList();

            if (_projectToCopyTo == null)
            {
                EditableSymbolProject.TryGetEditableProjectOfNamespace(nameSpace, out _projectToCopyTo);
            }
            
            _ = SymbolModificationInputs.DrawProjectDropdown(ref nameSpace, ref _projectToCopyTo);

            if (_projectToCopyTo != null)
            {
                _ = SymbolModificationInputs.DrawSymbolNameAndNamespaceInputs(ref combineName, ref nameSpace, _projectToCopyTo, out var symbolNamesValid);

                ImGui.Spacing();

                // Description
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.TextUnformatted("Description");
                ImGui.PopFont();
                ImGui.SetNextItemWidth(460);
                ImGui.InputTextMultiline("##description", ref description, 1024, new Vector2(450, 60));

                ImGui.Checkbox("Combine as time clip", ref _shouldBeTimeClip);
                    
                if (CustomComponents.DisablableButton("Combine", symbolNamesValid, enableTriggerWithReturn: false))
                {
                    if(!SymbolUiRegistry.TryGetSymbolUi(symbolGuid, out var compositionSymbolUi))
                        throw new Exception($"Can't find symbol ui for symbol {symbolGuid}");
                    
                    Combine.CombineAsNewType(compositionSymbolUi, _projectToCopyTo, selectedChildUis, selectedAnnotations, combineName, nameSpace, description,
                                             _shouldBeTimeClip);
                    _shouldBeTimeClip = false; // Making timeclips this is normally a one-off operation
                    result = ChangeSymbol.SymbolModificationResults.StructureChanged;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
            }

            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }

        EndDialog();
        return result;
    }

    private static bool _shouldBeTimeClip;
    private EditableSymbolProject _projectToCopyTo;
}