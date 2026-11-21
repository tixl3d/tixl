#nullable enable
using ImGuiNET;
using T3.Editor.Collab;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Asks for the session password when a join is turned away by a locked session, so a joiner
/// never has to find the password field in the settings first. Closing it abandons the join.
/// </summary>
internal sealed class CollabPasswordDialog : ModalDialog
{
    internal void Draw()
    {
        var collab = CollabManager.Instance;
        if (!collab.IsPasswordRequested)
        {
            _wasShown = false;
            return;
        }

        if (!_wasShown)
        {
            _wasShown = true;
            _hasBeenOpen = false;
            _password = "";
            ShowNextFrame();
        }

        DialogSize = new Vector2(360, 0);
        if (BeginDialog(DialogTitle))
        {
            _hasBeenOpen = true;

            CustomComponents.StylizedText(collab.PasswordRequestMessage, Fonts.FontBold, UiColors.Text);
            if (!string.IsNullOrEmpty(collab.PasswordRequestTarget))
                CustomComponents.StylizedText($"Joining {collab.PasswordRequestTarget}", Fonts.FontSmall, UiColors.TextMuted);

            FormInputs.AddVerticalSpace();

            ImGui.SetNextItemWidth(-1);
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();

            var submitted = ImGui.InputTextWithHint("##collabPassword", "Session password", ref _password, 256,
                                                    ImGuiInputTextFlags.Password | ImGuiInputTextFlags.EnterReturnsTrue);

            FormInputs.AddVerticalSpace();

            var canJoin = !string.IsNullOrEmpty(_password);
            if (CustomComponents.DrawCtaButton("Join", canJoin) || (submitted && canJoin))
            {
                var password = _password;
                _password = "";
                ImGui.CloseCurrentPopup();
                collab.SubmitRequestedPassword(password);
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
                collab.CancelPasswordRequest();
            }

            EndDialogContent();
        }
        else if (_hasBeenOpen)
        {
            // Dismissed with the window's close button.
            collab.CancelPasswordRequest();
        }

        EndDialog();
    }

    private const string DialogTitle = "Session Password";
    private bool _wasShown;
    private bool _hasBeenOpen;
    private string _password = "";
}
