using ImGuiNET;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Modification;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Dialogs;

internal sealed class CombineToSymbolDialog : ModalDialog
{
    public ChangeSymbol.SymbolModificationResults Draw(Guid symbolGuid, ProjectView projectView, ref string nameSpace,
                                                 ref string combineName, ref string description)
    {
        var result = ChangeSymbol.SymbolModificationResults.Nothing;
        DialogSize = new Vector2(500, 350);

        UpdateTitle(projectView.NodeSelection);
        if (BeginDialog(_title))
        {
            var selectedChildUis = projectView.NodeSelection.GetSelectedChildUis().ToList();
            var selectedSections = projectView.NodeSelection.GetSelectedNodes<Section>().ToList();

            var isAppearing = ImGui.IsWindowAppearing();
            if (isAppearing)
                _tags = SymbolUi.SymbolTags.None;

            if (isAppearing || _slotCountInfo == null)
                UpdateSlotCountInfo(symbolGuid, selectedChildUis);

            // Default to the project the ops are already in, rather than keeping the last used one
            if (isAppearing || _projectToCopyTo == null)
            {
                _projectToCopyTo = projectView.InstView?.Symbol.SymbolPackage as EditableSymbolProject;
                if (_projectToCopyTo == null)
                {
                    EditableSymbolProject.TryGetEditableProjectOfNamespace(nameSpace, out _projectToCopyTo);
                }
            }

            _ = SymbolModificationInputs.DrawProjectDropdown(ref nameSpace, ref _projectToCopyTo);

            var hasTargetProject = _projectToCopyTo != null;
            var symbolNamesValid = false;
            if (hasTargetProject)
            {
                _ = SymbolModificationInputs.DrawSymbolNameAndNamespaceInputs(ref combineName, ref nameSpace, _projectToCopyTo, out symbolNamesValid);

                ImGui.Spacing();

                // Description
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.TextUnformatted("Description");
                ImGui.PopFont();
                ImGui.SetNextItemWidth(460 * T3Ui.UiScaleFactor);
                ImGui.InputTextMultiline("##description", ref description, 1024, new Vector2(450, 60) * T3Ui.UiScaleFactor);

                ParameterWindow.DrawSymbolTagsButton(ref _tags, "##CombineTags");
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Tags");

                if (ImGui.Checkbox("Combine as time clip", ref _shouldBeTimeClip))
                    UpdateSlotCountInfo(symbolGuid, selectedChildUis);

                CustomComponents.TooltipForLastItem("""
                                                    The new symbol appears as a clip on the timeline: it only
                                                    evaluates while the playhead is inside its clip range, and
                                                    keyframe animations inside it travel with the clip.
                                                    """);

                FormInputs.AddVerticalSpace(5);
                CustomComponents.HelpText(_slotCountInfo);

                FormInputs.SetIndentToLeft();
                FormInputs.AddHint("Combining creates a new operator and can't be undone — this clears the undo history.");
                FormInputs.SetIndentToParameters();
            }

            if (hasTargetProject)
            {
                AlignCtaButtons("Cancel", "Combine");
            }
            else
            {
                AlignCtaButtons("Cancel");
            }

            if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Emphasized))
            {
                ImGui.CloseCurrentPopup();
            }

            if (hasTargetProject)
            {
                ImGui.SameLine();
                if (CustomComponents.DrawCtaButton("Combine", symbolNamesValid))
                {
                    if(!SymbolUiRegistry.TryGetSymbolUi(symbolGuid, out var compositionSymbolUi))
                        throw new Exception($"Can't find symbol ui for symbol {symbolGuid}");
                    
                    var newChildId = Combine.CombineAsNewType(compositionSymbolUi, _projectToCopyTo, selectedChildUis, selectedSections, combineName,
                                                              nameSpace, description, _tags, _shouldBeTimeClip);
                    _shouldBeTimeClip = false; // Making timeclips this is normally a one-off operation

                    var compositionOp = projectView.CompositionInstance;
                    if (newChildId != Guid.Empty && compositionOp != null)
                        projectView.NodeSelection.TrySelectCompositionChild(compositionOp, newChildId, add: false);

                    result = ChangeSymbol.SymbolModificationResults.StructureChanged;
                    ImGui.CloseCurrentPopup();
                }
            }

            EndDialogContent();
        }

        EndDialog();
        return result;
    }

    /** The dialog is drawn every frame, so the title is only rebuilt when the number of selected ops changes. */
    private void UpdateTitle(NodeSelection nodeSelection)
    {
        var opCount = 0;
        foreach (var node in nodeSelection.Selection)
        {
            if (node is SymbolUi.Child)
                opCount++;
        }

        if (opCount == _titleOpCount)
            return;

        _titleOpCount = opCount;
        // The ### suffix keeps the popup id stable while the visible title changes.
        _title = opCount == 1
                     ? "Combine 1 op into symbol###CombineIntoSymbol"
                     : $"Combine {opCount} ops into symbol###CombineIntoSymbol";
    }

    private void UpdateSlotCountInfo(Guid compositionSymbolId, List<SymbolUi.Child> selectedChildUis)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(compositionSymbolId, out var compositionSymbolUi))
        {
            _slotCountInfo = string.Empty;
            return;
        }

        Combine.CountNewSlots(compositionSymbolUi.Symbol, selectedChildUis, _shouldBeTimeClip, out var inputCount, out var outputCount);
        _slotCountInfo = $"The new operator will have {Pluralize(inputCount, "input")} and {Pluralize(outputCount, "output")}.";
    }

    private static string Pluralize(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static bool _shouldBeTimeClip;
    private EditableSymbolProject _projectToCopyTo;
    private SymbolUi.SymbolTags _tags;
    private string _slotCountInfo;
    private string _title = "Combine into symbol###CombineIntoSymbol";
    private int _titleOpCount = -1;
}