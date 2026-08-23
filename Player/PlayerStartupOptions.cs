#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;
using CommandLine.Text;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Serialization;
using T3.SystemUi;

namespace T3.Player;

/// <summary>
/// Everything the player needs to decide before creating its window. Resolved in layers:
/// project defaults from <see cref="ExportSettings"/>, then the user's last-used values, then the
/// command line, then (unless skipped) the startup dialog.
/// </summary>
internal sealed class PlayerStartupOptions
{
    /// <summary>Index into <see cref="IDisplayProvider.GetDisplays"/>; -1 selects the primary display.</summary>
    public int DisplayIndex = -1;

    /// <summary>Remembered with the index so a reordered display setup can still be matched by name.</summary>
    public string? DisplayName;

    public int Width = 1920;
    public int Height = 1080;
    public bool Fullscreen = true;
    public bool ShowLogs;
    public bool Loop;
    public bool VSync = true;

    public PlayerStartupOptions Clone() => (PlayerStartupOptions)MemberwiseClone();

    /// <summary>
    /// Parses the command line. Returns false when the process should exit (help requested or
    /// unparsable arguments); <paramref name="message"/> then holds the help text.
    /// </summary>
    public static bool TryParseCommandLine(string[] args, string applicationTitle, string author, out CommandLineArgs parsed, out string message)
    {
        var parser = new Parser(config =>
                                {
                                    config.HelpWriter = null;
                                    config.AutoVersion = false;
                                });
        var result = parser.ParseArguments<CommandLineArgs>(args);
        var helpText = HelpText.AutoBuild(result,
                                          h =>
                                          {
                                              h.AdditionalNewLineAfterOption = false;
                                              h.Heading = applicationTitle;
                                              h.Copyright = author;
                                              h.AutoVersion = false;
                                              return h;
                                          },
                                          e => e);

        CommandLineArgs? options = null;
        result.WithParsed(o => options = o);

        parsed = options ?? new CommandLineArgs();
        message = helpText;
        return options != null;
    }

    /// <summary>
    /// Builds the options the dialog (or a dialog-less start) begins with.
    /// </summary>
    public static PlayerStartupOptions Resolve(ExportSettings exportSettings, CommandLineArgs commandLine, string lastUsedPath)
    {
        var export = exportSettings.Export;
        var options = new PlayerStartupOptions
                          {
                              Width = export.PreferredWidth,
                              Height = export.PreferredHeight,
                              Fullscreen = export.DefaultWindowMode == WindowMode.Fullscreen,
                              ShowLogs = export.ShowLogs,
                          };

        if (commandLine.Reset)
        {
            TryDeleteLastUsed(lastUsedPath);
        }
        else if (File.Exists(lastUsedPath) && JsonUtils.TryLoadingJson(lastUsedPath, out PlayerStartupOptions? lastUsed))
        {
            options = lastUsed;
        }

        if (commandLine.Display.HasValue)
        {
            options.DisplayIndex = commandLine.Display.Value;
            options.DisplayName = null;
        }

        if (commandLine.Width.HasValue)
            options.Width = commandLine.Width.Value;

        if (commandLine.Height.HasValue)
            options.Height = commandLine.Height.Value;

        if (commandLine.Windowed)
            options.Fullscreen = false;

        if (commandLine.Fullscreen)
            options.Fullscreen = true;

        if (commandLine.ShowLogs)
            options.ShowLogs = true;

        if (commandLine.Loop)
            options.Loop = true;

        if (commandLine.NoVsync)
            options.VSync = false;

        return options;
    }

    /// <summary>
    /// Picks the display for these options, falling back to the primary one when the saved display is gone.
    /// </summary>
    public DisplayInfo ResolveDisplay(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0)
            throw new InvalidOperationException("No displays found");

        if (DisplayIndex >= 0 && DisplayIndex < displays.Count)
        {
            var candidate = displays[DisplayIndex];
            if (DisplayName == null || candidate.Name == DisplayName)
                return candidate;
        }

        if (DisplayName != null)
        {
            foreach (var display in displays)
            {
                if (display.Name == DisplayName)
                    return display;
            }

            Log.Warning($"Display '{DisplayName}' not found, falling back to primary display.");
        }

        foreach (var display in displays)
        {
            if (display.IsPrimary)
                return display;
        }

        return displays[0];
    }

    public void SaveAsLastUsed(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonUtils.TrySaveJson(this, path);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to save player settings to {path}: {e.Message}");
        }
    }

    public override string ToString() => $"display {DisplayIndex} ({DisplayName}), {Width}x{Height}, fullscreen: {Fullscreen}, logs: {ShowLogs}, loop: {Loop}, vsync: {VSync}";

    private static void TryDeleteLastUsed(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to reset player settings at {path}: {e.Message}");
        }
    }

    /// <summary>
    /// Command line switches. Nullable / flag members are only applied when explicitly given, so the
    /// saved and project defaults stay in effect otherwise.
    /// </summary>
    internal sealed class CommandLineArgs
    {
        [Option("display", HelpText = "Index of the display to use (1-based, as listed in the startup dialog).")]
        public int? DisplayOneBased { get; set; }

        public int? Display => DisplayOneBased.HasValue ? DisplayOneBased.Value - 1 : null;

        [Option("width", HelpText = "Render width in pixels.")]
        public int? Width { get; set; }

        [Option("height", HelpText = "Render height in pixels.")]
        public int? Height { get; set; }

        [Option("windowed", HelpText = "Run in a window.")]
        public bool Windowed { get; set; }

        [Option("fullscreen", HelpText = "Run borderless fullscreen on the selected display.")]
        public bool Fullscreen { get; set; }

        [Option("show-logs", HelpText = "Open a console window with log messages.")]
        public bool ShowLogs { get; set; }

        [Option("loop", HelpText = "Restart playback at the end of the timeline.")]
        public bool Loop { get; set; }

        [Option("novsync", HelpText = "Disable vsync.")]
        public bool NoVsync { get; set; }

        [Option("no-dialog", HelpText = "Skip the startup dialog and start with the resolved settings.")]
        public bool NoDialog { get; set; }

        [Option("dialog", HelpText = "Show the startup dialog even if the project disables it.")]
        public bool ForceDialog { get; set; }

        [Option("reset", HelpText = "Forget the previously used startup settings.")]
        public bool Reset { get; set; }
    }
}
