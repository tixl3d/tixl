#nullable enable
using System.IO;
using ImGuiNET;
using Sentry;
using T3.Core.Settings;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui;

/// <summary>
/// Help > Show Folder: opens the folders TiXL keeps its files in, and lists the recent sessions with their logs
/// so a session that crashed can be found, opened and shared.
/// </summary>
internal static class ShowFolderMenu
{
    internal static void DrawContent()
    {
        var projectDirectories = UserSettings.Config.ProjectDirectories;
        DrawFolderItem("Projects", projectDirectories.Count > 0 ? projectDirectories[0] : FileLocations.DefaultProjectFolder);

        var currentPackage = ProjectView.Focused?.OpenedProject.Package;
        if (currentPackage != null)
        {
            DrawFolderItem("Current Project", currentPackage.Folder);
        }

        DrawFolderItem("Settings", FileLocations.SettingsDirectory);
        DrawFolderItem("Cache", FileLocations.CacheDirectory);
        DrawFolderItem("Temp", _tempFolder);

        CustomComponents.SeparatorLine();

        if (AppMenuBar.BeginSubMenu("Logs"))
        {
            DrawLogsMenu();
            ImGui.EndMenu();
        }
    }

    private static void DrawLogsMenu()
    {
        var logDirectory = FileWriter.Instance?.LogDirectory;
        if (AppMenuBar.MenuItem("Open Log Folder", isEnabled: logDirectory != null) && logDirectory != null)
        {
            OpenFolder(logDirectory);
        }

        var sessions = RecentSessions.GetSessions(refreshLabels: ImGui.IsWindowAppearing());

        CustomComponents.DrawMenuGroupLabel("Recent Sessions");
        if (sessions.Count == 0)
        {
            AppMenuBar.MenuItem("None in the Last 7 Days", isEnabled: false);
            return;
        }

        var count = Math.Min(sessions.Count, MaxListedSessions);
        for (var sessionIndex = 0; sessionIndex < count; sessionIndex++)
        {
            var session = sessions[sessionIndex];

            // Labels like "2 h ago" repeat, so the row index keeps the ImGui ids apart.
            var id = SessionIdBase + sessionIndex;
            if (session.HasProblem)
            {
                DrawProblemSession(session, id);
                continue;
            }

            if (CustomComponents.DrawMenuItem(id, Icon.None, session.Label, reserveIconColumn: false) && session.LogPath != null)
            {
                CoreUi.Instance.OpenWithDefaultApplication(session.LogPath);
            }

            CustomComponents.TooltipForLastItem(session.Tooltip);
        }
    }

    private static void DrawProblemSession(RecentSessions.Session session, int id)
    {
        // Captured before the submenu opens, which switches the current window.
        var rowStart = ImGui.GetCursorScreenPos();
        var parentDrawList = ImGui.GetWindowDrawList();
        var rowHeight = ImGui.GetFrameHeight();
        var paddingX = ImGui.GetStyle().ItemSpacing.X;

        var isOpen = CustomComponents.DrawSubMenu(id, session.Label);

        // The warning sits in the checkmark column, which these rows don't otherwise use.
        Icons.DrawIconAtScreenPosition(Icon.Warning,
                                       (rowStart + new Vector2(paddingX, rowHeight / 2 - Icons.FontSize / 2)).Floor(),
                                       parentDrawList,
                                       UiColors.StatusAttention);

        if (!isOpen)
        {
            CustomComponents.TooltipForLastItem(session.Tooltip);
            return;
        }

        if (session.CrashReportPath != null && AppMenuBar.MenuItem("Open Crash Report"))
        {
            CoreUi.Instance.OpenWithDefaultApplication(session.CrashReportPath);
        }

        if (session.LogPath != null && AppMenuBar.MenuItem("Open Log"))
        {
            CoreUi.Instance.OpenWithDefaultApplication(session.LogPath);
        }

        if (AppMenuBar.MenuItem("Show in File Browser"))
        {
            CoreUi.Instance.RevealInFileBrowser(session.MainPath);
        }

        CustomComponents.SeparatorLine();

        var canShare = SentrySdk.IsEnabled && !session.IsShared;
        var shareLabel = session.IsShared ? "Shared with Developers" : "Share with Developers...";
        if (AppMenuBar.MenuItem(shareLabel, isChecked: session.IsShared, isEnabled: canShare))
        {
            T3Ui.ShareSessionDialog.ShowFor(session);
        }

        if (!SentrySdk.IsEnabled)
        {
            CustomComponents.TooltipForLastItem("Crash reporting could not be started, see the log.");
        }

        ImGui.EndMenu();
    }

    private static void DrawFolderItem(string label, string folder)
    {
        if (AppMenuBar.MenuItem(label))
        {
            OpenFolder(folder);
        }

        CustomComponents.TooltipForLastItem(folder);
    }

    private static void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Log.Warning($"'{folder}' does not exist yet. TiXL creates it once it has something to put there.");
            return;
        }

        CoreUi.Instance.OpenWithDefaultApplication(folder);
    }

    /** The log folder keeps about 20 sessions; a longer list would outgrow the screen. */
    private const int MaxListedSessions = 15;

    /** Keeps the per-row ids clear of the hashed label ids the other menu items use. */
    private const int SessionIdBase = 0x5E55_0000;

    private static readonly string _tempFolder = FileLocations.TempFolder;
}
