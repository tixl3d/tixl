#nullable enable
using System.IO;
using T3.Core.Output;

namespace T3.Editor.UiModel.ProjectHandling;

/// <summary>
/// Loads and caches the active output <see cref="Setup"/> and per-machine
/// <see cref="MachineConfig"/> for opened projects. Setups live at
/// &lt;project&gt;/.meta/&lt;name&gt;.setup.json; every project gets a default setup with the
/// always-present Default output on first access. The active setup is the venue the project
/// is currently configured for; switching, duplicating (GUID-preserving) and deleting are
/// the setup-switcher operations of the output window's setup panel.
/// </summary>
internal static class OutputSetupHandling
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

        // Publish for operators — they only reference Core and resolve GUIDs via ActiveSetup
        ActiveSetup.Current = setup;
        ActiveSetup.Machine = machineConfig;
        return true;
    }

    /// <summary>Persists the active setup and machine config of the focused project.</summary>
    public static void SaveActive()
    {
        if (!TryGetFocusedEntry(out var entry, out var metaFolder))
            return;

        Directory.CreateDirectory(metaFolder);
        entry.Setup.TrySaveToFile(SetupFilePath(metaFolder, entry.Setup.Name));
        entry.MachineConfig.TrySaveToFile(Path.Combine(metaFolder, MachineConfig.FileName));
    }

    /// <summary>Setup names available for the focused project (from .meta/*.setup.json).</summary>
    public static void GetAvailableSetupNames(List<string> names)
    {
        names.Clear();
        if (!TryGetFocusedMetaFolder(out var metaFolder) || !Directory.Exists(metaFolder))
            return;

        foreach (var filePath in Directory.EnumerateFiles(metaFolder, "*" + Setup.FileSuffix))
        {
            var fileName = Path.GetFileName(filePath);
            names.Add(fileName[..^Setup.FileSuffix.Length]);
        }
    }

    public static bool TrySwitchTo(string setupName)
    {
        if (!TryGetFocusedEntry(out var entry, out var metaFolder))
            return false;

        if (!Setup.TryLoadFromFile(SetupFilePath(metaFolder, setupName), out var setup) || setup == null)
            return false;

        entry.Setup = setup;
        return true;
    }

    /// <summary>GUID-preserving duplication — the venue-swap mechanism. The copy becomes active.</summary>
    public static bool TryDuplicateActive(string newName)
    {
        if (!TryGetFocusedEntry(out var entry, out var metaFolder) || !IsValidNewName(newName, metaFolder))
            return false;

        var duplicate = entry.Setup.Duplicate(newName);
        entry.Setup = duplicate;
        SaveActive();
        return true;
    }

    /// <summary>Creates an empty setup (fresh GUIDs — op bindings into it start unresolved). It becomes active.</summary>
    public static bool TryCreateNew(string newName)
    {
        if (!TryGetFocusedEntry(out var entry, out var metaFolder) || !IsValidNewName(newName, metaFolder))
            return false;

        entry.Setup = Setup.CreateDefault(newName);
        SaveActive();
        return true;
    }

    /// <summary>Deletes the active setup's file and switches to another one (or a fresh default).</summary>
    public static bool TryDeleteActive()
    {
        if (!TryGetFocusedEntry(out var entry, out var metaFolder))
            return false;

        var filePath = SetupFilePath(metaFolder, entry.Setup.Name);
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception e)
        {
            T3.Core.Logging.Log.Warning($"Can't delete setup {filePath}: {e.Message}");
            return false;
        }

        var names = new List<string>();
        GetAvailableSetupNames(names);
        if (names.Count > 0)
        {
            TrySwitchTo(names[0]);
        }
        else
        {
            entry.Setup = Setup.CreateDefault();
            SaveActive();
        }

        return true;
    }

    private sealed class ProjectEntry
    {
        public required Setup Setup;
        public required MachineConfig MachineConfig;
    }

    private static bool TryGetFocusedEntry([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ProjectEntry? entry, out string metaFolder)
    {
        entry = null;
        if (!TryGetFocusedMetaFolder(out metaFolder))
            return false;

        var package = ProjectView.Focused?.OpenedProject.Package;
        if (package == null)
            return false;

        entry = GetOrLoadEntry(package.Folder);
        return true;
    }

    private static bool TryGetFocusedMetaFolder(out string metaFolder)
    {
        metaFolder = string.Empty;
        var package = ProjectView.Focused?.OpenedProject.Package;
        if (package == null)
            return false;

        metaFolder = Path.Combine(package.Folder, Setup.FolderName);
        return true;
    }

    private static ProjectEntry GetOrLoadEntry(string projectFolder)
    {
        if (_entriesByProjectFolder.TryGetValue(projectFolder, out var entry))
            return entry;

        var metaFolder = Path.Combine(projectFolder, Setup.FolderName);

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
            setup.TrySaveToFile(SetupFilePath(metaFolder, setup.Name));
        }

        var machineConfigPath = Path.Combine(metaFolder, MachineConfig.FileName);
        var machineConfig = new MachineConfig();
        if (File.Exists(machineConfigPath))
            MachineConfig.TryLoadFromFile(machineConfigPath, out machineConfig);

        entry = new ProjectEntry { Setup = setup, MachineConfig = machineConfig };
        _entriesByProjectFolder[projectFolder] = entry;
        return entry;
    }

    private static string SetupFilePath(string metaFolder, string setupName)
    {
        return Path.Combine(metaFolder, setupName + Setup.FileSuffix);
    }

    private static bool IsValidNewName(string name, string metaFolder)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return !File.Exists(SetupFilePath(metaFolder, name));
    }

    private static readonly Dictionary<string, ProjectEntry> _entriesByProjectFolder = new();
}
