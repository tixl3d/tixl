#nullable enable
using System.Diagnostics;
using System.IO;
using T3.Editor.App.Startup;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Restarts the editor by spawning a fresh instance and exiting this one.
/// </summary>
internal static class EditorRestart
{
    /// <summary>
    /// Spawns a fresh editor process and exits this one through the regular application exit path.
    /// <paramref name="projectFolder"/> opens that project on the new instance instead of the one this
    /// process was launched with. Returns false if the new instance could not be started; the current
    /// instance keeps running then.
    /// </summary>
    public static bool TryRestart(string? projectFolder = null)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Log.Warning("Could not determine the editor executable for a restart.");
            return false;
        }

        var wasSupervised = CrashWatchdog.IsChildProcess;
        try
        {
            // Collect the carried-over flags first (drop stale wait flags from earlier restarts,
            // then wait for this process so the new instance doesn't race our save-on-quit).
            // A --project flag is carried unless the caller names a replacement; otherwise a restart
            // of an instance started into a project would silently fall back to the hub.
            var replaceProject = !string.IsNullOrWhiteSpace(projectFolder);
            var carryArgs = new List<string>();
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("--wait-for-exit=", StringComparison.Ordinal))
                    continue;

                if (replaceProject && args[i].Equals("--project", StringComparison.OrdinalIgnoreCase))
                {
                    i++; // also skip its separate value
                    continue;
                }

                if (replaceProject && args[i].StartsWith("--project=", StringComparison.OrdinalIgnoreCase))
                    continue;

                carryArgs.Add(args[i]);
            }

            if (replaceProject)
            {
                carryArgs.Add("--project");
                carryArgs.Add(projectFolder!);
            }

            carryArgs.Add($"--wait-for-exit={Environment.ProcessId}");

            var startInfo = new ProcessStartInfo { FileName = exePath };

            // A `dotnet TiXL.dll ...` launch has the managed host as ProcessPath; the assembly path
            // must be the first argument or the new process cannot find the editor. The apphost
            // (TiXL.exe / TiXL) needs no such argument.
            var isDotnetHost = string.Equals(Path.GetFileNameWithoutExtension(exePath), "dotnet", StringComparison.OrdinalIgnoreCase);
            if (isDotnetHost)
            {
                var assemblyPath = typeof(EditorRestart).Assembly.Location;
                if (!string.IsNullOrEmpty(assemblyPath) && File.Exists(assemblyPath))
                    startInfo.ArgumentList.Add(assemblyPath);
                else if (Environment.GetCommandLineArgs()[0] is { } entryArg
                         && entryArg.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    startInfo.ArgumentList.Add(entryArg);
            }

            if (OperatingSystem.IsWindows())
            {
                // Launch via the shell so the child isn't part of this process's job
                // object. Under a debugger (Rider) that job is killed when we exit,
                // which would take the freshly spawned editor down with it.
                startInfo.UseShellExecute = true;
                foreach (var arg in carryArgs)
                    startInfo.ArgumentList.Add(arg);
            }
            else
            {
                // On Unix, UseShellExecute builds a shell command line and mangles
                // ArgumentList entries into a single token (observed: 'TiXL.dll---debug-server');
                // exec directly instead, which passes the argv through byte-exact.
                startInfo.UseShellExecute = false;
                foreach (var arg in carryArgs)
                    startInfo.ArgumentList.Add(arg);
            }

            CrashWatchdog.SetSupervised(false);
            Process.Start(startInfo);
        }
        catch (Exception e)
        {
            CrashWatchdog.SetSupervised(wasSupervised);
            Log.Warning($"Failed to start a new editor instance: {e.Message}");
            return false;
        }

        Log.Info("Restarting the editor...");
        EditorUi.Instance.ExitApplication();
        return true;
    }
}
