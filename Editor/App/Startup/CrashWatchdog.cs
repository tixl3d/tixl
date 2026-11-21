#nullable enable
using System.Diagnostics;
using System.IO;
using System.Threading;
using T3.Core.Settings;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.App.Startup;

/// <summary>
/// Keeps a show machine running: when enabled in Settings, the editor launches as a child of a
/// small supervisor process (this same executable started with <c>--watchdog</c>). The supervisor
/// waits for the editor to exit and relaunches it when it died abnormally; a normal quit ends the
/// supervisor too.
/// </summary>
/// <remarks>
/// A managed <c>AppDomain.UnhandledException</c> hook cannot survive the native crashes that
/// matter here (Vulkan driver faults, teardown segfaults), so restart decisions are made from a
/// separate process via the child's exit code:
///   0                        normal quit
///   1 / -1                   taskkill /f, Task Manager / Stop-Process -Force on Windows
///   130 / 137 / 143          SIGINT / SIGKILL / SIGTERM on Linux — an explicit "stop" must not respawn
///   anything else            crash → restart
/// Startup-crash floods (5 consecutive crashes within seconds of launch) give up instead of
/// looping forever.
/// </remarks>
internal static class CrashWatchdog
{
    private const string WatchdogArg = "--watchdog";
    private const string ChildEnvVar = "TIXL_WATCHDOG_CHILD";
    private const string LogFileName = "watchdog.log";
    private const int MaxFastCrashes = 5;
    private const long FastCrashWindowMs = 10_000;
    private const int RestartDelayMs = 2000;

    private static readonly HashSet<int> _suppressRestartExitCodes = [1, -1, 130, 137, 143];

    /// <summary>True in editor processes spawned by the watchdog (i.e. this session is supervised).</summary>
    internal static bool IsChildProcess => Environment.GetEnvironmentVariable(ChildEnvVar) == "1";

    /// <summary>
    /// Marks or unmarks this process (and every process it spawns) as supervised. An in-app restart
    /// unmarks it first, so the new instance starts its own supervisor instead of running unwatched
    /// after the old supervisor exits with the old editor.
    /// </summary>
    internal static void SetSupervised(bool supervised)
        => Environment.SetEnvironmentVariable(ChildEnvVar, supervised ? "1" : null);

    /// <summary>
    /// Handles a <c>--watchdog</c> launch: runs the supervision loop (which blocks until the
    /// editor stays down) and never starts the editor in this process. Returns true when handled.
    /// </summary>
    internal static bool TryRunWatchdogMode(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], WatchdogArg, StringComparison.OrdinalIgnoreCase))
            return false;

        RunSupervisionLoop(args.Skip(1).ToArray());
        return true;
    }

    /// <summary>
    /// Called very early in startup, before any window appears: when crash restart is enabled and
    /// this instance is not already supervised, spawns the supervisor and returns true — the caller
    /// should exit immediately, and the supervised editor takes over. The hand-off happens before
    /// SDL init, so nothing visible flickers.
    /// </summary>
    internal static bool TryHandOffToWatchdog(string[] args)
    {
        if (IsChildProcess
            || Debugger.IsAttached
            || !UserSettings.Config.StartupEnabled
            || !UserSettings.Config.CrashAutoRestart
            || string.IsNullOrEmpty(LaunchCommands.HostPath))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo { FileName = LaunchCommands.HostPath };
            if (OperatingSystem.IsWindows())
            {
                // Launch via the shell so the supervisor isn't part of this process's job
                // object. Under a debugger (Rider) that job is killed when we exit, which
                // would take the supervisor down with it (same rationale as EditorRestart).
                startInfo.UseShellExecute = true;
            }
            else
            {
                // On Unix, UseShellExecute builds a shell command line and mangles
                // ArgumentList entries into a single token; exec directly instead,
                // which passes the argv through byte-exact.
                startInfo.UseShellExecute = false;
            }

            startInfo.ArgumentList.Add(WatchdogArg);
            foreach (var arg in SanitizePayload(args))
                startInfo.ArgumentList.Add(arg);

            // The supervised child must not read the settings files while this instance's
            // save-on-quit handlers are still writing them.
            startInfo.ArgumentList.Add($"--wait-for-exit={Environment.ProcessId}");

            using var supervisor = Process.Start(startInfo);
            if (supervisor == null)
                return false;
        }
        catch (Exception e)
        {
            Log.Warning($"Could not start the crash watchdog: {e.Message}");
            return false;
        }

        Console.WriteLine("Handing over to the crash watchdog...");
        return true;
    }

    /// <summary>Original args without flags that must not survive into a supervised relaunch.</summary>
    private static IEnumerable<string> SanitizePayload(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals(WatchdogArg, StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("--wait-for-exit=", StringComparison.Ordinal))
            {
                continue;
            }

            yield return arg;
        }
    }

    private static void RunSupervisionLoop(string[] payload)
    {
        LogEvent($"supervisor started (pid {Environment.ProcessId}), payload: {string.Join(' ', payload)}");

        var fastCrashes = 0;
        while (true)
        {
            Process child;
            try
            {
                child = StartChild(payload);

                // The wait only applies to the first launch; the pid may belong to another process by the time of a restart.
                payload = SanitizePayload(payload).ToArray();
            }
            catch (Exception e)
            {
                LogEvent($"could not start the editor: {e.Message} — supervisor exits");
                Environment.Exit(1);
                return;
            }

            var startedAtMs = Environment.TickCount64;
            child.WaitForExit();
            var exitCode = child.ExitCode;

            if (exitCode == 0 || _suppressRestartExitCodes.Contains(exitCode))
            {
                LogEvent($"editor exited (code {exitCode}) — not a crash, supervisor exits");
                Environment.Exit(0);
                return;
            }

            var uptimeMs = Environment.TickCount64 - startedAtMs;
            if (uptimeMs < FastCrashWindowMs && ++fastCrashes >= MaxFastCrashes)
            {
                LogEvent($"editor crashed {MaxFastCrashes} times within seconds of starting "
                         + $"(last code {exitCode}) — giving up to avoid a restart loop");
                Environment.Exit(exitCode);
                return;
            }

            if (uptimeMs >= FastCrashWindowMs)
                fastCrashes = 0;

            LogEvent($"editor crashed after {uptimeMs / 1000f:0.0}s (code {exitCode}) — restarting in {RestartDelayMs / 1000f:0.0}s");
            Thread.Sleep(RestartDelayMs);
        }
    }

    private static Process StartChild(string[] payload)
    {
        var startInfo = new ProcessStartInfo
                            {
                                FileName = LaunchCommands.HostPath ?? "TiXL",
                                // Direct exec: the supervisor needs the child's exit code and sets its env.
                                UseShellExecute = false,
                            };

        foreach (var arg in LaunchCommands.BuildArgumentList(payload))
            startInfo.ArgumentList.Add(arg);

        startInfo.EnvironmentVariables[ChildEnvVar] = "1";
        return Process.Start(startInfo)!;
    }

    /// <summary>Appends one line to the settings folder's watchdog.log; never throws.</summary>
    private static void LogEvent(string message)
    {
        Console.WriteLine($"TiXL watchdog: {message}");
        try
        {
            var path = Path.Combine(FileLocations.SettingsDirectory, LogFileName);
            if (File.Exists(path) && new FileInfo(path).Length > 100_000)
                File.Delete(path);

            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch
        {
            // The supervisor must keep running even when its log is unwritable.
        }
    }
}
