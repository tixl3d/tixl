#nullable enable
using ImGuiNET;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;

namespace T3.Editor.UiModel.ProjectHandling;

internal sealed partial class ProjectView
{
    /// <summary>
    /// Stores the current view of the project so that reopening it continues where the user left off.
    /// Uses ImGui and must be called on the UI thread.
    /// </summary>
    internal void SaveSession()
    {
        if (CompositionInstance == null)
            return;

        SaveUsersViewForCurrentComposition(useLastFrameArea: true);

        // Views opened on an explicit home symbol (skills, missing operators) are not where the user works.
        if (OpenedProject.Package.OverrideHomeGuid != Guid.Empty)
            return;

        var session = new ProjectSession
                          {
                              CompositionPath = ProjectSession.FormatPath(_compositionPath),
                          };

        foreach (var childUi in NodeSelection.GetSelectedChildUis())
        {
            session.SelectedChildIds.Add(childUi.Id.ToString());
        }

        var historyPaths = new List<IReadOnlyList<Guid>>();
        NavigationHistory.CollectRecentPaths(historyPaths, ProjectSession.MaxNavigationHistoryCount);
        foreach (var path in historyPaths)
        {
            session.NavigationHistory.Add(ProjectSession.FormatPath(path));
        }

        if (Focused == this && UiConfig.StateBeforeFocusMode is { } stateBeforeFocusMode)
        {
            session.UiStateBeforeFocusMode = stateBeforeFocusMode;
            if (GraphImageBackground.OutputInstance is { } background)
                session.BackgroundOutputPath = ProjectSession.FormatPath(background.InstancePath);
        }

        if (UserSettings.Config.SaveWindowLayoutsWithProjects)
        {
            if (Focused == this && !LayoutHandling.FocusMode)
            {
                CaptureWindowLayout(out var imGuiSettings, out var windowVisibility);
                session.WindowLayout = imGuiSettings;
                session.WindowLayoutImGuiVersion = ImGui.GetVersion();
                session.WindowVisibility = windowVisibility;
            }
            // The windows show another project's layout, or focus mode's, which is applied anew on reopen.
            // Keep the one stored before.
            else if (ProjectSession.TryLoad(OpenedProject.Package, out var previousSession))
            {
                session.WindowLayout = previousSession.WindowLayout;
                session.WindowLayoutImGuiVersion = previousSession.WindowLayoutImGuiVersion;
                session.WindowVisibility = previousSession.WindowVisibility;
            }
        }

        session.Save(OpenedProject.Package);
    }

    /// <summary>
    /// Restores navigation history and selection of a session. Call after the session's composition has been opened.
    /// </summary>
    internal void RestoreSelectionAndHistory(ProjectSession session)
    {
        var historyPaths = new List<IReadOnlyList<Guid>>(session.NavigationHistory.Count);
        foreach (var pathText in session.NavigationHistory)
        {
            if (ProjectSession.TryParsePath(pathText, out var path))
                historyPaths.Add(path);
        }

        NavigationHistory.Restore(historyPaths);

        var composition = CompositionInstance;
        if (composition == null)
            return;

        foreach (var idText in session.SelectedChildIds)
        {
            if (Guid.TryParse(idText, out var childId))
                NodeSelection.TrySelectCompositionChild(composition, childId);
        }
    }

    /// <summary>
    /// Resumes focus mode if the session was left in it. Call after the session's composition has been opened.
    /// </summary>
    internal void RestoreFocusMode(ProjectSession session)
    {
        if (session.UiStateBeforeFocusMode == null || LayoutHandling.FocusMode)
            return;

        var background = ProjectSession.TryParsePath(session.BackgroundOutputPath, out var backgroundPath)
                             ? Structure.GetInstanceFromIdPath(backgroundPath)
                             : null;
        UiConfig.RestoreFocusMode(this, session.UiStateBeforeFocusMode, background);
    }

    /// <summary>
    /// Captures the session of the focused project, e.g. before the editor shuts down.
    /// </summary>
    internal static void SaveSessionForFocusedProject()
    {
        Focused?.SaveSession();
    }
}
