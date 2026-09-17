#nullable enable
using System.IO;
using T3.Editor.Gui.Windows.OutputSetup;
using T3.Core.DataTypes.Vector;
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
        ResolveCanvasResolutions(entry.Setup, entry.MachineConfig);
    }

    /// <summary>Drops a closed project's cached setup, so reopening reloads from disk.</summary>
    public static void OnProjectClosed(string projectFolder)
    {
        _entriesByProjectFolder.Remove(projectFolder);
        OutputPresentation.ReleaseAll();
    }

    /// <summary>
    /// The setup and machine config <see cref="UpdateFrame"/> published for this frame. Only a reader: no disk
    /// IO, so it is safe inside per-frame draws. False until the first publication or while no project is focused.
    /// </summary>
    public static bool TryGetActiveSetup(out Setup setup, out MachineConfig machineConfig)
    {
        setup = ActiveSetup.Current!;
        machineConfig = ActiveSetup.Machine!;
        return setup != null && machineConfig != null;
    }

    /// <summary>
    /// Bumped on every save of the active setup or machine config — every mutation funnels through
    /// <see cref="SaveActive"/> (commands, undo, sync, repair). Views key per-structure caches (labels,
    /// connection lists) on it instead of rebuilding them per frame.
    /// </summary>
    public static int StructureVersion { get; private set; }

    /// <summary>Persists the active setup and machine config of the focused project.</summary>
    public static void SaveActive()
    {
        StructureVersion++;
        if (!TryGetFocusedEntry(out var entry, out var metaFolder))
            return;

        Directory.CreateDirectory(metaFolder);
        entry.Setup.TrySaveToFile(SetupFilePath(metaFolder, entry.Setup.Name));
        entry.MachineConfig.ActiveSetupName = entry.Setup.Name;
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

        if (!Setup.TryLoadFromFile(SetupFilePath(metaFolder, setupName), out var setup, out _))
            return false;

        entry.Setup = setup;
        OutputPresentation.ReleaseAll();
        SaveActive(); // records the new active setup name (and any load-time repair) so the switch survives a restart
        return true;
    }

    /// <summary>GUID-preserving duplication — the venue-swap mechanism. The copy becomes active.</summary>
    public static bool TryDuplicateActive(string newName)
    {
        if (!TryGetFocusedEntry(out var entry, out var metaFolder) || !IsValidNewName(newName, metaFolder))
            return false;

        var duplicate = entry.Setup.Duplicate(newName);
        entry.Setup = duplicate;
        OutputPresentation.ReleaseAll();
        SaveActive();
        return true;
    }

    /// <summary>Creates an empty setup (fresh GUIDs — op bindings into it start unresolved). It becomes active.</summary>
    public static bool TryCreateNew(string newName)
    {
        if (!TryGetFocusedEntry(out var entry, out var metaFolder) || !IsValidNewName(newName, metaFolder))
            return false;

        entry.Setup = Setup.CreateDefault(newName);
        OutputPresentation.ReleaseAll();
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
            OutputPresentation.ReleaseAll();
            SaveActive();
        }

        return true;
    }

    private sealed class ProjectEntry
    {
        public required Setup Setup;
        public required MachineConfig MachineConfig;
    }

    /// <summary>
    /// Fills each output's <see cref="OutputDefinition.ResolvedResolution"/>: its own canvas size, or the size
    /// of the plug bound to it when that is left at 0×0. Done here rather than in the model because a binding
    /// is machine state — the setup file stays free of display numbering. Fitted patches are re-derived right
    /// after, so a display of another aspect re-fits them before anything draws this frame.
    /// </summary>
    private static void ResolveCanvasResolutions(Setup setup, MachineConfig machineConfig)
    {
        SetupFiles.ResolveCanvasResolutions(setup, machineConfig, _resolutionOfBinding);
    }

    /** Cached so the per-frame resolve doesn't allocate a closure for every output. */
    private static readonly Func<PlugBinding?, Int2> _resolutionOfBinding =
        binding =>
        {
            var plugId = Plugs.BoundPlugId(binding);
            return plugId == Guid.Empty ? SetupFiles.UnboundResolution : Plugs.PlugResolution(plugId);
        };

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

    /// <summary>
    /// Loads from disk on first access per project. Only <see cref="UpdateFrame"/> and the switcher operations
    /// call this; per-frame readers go through <see cref="TryGetActiveSetup"/> and never touch the disk.
    /// </summary>
    private static ProjectEntry GetOrLoadEntry(string projectFolder)
    {
        if (_entriesByProjectFolder.TryGetValue(projectFolder, out var entry))
            return entry;

        var metaFolder = Path.Combine(projectFolder, Setup.FolderName);
        SetupFiles.TryLoad(metaFolder, out var setup, out var machineConfig, out var wasRepaired);

        if (setup == null)
        {
            setup = Setup.CreateDefault();
            Directory.CreateDirectory(metaFolder);
            setup.TrySaveToFile(SetupFilePath(metaFolder, setup.Name));
        }
        else if (wasRepaired)
        {
            // Persist the repair right away, so the file on disk stops being broken.
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
