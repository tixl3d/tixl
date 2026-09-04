using ImGuiNET;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Input;
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
            var selectedSections = projectView.NodeSelection.GetSelectedNodes<Section>().ToList();

            // Default to the project the ops are already in, rather than keeping the last used one
            if (ImGui.IsWindowAppearing() || _projectToCopyTo == null)
            {
                _projectToCopyTo = projectView.InstView?.Symbol.SymbolPackage as EditableSymbolProject;
                if (_projectToCopyTo == null)
                {
                    EditableSymbolProject.TryGetEditableProjectOfNamespace(nameSpace, out _projectToCopyTo);
                }
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
                CustomComponents.TooltipForLastItem("""
                                                    The new symbol appears as a clip on the timeline: it only
                                                    evaluates while the playhead is inside its clip range, and
                                                    keyframe animations inside it travel with the clip.
                                                    """);

                FormInputs.AddHint("Combining creates a new operator and can't be undone — this clears the undo history.");

                if (CustomComponents.DrawCtaButton("Combine", symbolNamesValid))
                {
                    if(!SymbolUiRegistry.TryGetSymbolUi(symbolGuid, out var compositionSymbolUi))
                        throw new Exception($"Can't find symbol ui for symbol {symbolGuid}");
                    
                    Combine.CombineAsNewType(compositionSymbolUi, _projectToCopyTo, selectedChildUis, selectedSections, combineName, nameSpace, description,
                                             _shouldBeTimeClip);
                    _shouldBeTimeClip = false; // Making timeclips this is normally a one-off operation
                    result = ChangeSymbol.SymbolModificationResults.StructureChanged;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
            }

            if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Emphasized))
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