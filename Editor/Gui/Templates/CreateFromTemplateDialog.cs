#nullable enable
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Editor.Gui.Dialogs;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Templates;

internal sealed class CreateFromTemplateDialog : ModalDialog
{
    public CreateFromTemplateDialog()
    {
        DialogSize = new Vector2(800, 400);
        Padding = 0;
    }
        
    public void Draw()
    {
        if (_selectedTemplateIndex == -1)
        {
            _selectedTemplateIndex = 0;
            ApplyTemplateSwitch();
        }
            
        if (BeginDialog("Create"))
        {
            var graphCanvas = ProjectView.Focused?.GraphView;

            if (graphCanvas == null)
            {
                BlockingWindow.Instance.ShowMessageBox("Can't create from template without open graph window");
                EndDialog();
                return;
            }
                
            //_selectedTemplate = null;
            ImGui.BeginChild("templates", new Vector2(200, -1));
            {
                var windowMin = ImGui.GetWindowPos();
                ImGui.GetWindowDrawList().AddRectFilled(windowMin,
                                                        windowMin + ImGui.GetContentRegionAvail(), 
                                                        UiColors.BackgroundButton);
                FormInputs.SetIndentToParameters();
                    
                //ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10,10));
                //ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(4, 4));
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.One * 2);
                    
                for (var index = 0; index < TemplateDefinition.TemplateDefinitions.Count; index++)
                {
                    ImGui.PushID(index);
                    var template = TemplateDefinition.TemplateDefinitions[index];
                    var isSelected = index == _selectedTemplateIndex;
                    if (isSelected)
                        _selectedTemplate = template;

                    if (ImGui.Selectable("##template.Title", isSelected, ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X, 45)))
                    {
                        _selectedTemplateIndex = index;
                        ApplyTemplateSwitch();
                    }
                        
                    var itemRectMin = ImGui.GetItemRectMin();
                    var itemRectMax = ImGui.GetItemRectMax();
                    var keepCursor = ImGui.GetCursorScreenPos();

                    // Background
                    ImGui.GetWindowDrawList().AddRectFilled(itemRectMin, itemRectMax, isSelected ? UiColors.BackgroundActive : UiColors.BackgroundButton);
                        
                    // Title
                    ImGui.SetCursorScreenPos(itemRectMin + new Vector2(10, 5));
                    ImGui.TextUnformatted(template.Title);
                        
                    // summary
                    ImGui.SetCursorScreenPos(itemRectMin + new Vector2(10, 25));
                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    ImGui.TextWrapped(template.Summary);
                    ImGui.PopStyleColor();
                    ImGui.PopFont();
                        
                    ImGui.SetCursorScreenPos(keepCursor + new Vector2(0,2));
                    ImGui.PopID();
                }

                // ImGui.PopStyleColor(6);
                ImGui.PopStyleVar();
                //ImGui.PopStyleVar();
            }
            ImGui.EndChild();

            ImGui.SameLine();
            //ImGui.PushStyleVar(ImGuiStyleVar., new Vector2(20,20));
                
            ImGui.BeginChild("options", new Vector2(-20, 0), false, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar);
            {
                ImGui.Dummy(new Vector2(20,10));
                    
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);
                ImGui.BeginGroup();
                    
                ImGui.PushFont(Fonts.FontLarge);
                ImGui.TextUnformatted($"Create {_selectedTemplate?.Title}");
                ImGui.PopFont();
                    
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextWrapped(_selectedTemplate?.Documentation);
                ImGui.PopStyleColor();
                ImGui.Dummy(new Vector2(10,10));

                var projectChanged = CustomComponents.DrawProjectDropdown(ref _projectToCopyTo);
                if (projectChanged && _projectToCopyTo != null)
                {
                    _newNameSpace = _projectToCopyTo.CsProjectFile.RootNamespace;
                }
                    
                //_ = SymbolModificationInputs.DrawProjectDropdown(ref _newNameSpace, ref _projectToCopyTo);

                if (_projectToCopyTo != null)
                {
                    _ = SymbolModificationInputs.DrawSymbolNameAndNamespaceInputs(ref _newSymbolName, ref _newNameSpace, _projectToCopyTo, out var isNewSymbolNameValid);

                    FormInputs.AddStringInput("Description", ref _newDescription);

                    ImGui.Dummy(new Vector2(10, 10));

                    if (CustomComponents.DisablableButton("Create",
                                                          isNewSymbolNameValid,
                                                          enableTriggerWithReturn: false))
                    {
                        TemplateUse.TryToApplyTemplate(_selectedTemplate, _newSymbolName, _newNameSpace, _newDescription, _projectToCopyTo);
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.SameLine();
                }

                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndGroup();
            ImGui.EndChild();
            //ImGui.PopStyleVar();
            EndDialogContent();
        }

        EndDialog();
    }

    private void ApplyTemplateSwitch()
    {
        _selectedTemplate = TemplateDefinition.TemplateDefinitions[_selectedTemplateIndex];
        _newSymbolName = _selectedTemplate.DefaultSymbolName ?? "MyOp";
    }
        
    private TemplateDefinition _selectedTemplate = TemplateDefinition.TemplateDefinitions[0];
        
    private string _newSymbolName = "MyNewOp";
    private string _newNameSpace = string.Empty;
    private string _newDescription = string.Empty;

    private int _selectedTemplateIndex = -1;
    private EditableSymbolProject? _projectToCopyTo;
}