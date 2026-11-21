#nullable enable
using System.IO;
using ImGuiNET;
using T3.Core.Logging;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.App.Startup;

/// <summary>
/// Opens the project selected in Settings → Startup ("Project at startup") once after launch,
/// with the same behavior as clicking it in the Hub's project list.
/// </summary>
internal static class StartupProjectLoader
{
    private static bool _checked;

    /** Frames to wait for a graph window to exist before giving up (a few seconds). */
    private const int MaxFramesToAwaitGraphWindow = 600;

    /// <summary>Called once per frame; does its work on the first frame with a complete layout.</summary>
    internal static void CheckOncePerLaunch()
    {
        if (_checked || ImGui.GetFrameCount() <= 2)
            return;

        // An explicit --project argument always wins: scripted and launcher-driven sessions don't
        // depend on the Startup setting.
        var cliFolder = Program.ProjectFolderArgument;

        // The Startup master switch gates the autoload: an editor with it disabled starts
        // normally with the Hub even when a startup project is still configured.
        if (!string.IsNullOrWhiteSpace(cliFolder))
        {
            var graphWindowForCli = GetGraphWindow();
            if (graphWindowForCli == null && ImGui.GetFrameCount() <= MaxFramesToAwaitGraphWindow)
                return;

            _checked = true;
            if (!TryOpen(cliFolder))
                Log.Warning($"Could not open the project folder passed with --project: {cliFolder}");
            return;
        }

        if (!UserSettings.Config.StartupEnabled)
        {
            _checked = true;
            return;
        }

        var folder = UserSettings.Config.StartupProjectFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            _checked = true;
            return;
        }

        var graphWindow = GetGraphWindow();
        if (graphWindow == null && ImGui.GetFrameCount() <= MaxFramesToAwaitGraphWindow)
            return;

        _checked = true;

        var project = FindProject(folder);
        if (project == null)
        {
            Log.Warning($"The startup project '{folder}' no longer exists — starting with the Hub.");
            return;
        }

        if (!TryOpen(project, graphWindow))
            return;

        Log.Info($"Startup project '{project.DisplayName}' loaded.");
    }

    /// <summary>
    /// Opens a project exactly as the Hub and the startup setting do. Also used by boygrouping: a
    /// node that just received a bootstrapped project opens it in place instead of restarting the
    /// whole editor (a deploy restart only exists because a *running* project cannot be replaced).
    /// </summary>
    internal static bool TryOpen(string folder)
    {
        var project = FindProject(folder);
        if (project == null)
        {
            Log.Warning($"Project '{folder}' is not known yet — cannot open it in place.");
            return false;
        }

        return TryOpen(project, GetGraphWindow());
    }

    private static bool TryOpen(EditableSymbolProject project, GraphWindow? graphWindow)
    {
        if (!OpenedProject.TryCreate(project, out var openedProject, out var failureLog) || openedProject == null)
        {
            Log.Warning($"Could not load the project '{project.DisplayName}': {failureLog}");
            return false;
        }

        if (graphWindow == null || !graphWindow.TrySetToProject(openedProject))
        {
            Log.Warning($"Could not open the project '{project.DisplayName}' in a graph window.");
            return false;
        }

        return true;
    }

    private static EditableSymbolProject? FindProject(string folder)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string? normalized = null;
        foreach (var project in EditableSymbolProject.AllProjects)
        {
            normalized ??= Path.GetFullPath(folder);
            if (string.Equals(Path.GetFullPath(project.Folder), normalized, comparison))
                return project;
        }

        return null;
    }

    /// <summary>Prefer the visible graph window — hidden instances can exist while the Hub is shown.</summary>
    private static GraphWindow? GetGraphWindow()
    {
        GraphWindow? graphWindow = null;
        foreach (var window in GraphWindow.GraphWindowInstances)
        {
            if (!window.Config.Visible)
                continue;

            graphWindow = window;
            break;
        }

        return graphWindow ?? (GraphWindow.GraphWindowInstances.Count > 0 ? GraphWindow.GraphWindowInstances[0] : null);
    }
}
