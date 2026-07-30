#nullable enable
using System.Diagnostics;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Restarts the editor by spawning a fresh instance and exiting this one.
/// </summary>
internal static class EditorRestart
{
    /// <summary>
    /// Spawns a fresh editor process and exits this one through the regular application exit path.
    /// Returns false if the new instance could not be started; the current instance keeps running then.
    /// </summary>
    public static bool TryRestart()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Log.Warning("Could not determine the editor executable for a restart.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
                                {
                                    FileName = exePath,
                                    // Launch via the shell so the child isn't part of this process's job
                                    // object. Under a debugger (Rider) that job is killed when we exit,
                                    // which would take the freshly spawned editor down with it.
                                    UseShellExecute = true,
                                };

            // Keep flags like --override-version-id so the new instance uses the same folders,
            // but drop stale wait flags from earlier restarts.
            var args = Environment.GetCommandLineArgs();
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("--wait-for-exit=", StringComparison.Ordinal))
                    continue;

                startInfo.ArgumentList.Add(args[i]);
            }

            // The new instance waits for this process to fully exit — starting earlier races
            // our save-on-quit settings writes and kills the new instance during startup.
            startInfo.ArgumentList.Add($"--wait-for-exit={Environment.ProcessId}");

            Process.Start(startInfo);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to start a new editor instance: {e.Message}");
            return false;
        }

        Log.Info("Restarting the editor...");
        EditorUi.Instance.ExitApplication();
        return true;
    }
}
