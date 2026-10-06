#nullable enable
using System.Collections.Generic;
using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Offers the crashes from earlier sessions that were never reported.
/// </summary>
internal sealed class SendCrashReportsDialog : ModalDialog
{
    /// <summary>Shows the dialog when there is something to send and the user still wants to be asked.</summary>
    internal void ShowIfReportsArePending()
    {
        if (!UserSettings.Config.AskToSendCrashReports)
        {
            return;
        }

        _reports = PendingCrashReports.FindUnsent();
        if (_reports.Count == 0)
        {
            return;
        }

        _dontAskAgain = false;
        ShowNextFrame();
    }

    internal void Draw()
    {
        DialogSize = new Vector2(600, 400);

        if (BeginDialog("Send crash reports?"))
        {
            FormInputs.AddSectionHeader(_reports.Count == 1
                                            ? "TiXL crashed in an earlier session."
                                            : $"TiXL crashed {_reports.Count} times in earlier sessions.");

            ImGui.TextUnformatted("Sending the report helps us fix it. It contains the error, your nickname,\n"
                                  + "and a description of this machine - no file contents and no personal data.");

            FormInputs.AddVerticalSpace();

            foreach (var report in _reports)
            {
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.TextUnformatted($"{report.TimeUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {report.Title}");
                ImGui.PopFont();
            }

            FormInputs.AddVerticalSpace();
            FormInputs.AddCheckBox("Don't ask again", ref _dontAskAgain);

            FormInputs.AddVerticalSpace();
            FormInputs.ApplyIndent();

            if (CustomComponents.DrawCtaButton("Send", true))
            {
                PendingCrashReports.Send(_reports);
                Close();
            }

            ImGui.SameLine();

            // Declining also clears the markers: the reports stay on disk, but a decline that is re-asked
            // on every launch is worse than losing one report.
            if (ImGui.Button("Not now"))
            {
                PendingCrashReports.Discard(_reports);
                Close();
            }

            EndDialogContent();
        }

        EndDialog();
    }

    private void Close()
    {
        if (_dontAskAgain)
        {
            UserSettings.Config.AskToSendCrashReports = false;
            UserSettings.Save();
        }

        _reports = [];
        ImGui.CloseCurrentPopup();
    }

    private IReadOnlyList<PendingCrashReports.Report> _reports = [];
    private bool _dontAskAgain;
}
