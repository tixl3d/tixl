#nullable enable
using System.IO;
using T3.Core.Output;

namespace T3.Editor.UiModel.ProjectHandling;

/// <summary>
/// Loads and caches the active output <see cref="Setup"/> and per-machine
/// <see cref="MachineConfig"/> for opened projects. Setups live at
/// &lt;project&gt;/.meta/&lt;name&gt;.setup.json; every project gets a default setup with the
/// always-present Default output on first access.
/// </summary>
internal static class ProjectSetups
{
    public static bool TryGetActiveSetup(out Setup setup, out MachineConfig machineConfig)
    {
        setup = null!;
        machineConfig = null!;

        var package = ProjectView.Focused?.OpenedProject.Package;
        if (package == null)
            return false;

        var entry = GetOrLoadEntry(package.Folder);
        setup = entry.Setup;
        machineConfig = entry.MachineConfig;
        return true;
    }

    /// <summary>Persists the active setup and machine config of the focused project.</summary>
    public static void SaveActive()
    {
        var package = ProjectView.Focused?.OpenedProject.Package;
        if (package == null)
            return;

        if (!_entriesByProjectFolder.TryGetValue(package.Folder, out var entry))
            return;

        var metaFolder = GetMetaFolder(package.Folder);
        Directory.CreateDirectory(metaFolder);
        entry.Setup.TrySaveToFile(Path.Combine(metaFolder, entry.Setup.Name + Setup.FileSuffix));
        entry.MachineConfig.TrySaveToFile(Path.Combine(metaFolder, MachineConfig.FileName));
    }

    private sealed class ProjectEntry
    {
        public required Setup Setup;
        public required MachineConfig MachineConfig;
    }

    private static ProjectEntry GetOrLoadEntry(string projectFolder)
    {
        if (_entriesByProjectFolder.TryGetValue(projectFolder, out var entry))
            return entry;

        var metaFolder = GetMetaFolder(projectFolder);

        Setup? setup = null;
        if (Directory.Exists(metaFolder))
        {
            foreach (var filePath in Directory.EnumerateFiles(metaFolder, "*" + Setup.FileSuffix))
            {
                if (Setup.TryLoadFromFile(filePath, out setup))
                    break;
            }
        }

        if (setup == null)
        {
            setup = Setup.CreateDefault();
            Directory.CreateDirectory(metaFolder);
            setup.TrySaveToFile(Path.Combine(metaFolder, setup.Name + Setup.FileSuffix));
        }

        var machineConfigPath = Path.Combine(metaFolder, MachineConfig.FileName);
        var machineConfig = new MachineConfig();
        if (File.Exists(machineConfigPath))
            MachineConfig.TryLoadFromFile(machineConfigPath, out machineConfig);

        entry = new ProjectEntry { Setup = setup, MachineConfig = machineConfig };
        _entriesByProjectFolder[projectFolder] = entry;
        return entry;
    }

    private static string GetMetaFolder(string projectFolder)
    {
        return Path.Combine(projectFolder, Setup.FolderName);
    }

    private static readonly Dictionary<string, ProjectEntry> _entriesByProjectFolder = new();
}
