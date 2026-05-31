#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using T3.Core.Settings;
using T3.Editor.Gui.UiHelpers;
using T3.Serialization;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Finds a previous TiXL install's user folders and copies selected data (projects, settings,
/// layouts, themes, keymaps) into the current version's folders. All operations copy — the source
/// is never modified, so the older install stays usable.
/// </summary>
internal static partial class PreviousVersionImport
{
    /// <summary>A sibling TiXL user folder from a different version, usable as an import source.</summary>
    internal sealed record PreviousVersion(string FolderName, string SettingsPath, string ProjectsPath, bool IsStable);

    /// <summary>One project found in a previous version's project folder.</summary>
    internal sealed record ProjectEntry(string Name, string SourcePath, bool AlreadyImported);

    internal const string LayoutsSubfolder = "Layouts";

    /// <summary>
    /// Picks the best previous version to offer as an import source: highest <c>major.minor</c> that
    /// isn't the running one, preferring a stable folder over an alpha folder of the same minor.
    /// </summary>
    internal static bool TryFindBest(out PreviousVersion? best)
    {
        best = null;

        var currentName = FileLocations.VersionedAppFolderName;
        var settingsRoot = Directory.GetParent(FileLocations.SettingsDirectory)?.FullName;
        var projectsRoot = Directory.GetParent(FileLocations.DefaultProjectFolder)?.FullName;

        if (settingsRoot == null || !Directory.Exists(settingsRoot))
            return false;

        var bestVersion = new Version(0, 0);
        foreach (var dir in Directory.GetDirectories(settingsRoot))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase))
                continue;

            var match = FolderNamePattern().Match(name);
            if (!match.Success)
                continue;

            var version = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            var isStable = !match.Groups[3].Success;

            // Higher version wins; on a tie, stable beats alpha.
            var comparison = version.CompareTo(bestVersion);
            if (best != null && comparison < 0)
                continue;
            if (best != null && comparison == 0 && !(isStable && !best.IsStable))
                continue;

            var projectsPath = projectsRoot != null ? Path.Combine(projectsRoot, name) : string.Empty;
            best = new PreviousVersion(name, dir, projectsPath, isStable);
            bestVersion = version;
        }

        return best != null;
    }

    /// <summary>
    /// True if the current settings folder already holds user data — i.e. this is a routine version
    /// bump rather than a fresh folder, so the import section is hidden.
    /// </summary>
    /// <remarks>
    /// Keyed off the version marker and <c>userSettings.json</c>, both written only after a real
    /// session. Theme/layout/keymap defaults can be written during startup, so their presence is
    /// not a reliable "folder was used before" signal.
    /// </remarks>
    internal static bool CurrentFolderHasPriorData()
    {
        return VersionMarker.LastRunVersion != null
               || File.Exists(Path.Combine(FileLocations.SettingsDirectory, "userSettings.json"));
    }

    internal static bool HasProjects(PreviousVersion previous) => HasFiles(previous.ProjectsPath);
    internal static bool HasSettingsFile(PreviousVersion previous) => File.Exists(Path.Combine(previous.SettingsPath, "userSettings.json"));
    internal static bool HasSubfolder(PreviousVersion previous, string subfolder) => HasFiles(Path.Combine(previous.SettingsPath, subfolder));

    /// <summary>Approximate on-disk size of the previous version's project tree, for display before import.</summary>
    internal static long TryGetProjectsSizeBytes(PreviousVersion previous)
    {
        try
        {
            if (!Directory.Exists(previous.ProjectsPath))
                return 0;

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(previous.ProjectsPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // Skip files we can't stat; the figure is only an estimate for the user.
                }
            }

            return total;
        }
        catch (Exception e)
        {
            Log.Debug($"Could not measure previous project folder: {e.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Copies the allowlisted keys from the previous <c>userSettings.json</c> into the live settings
    /// and saves. Deliberately narrow — only values that don't drift between versions are copied;
    /// layouts, themes, and keymaps are separate import categories.
    /// </summary>
    internal static void ImportSettingsAllowlist(PreviousVersion previous)
    {
        var previousFile = Path.Combine(previous.SettingsPath, "userSettings.json");
        var previousConfig = JsonUtils.TryLoadingJson<UserSettings.ConfigData>(previousFile);
        if (previousConfig == null)
        {
            Log.Warning($"Could not read settings to import from {previousFile}");
            return;
        }

        var current = UserSettings.Config;
        current.UserName = previousConfig.UserName;
        current.UiScaleFactor = previousConfig.UiScaleFactor;
        current.KeyBindingName = previousConfig.KeyBindingName;
        current.ColorThemeName = previousConfig.ColorThemeName;

        UserSettings.Save();
    }

    /// <summary>Copies a settings subfolder (Layouts / Themes / KeyBindings) from the previous version, overwriting matching files.</summary>
    internal static void ImportSubfolder(PreviousVersion previous, string subfolder)
    {
        var source = Path.Combine(previous.SettingsPath, subfolder);
        if (!Directory.Exists(source))
            return;

        CopyDirectory(source, Path.Combine(FileLocations.SettingsDirectory, subfolder), null);
    }

    /// <summary>
    /// Lists the projects in the previous version's project folder. Each immediate subfolder is a
    /// project; a project is "already imported" when a folder of the same name exists in the current
    /// default project folder.
    /// </summary>
    internal static IReadOnlyList<ProjectEntry> EnumerateProjects(PreviousVersion previous)
    {
        if (!Directory.Exists(previous.ProjectsPath))
            return [];

        var entries = new List<ProjectEntry>();
        foreach (var dir in Directory.GetDirectories(previous.ProjectsPath))
        {
            var name = Path.GetFileName(dir);
            var alreadyImported = Directory.Exists(Path.Combine(FileLocations.DefaultProjectFolder, name));
            entries.Add(new ProjectEntry(name, dir, alreadyImported));
        }

        return entries;
    }

    /// <summary>Copies a single project folder into the current default project folder, skipping build output.</summary>
    internal static void ImportProject(ProjectEntry project)
    {
        if (!Directory.Exists(project.SourcePath))
            return;

        CopyDirectory(project.SourcePath, Path.Combine(FileLocations.DefaultProjectFolder, project.Name), _projectExcludedDirs);
    }

    private static void CopyDirectory(string source, string target, ISet<string>? excludedDirNames)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (excludedDirNames != null && excludedDirNames.Contains(name))
                continue;

            CopyDirectory(dir, Path.Combine(target, name), excludedDirNames);
        }
    }

    private static bool HasFiles(string path)
    {
        return !string.IsNullOrEmpty(path)
               && Directory.Exists(path)
               && Directory.EnumerateFileSystemEntries(path).Any();
    }

    private static readonly HashSet<string> _projectExcludedDirs = new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".temp" };

    [GeneratedRegex(@"^TiXL(\d+)\.(\d+)(?:-(.+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex FolderNamePattern();
}
