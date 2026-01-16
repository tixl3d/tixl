using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Dialogs;

public sealed class EditSymbolDescriptionDialog : ModalDialog
{
    public void Draw(Symbol operatorSymbol)
    {
        DialogSize = new Vector2(1100, 700);

        if (BeginDialog("Edit description"))
        {
            var symbolUi = operatorSymbol.GetSymbolUi();
            var desc = symbolUi.Description ?? string.Empty;

            ImGui.PushFont(Fonts.FontLarge);
            ImGui.Text(symbolUi.Symbol.Name);
            ImGui.PopFont();

            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            var modified = false;
            modified |= ImGui.InputTextMultiline("##name", ref desc, 2000, new Vector2(-1, 400), ImGuiInputTextFlags.None);
            symbolUi.Description = desc;

            ImGui.Text("Links...");
            foreach (var l in symbolUi.Links.Values)
            {
                ImGui.PushID(l.Id.GetHashCode());
                    
                ImGui.SetNextItemWidth(150);
                modified |= FormInputs.DrawEnumDropdown(ref l.Type, "type");
                    
                ImGui.SameLine();
                modified |= CustomComponents.DrawInputFieldWithPlaceholder("URL", ref l.Url, 220);
                    
                ImGui.SameLine();
                modified |= CustomComponents.DrawInputFieldWithPlaceholder("Title", ref l.Title, 220);
                    

                ImGui.SameLine();
                modified |= CustomComponents.DrawInputFieldWithPlaceholder("Description", ref l.Description);
                    
                ImGui.SameLine();
                if (CustomComponents.IconButton(Icon.Trash, Vector2.One * ImGui.GetFrameHeight()))
                {
                    modified = true;
                    symbolUi.Links.Remove(l.Id);
                    ImGui.PopID();
                    break; // prevent further iteration on dict
                }
                ImGui.PopID();
            }
                
            if (ImGui.Button("Add link"))
            {
                var newLink = new ExternalLink() { Type = ExternalLink.LinkTypes.TutorialVideo};
                symbolUi.Links.Add(newLink.Id, newLink);
                modified = true;
            }
                
            if(modified)
                symbolUi.FlagAsModified();

            if (ImGui.Button("Close"))
            {
                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }

        EndDialog();
    }
}