using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Dialogs;

internal sealed class LibWarningDialog : ModalDialog
{
    public void Draw(ProjectView components)
    {
        if (BeginDialog("Careful now"))
        {
            ImGui.TextUnformatted("You tried to open a library symbol.\n" +
                                  $"Any change would affect {DependencyCount} operators using it.");
            ImGui.Spacing();
                
                
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
                // the UserSettings modification get rolled-back when the user hits cancel:
                // we can safely assume it was true before (since the pop-up was displayed)
                UserSettings.Config.WarnBeforeLibEdit = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("I know what I'm doing"))
            {
                components.TrySetCompositionOpToChild(HandledInstance.SymbolChildId);
                ImGui.CloseCurrentPopup();
            }

            // no sane way to "mirror" a ref, note: this whole section could be replaced
            // by a simple UserSettings reference, and changing label to "Remind me next time"
            // "Dont ask again" is actually the better/safer mainly bc it's the label we're all used to.
            if(ImGui.TreeNode("More..."))
            {
                var DontWarnAgain = !UserSettings.Config.WarnBeforeLibEdit;
                FormInputs.SetIndent(10);
                if(FormInputs.AddCheckBox("Don't ask me again!", ref DontWarnAgain))
                {
                    DontWarnAgain = !DontWarnAgain;
                    UserSettings.Config.WarnBeforeLibEdit = !UserSettings.Config.WarnBeforeLibEdit;
                }
            }
            EndDialogContent();
            ImGui.TreePop();
        }
        EndDialog();
    }

    public static int DependencyCount=0;
    public static Instance HandledInstance;
}