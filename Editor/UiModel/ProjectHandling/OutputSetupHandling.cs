#nullable enable
using System.IO;
using T3.Editor.Gui.Windows.OutputSetup;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;

namespace T3.Editor.UiModel.ProjectHandling;

/// <summary>
/// Loads and caches the active output <see cref="Setup"/> and per-machine
/// <see cref="MachineConfig"/> for opened projects. Setups live at
/// &lt;project&gt;/.meta/Setups/&lt;name&gt;.setup.json; every project gets a default setup with the
/// always-present Default output on first access. The active setup is the venue the project
/// is currently configured for; switching, renaming, duplicating (GUID-preserving) and deleting are
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

        // A setup edit (re-routing, a new slice, a moved patch) changes what the cards show, so they render once more.
        T3.Core.Output.Rendering.OutputPreviewRefresh.InvalidateAll();
        if (!TryGetFocusedEntry(out var entry, out var setupsFolder))
            return;

        Directory.CreateDirectory(setupsFolder);
        entry.Setup.TrySaveToFile(SetupFilePath(setupsFolder, entry.Setup.Name));
        entry.MachineConfig.ActiveSetupName = entry.Setup.Name;
        entry.MachineConfig.TrySaveToFile(Path.Combine(setupsFolder, MachineConfig.FileName));
    }

    /// <summary>Setup names available for the focused project (from .meta/Setups/*.setup.json).</summary>
    public static void GetAvailableSetupNames(List<string> names)
    {
        names.Clear();
        if (!TryGetFocusedSetupsFolder(out var setupsFolder) || !Directory.Exists(setupsFolder))
            return;

        foreach (var filePath in Directory.EnumerateFiles(setupsFolder, "*" + Setup.FileSuffix))
        {
            var fileName = Path.GetFileName(filePath);
            names.Add(fileName[..^Setup.FileSuffix.Length]);
        }
    }

    public static bool TrySwitchTo(string setupName)
    {
        if (!TryGetFocusedEntry(out var entry, out var setupsFolder))
            return false;

        if (!Setup.TryLoadFromFile(SetupFilePath(setupsFolder, setupName), out var setup, out _))
            return false;

        entry.Setup = setup;
        OutputPresentation.ReleaseAll();
        SaveActive(); // records the new active setup name (and any load-time repair) so the switch survives a restart
        return true;
    }

    /// <summary>
    /// Renames the active setup and its file. Other machines that had it active fall back to the first setup
    /// in the folder, as they do after a delete.
    /// </summary>
    public static bool TryRenameActive(string newName)
    {
        if (!TryGetFocusedEntry(out var entry, out var setupsFolder))
            return false;

        var oldName = entry.Setup.Name;
        if (newName == oldName)
            return false;

        // A case-only rename names the same file on Windows, so it must not count as a collision.
        var isCaseOnlyChange = string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase);
        if (!isCaseOnlyChange && !IsValidNewName(newName, setupsFolder))
        {
            T3.Core.Logging.Log.Warning($"Can't rename setup to \"{newName}\": the name is empty, invalid or already taken.");
            return false;
        }

        var oldPath = SetupFilePath(setupsFolder, oldName);
        try
        {
            if (File.Exists(oldPath))
                File.Move(oldPath, SetupFilePath(setupsFolder, newName));
        }
        catch (Exception e)
        {
            T3.Core.Logging.Log.Warning($"Can't rename setup {oldPath}: {e.Message}");
            return false;
        }

        entry.Setup.Name = newName;
        SaveActive();
        return true;
    }

    /// <summary>GUID-preserving duplication — the venue-swap mechanism. The copy becomes active.</summary>
    public static bool TryDuplicateActive(string newName)
    {
        if (!TryGetFocusedEntry(out var entry, out var setupsFolder) || !IsValidNewName(newName, setupsFolder))
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
        if (!TryGetFocusedEntry(out var entry, out var setupsFolder) || !IsValidNewName(newName, setupsFolder))
            return false;

        entry.Setup = Setup.CreateDefault(newName);
        OutputPresentation.ReleaseAll();
        SaveActive();
        return true;
    }

    /// <summary>Deletes the active setup's file and switches to another one (or a fresh default).</summary>
    public static bool TryDeleteActive()
    {
        if (!TryGetFocusedEntry(out var entry, out var setupsFolder))
            return false;

        var filePath = SetupFilePath(setupsFolder, entry.Setup.Name);
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

    private static bool TryGetFocusedEntry([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ProjectEntry? entry, out string setupsFolder)
    {
        entry = null;
        if (!TryGetFocusedSetupsFolder(out setupsFolder))
            return false;

        var package = ProjectView.Focused?.OpenedProject.Package;
        if (package == null)
            return false;

        entry = GetOrLoadEntry(package.Folder);
        return true;
    }

    private static bool TryGetFocusedSetupsFolder(out string setupsFolder)
    {
        setupsFolder = string.Empty;
        var package = ProjectView.Focused?.OpenedProject.Package;
        if (package == null)
            return false;

        setupsFolder = SetupFiles.FolderIn(package.Folder);
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

        var setupsFolder = SetupFiles.FolderIn(projectFolder);
        SetupFiles.TryLoad(setupsFolder, out var setup, out var machineConfig, out var wasRepaired);

        if (setup == null)
        {
            setup = Setup.CreateDefault();
            Directory.CreateDirectory(setupsFolder);
            setup.TrySaveToFile(SetupFilePath(setupsFolder, setup.Name));
        }
        else if (wasRepaired)
        {
            // Persist the repair right away, so the file on disk stops being broken.
            setup.TrySaveToFile(SetupFilePath(setupsFolder, setup.Name));
        }

        entry = new ProjectEntry { Setup = setup, MachineConfig = machineConfig };
        _entriesByProjectFolder[projectFolder] = entry;
        return entry;
    }

    private static string SetupFilePath(string setupsFolder, string setupName)
    {
        return Path.Combine(setupsFolder, setupName + Setup.FileSuffix);
    }

    private static bool IsValidNewName(string name, string setupsFolder)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return !File.Exists(SetupFilePath(setupsFolder, name));
    }

    private static readonly Dictionary<string, ProjectEntry> _entriesByProjectFolder = new();
}
