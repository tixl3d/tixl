#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using GraphUtils = T3.Editor.UiModel.Helpers.GraphUtils;

namespace T3.Editor.Gui.Dialog;

internal sealed class UserNameDialog : ModalDialog
{
    private string _userName = string.Empty;

    protected override void OnShowNextFrame() => _userName = UserSettings.Config.UserName;

    internal void Draw()
    {
        DialogSize = new Vector2(600, 300);
        
        if (BeginDialog("Edit username"))
        {
            FormInputs.AddSectionHeader("Welcome to TiXL.");
            ImGui.TextUnformatted("Enter your nickname to group your projects into a namespace.");
            
            FormInputs.AddVerticalSpace();

            var isValidProjectName = GraphUtils.IsValidProjectName(_userName);
            var warning = isValidProjectName
                              ? null
                              : "Must be valid";

            FormInputs.AddStringInput("Username",
                                      ref _userName!,
                                      "Nickname",
                                      warning,
                                      "Your nickname should be short and not contain spaces or special characters.",
                                      UserSettings.UndefinedUserName,
                                      ImGui.IsWindowAppearing());

            FormInputs.AddVerticalSpace();
            FormInputs.ApplyIndent();

            if (CustomComponents.DisablableButton("Okay", isValidProjectName))
            {
                try
                {
                    UserSettings.Config.UserName = _userName;
                }
                catch (Exception e)
                {
                    Log.Error($"Error while renaming user {e}");
                }

                ImGui.CloseCurrentPopup();
            }

            EndDialogContent();
        }

        EndDialog();
    }
}