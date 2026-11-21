#nullable enable
using ImGuiNET;
using T3.Editor.App.Startup;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// The "Startup" panel of the settings window: which project opens on launch, whether the editor
/// starts with the operating system, and automatic restart after crashes — the kiosk/show-machine
/// corner of the settings.
/// </summary>
internal sealed partial class SettingsWindow
{
    private static void DrawStartupPanel(ref bool changed)
    {
        var config = UserSettings.Config;

        PanelLayout.BeginSection("Startup automation",
                                 "For kiosk and show machines: open a project on launch, start with the computer and restart after crashes.");

        var enabled = config.StartupEnabled;
        if (FormInputs.AddCheckBox("Enable",
                                   ref enabled,
                                   """
                                   Turns startup automation on: the startup project, login autostart and
                                   crash restart. When off, TiXL starts normally with the Hub and nothing
                                   runs or restarts automatically.
                                   """,
                                   false))
        {
            config.StartupEnabled = enabled;
            if (!enabled && config.AutostartWithSystem)
            {
                // The OS autostart entry is externally visible; removing it keeps the machine
                // clean instead of leaving TiXL launching at login without its automation.
                if (OsAutostart.TrySetEnabled(false, out var error))
                {
                    config.AutostartWithSystem = false;
                }
                else
                {
                    Log.Warning($"Could not remove the autostart entry: {error}");
                }
            }

            changed = true;
        }

        PanelLayout.EndSection();

        if (!config.StartupEnabled)
            return;

        PanelLayout.BeginSection("Project autoload");

        var selected = config.StartupProjectFolder;

        if (FormInputs.AddDropdown(ref selected,
                                   CachedProjectOptions(),
                                   "Project at startup",
                                   GetProjectDisplayName,
                                   """
                                   The selected project opens automatically on the next launch.
                                   Choose "Hub" to start without a project.
                                   """))
        {
            config.StartupProjectFolder = selected;
            changed = true;
        }

        PanelLayout.EndSection();

        PanelLayout.BeginSection("Start with the computer");

        var autostart = config.AutostartWithSystem;
        if (FormInputs.AddCheckBox("Start TiXL when you log in",
                                   ref autostart,
                                   $"""
                                    Registers this TiXL build to start at login and load the selected
                                    startup project. The entry follows the platform convention
                                    ({OsAutostart.DescribeEntry()}) and can also be managed from your
                                    OS's autostart settings.
                                    """,
                                   false))
        {
            if (OsAutostart.TrySetEnabled(autostart, out var error))
            {
                config.AutostartWithSystem = autostart;
            }
            else
            {
                config.AutostartWithSystem = false;
                Log.Warning($"Could not {(autostart ? "install" : "remove")} the autostart entry: {error}");
            }

            changed = true;
        }

        PanelLayout.ValueRow("Entry", OsAutostart.DescribeEntry(), UiColors.TextMuted);

        PanelLayout.EndSection();

        PanelLayout.BeginSection("Crash protection");

        changed |= FormInputs.AddCheckBox("Restart automatically after a crash",
                                          ref config.CrashAutoRestart,
                                          """
                                          A small supervisor process relaunches the editor when it crashes,
                                          reloading the startup project. Normal quits and explicit kills
                                          (task manager, kill) are not restarted, and a project that
                                          crashes the editor at startup gives up after several attempts.
                                          """,
                                          false);

        PanelLayout.ValueRow("Watchdog",
                             CrashWatchdog.IsChildProcess
                                 ? "Active: this instance is supervised."
                                 : "Becomes active on the next start.",
                             CrashWatchdog.IsChildProcess ? UiColors.StatusActivated : UiColors.TextMuted);

        PanelLayout.EndSection();
    }

    /// <summary>Dropdown label for a project folder; empty shows the Hub, unknown folders stay visible.</summary>
    private static string GetProjectDisplayName(string folder)
    {
        if (string.IsNullOrEmpty(folder))
            return "Hub  (no project)";

        foreach (var project in EditableSymbolProject.AllProjects)
        {
            if (!string.Equals(project.Folder, folder, StringComparison.Ordinal))
                continue;

            var rootName = project.RootNamespace.Split(".")[^1];
            return rootName == project.RootNamespace
                       ? rootName
                       : $"{rootName}  ({project.RootNamespace})";
        }

        return folder + "  (missing)";
    }

    /// <summary>The dropdown options, cached so the draw path does not rebuild the list every
    /// frame; refreshed when the project list instance changes (a project opened or closed).</summary>
    private static List<string> CachedProjectOptions()
    {
        var allProjects = EditableSymbolProject.AllProjects;
        var options = _projectOptions;
        if (options == null || !ReferenceEquals(allProjects, _projectOptionsSource))
        {
            options = new List<string> { string.Empty };
            foreach (var project in allProjects)
                options.Add(project.Folder);

            _projectOptions = options;
            _projectOptionsSource = allProjects;
        }

        return options;
    }

    private static List<string>? _projectOptions;
    private static IEnumerable<EditableSymbolProject>? _projectOptionsSource;
}
