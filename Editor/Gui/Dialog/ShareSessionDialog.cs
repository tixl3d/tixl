#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Confirms sending a session that crashed or ended unexpectedly to the developers, with an optional note.
/// </summary>
internal sealed class ShareSessionDialog : ModalDialog
{
    internal void ShowFor(RecentSessions.Session session)
    {
        _session = session;
        _comment = string.Empty;

        var sentFiles = session.CrashReportPath != null && session.LogPath != null
                            ? "the crash report and the log"
                            : session.CrashReportPath != null
                                ? "the crash report"
                                : "the log";

        _explanation = $"""
                        This sends {sentFiles} of this session to the TiXL developers, together with your nickname.
                        Logs list the files and projects TiXL worked with, so they can contain folder and project names.
                        """;
        ShowNextFrame();
    }

    internal void Draw()
    {
        DialogSize = new Vector2(600, 420);

        if (BeginDialog("Share with Developers"))
        {
            if (_session != null)
            {
                DrawContent(_session);
            }

            EndDialogContent();
        }

        EndDialog();
    }

    private void DrawContent(RecentSessions.Session session)
    {
        FormInputs.AddSectionHeader(session.ProblemTitle);

        ImGui.TextWrapped(_explanation);

        FormInputs.AddVerticalSpace();
        ImGui.TextUnformatted("What were you doing when it happened? (optional)");
        ImGui.InputTextMultiline("##comment", ref _comment, MaxCommentLength, new Vector2(-1, 100 * T3Ui.UiScaleFactor));

        FormInputs.AddVerticalSpace();
        FormInputs.ApplyIndent();

        AlignCtaButtons("Cancel", "Share");
        if (CustomComponents.DrawCtaButton("Cancel", Icon.None, CustomComponents.ButtonStates.Default))
        {
            Close();
        }

        ImGui.SameLine();
        if (CustomComponents.DrawCtaButton("Share", true))
        {
            RecentSessions.Share(session, _comment);
            ActionToast.Show("{Shared with the developers}  thank you!");
            Close();
        }
    }

    private void Close()
    {
        _session = null;
        ImGui.CloseCurrentPopup();
    }

    private const uint MaxCommentLength = 2000;

    private RecentSessions.Session? _session;
    private string _comment = string.Empty;
    private string _explanation = string.Empty;
}
