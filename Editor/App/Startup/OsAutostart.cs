#nullable enable
using System.Diagnostics;
using System.IO;
using T3.Core.Settings;

namespace T3.Editor.App.Startup;

/// <summary>
/// Installs and removes the per-user "start at login" entry for the current TiXL build. The
/// mechanism is the conventional one per platform, so it also shows up in the OS's own autostart
/// managers where users expect to find it:
///   Windows:  HKCU\Software\Microsoft\Windows\CurrentVersion\Run registry value "TiXL"
///   Linux:    ~/.config/autostart/app.tixl.TiXL.desktop (XDG autostart)
///   macOS:    ~/Library/LaunchAgents/app.tixl.TiXL.autostart.plist (RunAtLoad)
/// </summary>
internal static class OsAutostart
{
    /// <summary>Passed by the OS entries so the editor can tell an autostarted launch from a manual one.</summary>
    internal const string ArgMarker = "--autostart";

    private const string ValueName = "TiXL";
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DesktopAppId = "app.tixl.TiXL";
    private const string PlistLabel = DesktopAppId + ".autostart";

    /// <summary>
    /// Installs (enable) or removes (disable) the login entry for this build. Returns false with an
    /// error message when the OS rejected the change; never throws.
    /// </summary>
    internal static bool TrySetEnabled(bool enable, out string error)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return SetWindows(enable, out error);

            if (OperatingSystem.IsLinux())
                return SetLinux(enable, out error);

            if (OperatingSystem.IsMacOS())
                return SetMacOs(enable, out error);

            error = "autostart is not supported on this platform";
            return false;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>Human-readable description of where the entry lives, for the settings panel.</summary>
    internal static string DescribeEntry()
    {
        if (OperatingSystem.IsWindows())
            return $@"{RunKey}\{ValueName}";

        if (OperatingSystem.IsLinux())
            return DesktopFilePath;

        if (OperatingSystem.IsMacOS())
            return PlistFilePath;

        return "unsupported platform";
    }

    /// <summary>The extra arguments embedded in the OS entry: the marker, plus the version-id
    /// override when this instance runs in a separate settings folder.</summary>
    private static string[] EntryArguments()
    {
        var versionId = FileLocations.VersionIdOverride;
        return versionId == null
                   ? [ArgMarker]
                   : [ArgMarker, "--override-version-id=" + versionId];
    }

    #region Windows ----------------------------------------------------------

    private static bool SetWindows(bool enable, out string error)
    {
        if (!enable)
        {
            // A missing value is the target state, so a "not found" delete still succeeds.
            RunReg(["delete", RunKey, "/v", ValueName, "/f"], out _);
            error = string.Empty;
            return true;
        }

        if (!RunReg(["add", RunKey, "/v", ValueName, "/t", "REG_SZ", "/d", LaunchCommands.ToCommandLine(EntryArguments()), "/f"], out error))
        {
            error = "reg.exe: " + error;
            return false;
        }

        return true;
    }

    /// <summary>Runs reg.exe (always present in System32) instead of linking a registry package.</summary>
    private static bool RunReg(string[] arguments, out string error)
    {
        var startInfo = new ProcessStartInfo
                            {
                                FileName = "reg.exe",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardError = true,
                                RedirectStandardOutput = true,
                            };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                error = "could not start reg.exe";
                return false;
            }

            // Drain both pipes; a full undrained pipe would block reg.exe.
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            error = (stderr.Trim().Length > 0 ? stderr : stdout).Trim();
            return process.ExitCode == 0;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    #endregion

    #region Linux ------------------------------------------------------------

    private static string DesktopFilePath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart",
                        DesktopAppId + ".desktop");

    private static bool SetLinux(bool enable, out string error)
    {
        var path = DesktopFilePath;
        if (!enable)
        {
            File.Delete(path);
            error = string.Empty;
            return true;
        }

        var exec = string.Join(" ",
                               new[] { LaunchCommands.HostPath ?? "TiXL" }
                                  .Concat(LaunchCommands.BuildArgumentList(EntryArguments()))
                                  .Select(LaunchCommands.EscapeDesktopArg));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
                          $"""
                           [Desktop Entry]
                           Type=Application
                           Name=TiXL
                           Comment=TiXL editor — started automatically at login
                           Exec={exec}
                           Terminal=false
                           X-GNOME-Autostart-enabled=true
                           """);
        error = string.Empty;
        return true;
    }

    #endregion

    #region macOS ------------------------------------------------------------

    private static string PlistFilePath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library", "LaunchAgents", PlistLabel + ".plist");

    private static bool SetMacOs(bool enable, out string error)
    {
        var path = PlistFilePath;
        if (!enable)
        {
            File.Delete(path);
            error = string.Empty;
            return true;
        }

        var programArguments = new[] { LaunchCommands.HostPath ?? "TiXL" }
                              .Concat(LaunchCommands.BuildArgumentList(EntryArguments()))
                              .Select(a => $"                <string>{LaunchCommands.EscapeXml(a)}</string>");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
                          $"""
                           <?xml version="1.0" encoding="UTF-8"?>
                           <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                           <plist version="1.0">
                           <dict>
                               <key>Label</key>
                               <string>{PlistLabel}</string>
                               <key>ProgramArguments</key>
                               <array>
                           {string.Join(Environment.NewLine, programArguments)}
                               </array>
                               <key>RunAtLoad</key>
                               <true/>
                           </dict>
                           </plist>
                           """);
        error = string.Empty;
        return true;
    }

    #endregion
}
