#nullable enable
using System.IO;
using T3.Core.Model;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Show;

/// <summary>
/// Keeps a show node on the project its master runs. In every show-control mode the following node
/// (boygroup client, failover standby, collab guest) opens the active machine's project: one that is
/// already installed switches in place without a restart, one that still has to be received is
/// deployed first and the restart is pinned to it.
/// </summary>
internal static class ProjectFollower
{
    /// <summary>Folder of the project this editor currently shows; null while the hub is up.</summary>
    public static string? OpenProjectFolder => ProjectView.Focused?.OpenedProject?.Package.Folder;

    public static string? OpenProjectName => ProjectView.Focused?.OpenedProject?.Package.DisplayName;

    public static bool IsOpen(string? folder) => folder is { Length: > 0 } && IsSameFolder(OpenProjectFolder, folder);

    /// <summary>Whether the project with this display name (short form accepted) is already shown.</summary>
    public static bool IsOpenName(string? name) => IsSameFolder(OpenProjectFolder, FindByName(name)?.Folder);

    /// <summary>Whether a project with this display name (short form accepted) is installed locally.</summary>
    public static bool HasProjectNamed(string? name) => FindByName(name) != null;

    /// <summary>
    /// Shows the project at this folder, unless it is already shown. Returns false when the folder
    /// is not a loaded project — the caller has to deploy/pin it and restart instead.
    /// </summary>
    public static bool TrySwitchTo(string folder)
    {
        var project = FindByFolder(folder);
        if (project == null)
            return false;

        return TrySwitchTo(project);
    }

    /// <summary>Shows the project with this display name (short form accepted), if it is installed.</summary>
    public static bool TrySwitchToName(string name)
    {
        var project = FindByName(name);
        return project != null && TrySwitchTo(project);
    }

    /// <summary>
    /// Pins a project as the one to open after a restart. Covers both restart paths: the
    /// reopen-last-session loader and the startup-project loader.
    /// </summary>
    public static void PinForRestart(string folder)
    {
        var config = UserSettings.Config;
        config.LastOpenProjectFolder = folder;
        config.StartupProjectFolder = folder;

        // The reopen path is skipped when the operator disabled it; only the startup-project path
        // is left then, and that one is gated by its own master switch.
        if (!config.ReopenLastProjectOnStartup)
            config.StartupEnabled = true;

        UserSettings.Save();
    }

    private static bool TrySwitchTo(EditorSymbolPackage project)
    {
        if (IsSameFolder(OpenProjectFolder, project.Folder))
            return true;

        if (!OpenedProject.TryCreate(project, out var openedProject, out var failureLog) || openedProject == null)
        {
            Log.Warning($"Project follow: could not load '{project.DisplayName}': {failureLog}");
            return false;
        }

        var graphWindow = VisibleGraphWindow();
        if (graphWindow == null || !graphWindow.TrySetToProject(openedProject, tryRestoreViewArea: false))
        {
            Log.Warning($"Project follow: could not show '{project.DisplayName}' in a graph window");
            return false;
        }

        Log.Info($"Project follow: switched to '{project.DisplayName}'");
        return true;
    }

    private static EditorSymbolPackage? FindByFolder(string folder)
    {
        foreach (var package in SymbolPackage.AllPackages)
        {
            if (package is EditorSymbolPackage editorPackage && IsSameFolder(editorPackage.Folder, folder))
                return editorPackage;
        }

        return null;
    }

    /// <summary>
    /// Matches the display name, which for created projects is "&lt;name&gt; (&lt;namespace&gt;)" while
    /// the master advertises the folder name.
    /// </summary>
    private static EditorSymbolPackage? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var package in SymbolPackage.AllPackages)
        {
            if (package is not EditorSymbolPackage editorPackage)
                continue;

            if (string.Equals(editorPackage.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                || editorPackage.DisplayName.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(editorPackage.Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                                 name, StringComparison.OrdinalIgnoreCase))
                return editorPackage;
        }

        return null;
    }

    /// <summary>Prefers the visible graph window — hidden instances can exist while the hub is shown.</summary>
    private static GraphWindow? VisibleGraphWindow()
    {
        foreach (var window in GraphWindow.GraphWindowInstances)
        {
            if (window.Config.Visible)
                return window;
        }

        return GraphWindow.GraphWindowInstances.Count > 0 ? GraphWindow.GraphWindowInstances[0] : null;
    }

    private static bool IsSameFolder(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                                 Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                                 comparison);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
