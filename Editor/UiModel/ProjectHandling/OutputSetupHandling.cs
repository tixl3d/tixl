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
    /// <summary>
    /// Publishes the focused project's setup to <see cref="ActiveSetup"/> once per frame — operators only
    /// reference Core and resolve GUIDs against it. Publication must not depend on any window being open or
    /// any UI code happening to query the setup, so this runs from the frame loop, not from a getter.
    /// </summary>
    public static void UpdateFrame()
    {
        var package = ProjectView.Focused?.OpenedProject.Package;
        if (package == null)
        {
            ActiveSetup.Current = null;
            ActiveSetup.Machine = null;
            return;
        }

        var entry = GetOrLoadEntry(package.Folder);
        ActiveSetup.Current = entry.Setup;
        ActiveSetup.Machine = entry.MachineConfig;
    }

    /// <summary>Drops a closed project's cached setup, so reopening reloads from disk.</summary>
    public static void OnProjectClosed(string projectFolder)
    {
        _entriesByProjectFolder.Remove(projectFolder);
    }

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
        if (!TryGetFocusedEntry(out var entry, out var metaFolder))
            return;

        Directory.CreateDirectory(metaFolder);
        entry.Setup.TrySaveToFile(SetupFilePath(metaFolder, entry.Setup.Name));
        entry.MachineConfig.TrySaveToFile(Path.Combine(metaFolder, MachineConfig.FileName));

        // Remember which setup is active in the project's settings (.t3ui), so a restart reopens the same venue.
        var symbolUi = ProjectView.Focused?.RootInstance?.Symbol.GetSymbolUi();
        if (symbolUi is { ReadOnly: false } && symbolUi.ActiveOutputSetupName != entry.Setup.Name)
        {
            symbolUi.ActiveOutputSetupName = entry.Setup.Name;
            symbolUi.FlagAsModified();
        }
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
        SaveActive(); // records the new active setup name so the switch survives a restart
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

        // Load the machine config first — it remembers which setup this machine last had active.
        var machineConfigPath = Path.Combine(metaFolder, MachineConfig.FileName);
        var machineConfig = new MachineConfig();
        if (File.Exists(machineConfigPath))
            MachineConfig.TryLoadFromFile(machineConfigPath, out machineConfig);

        Setup? setup = null;
        if (Directory.Exists(metaFolder))
        {
            // Prefer the setup the project last had active (from its .t3ui settings); fall back to the first on
            // disk if it's gone or none was recorded.
            var activeName = ProjectView.Focused?.RootInstance?.Symbol.GetSymbolUi()?.ActiveOutputSetupName;
            if (!string.IsNullOrEmpty(activeName))
            {
                var preferred = SetupFilePath(metaFolder, activeName);
                if (File.Exists(preferred))
                    Setup.TryLoadFromFile(preferred, out setup);
            }

            if (setup == null)
            {
                foreach (var filePath in Directory.EnumerateFiles(metaFolder, "*" + Setup.FileSuffix))
                {
                    if (Setup.TryLoadFromFile(filePath, out setup))
                        break;
                }
            }
        }

        if (setup == null)
        {
            setup = Setup.CreateDefault();
            Directory.CreateDirectory(metaFolder);
            setup.TrySaveToFile(SetupFilePath(metaFolder, setup.Name));
        }

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
