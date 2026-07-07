using System.IO;
using T3.Core.SystemUi;
using T3.Core.Settings;
using T3.Core.Utils;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;

namespace T3.Editor.Gui.Interaction.StartupCheck;

/// <summary>
/// A helper that provides an option to restore the latest backups when the previous
/// startup failed.
/// </summary>
public static class StartUp
{
    public static void FlagBeginStartupSequence()
    {
        WarnIfLowDiskSpace();

        if (File.Exists(StartUpLockFilePath))
        {
            #if !DEBUG
            ShowLastStartupFailedMessageBox();
            #endif
        }

        File.WriteAllText(StartUpLockFilePath, "Startup " + DateTime.Now);
    }

    public static void FlagStartupSequenceComplete()
    {
        if (!File.Exists(StartUpLockFilePath))
        {
            Log.Debug("Strange. Startup file missing?");
        }
        else
        {
            File.Delete(StartUpLockFilePath);
        }
    }

    private static void ShowLastStartupFailedMessageBox()
    {
        var isThereABackup = AutoBackup.AutoBackup.HasAnyBackups();
        if (!isThereABackup)
        {
            var result2 = BlockingWindow.Instance.ShowMessageBox("It looks like the last startup failed.\nUnfortunately, there is no backup yet.", "Startup Failed", "Retry", "Cancel");
            if (result2 != "Retry")
            {
                Log.Info("User cancelled startup.");
                EditorUi.Instance.ExitApplication();
            }

            return;
        }

        Log.Debug("StartUpProgress lock file exists?");

        var timeOfLastBackup = AutoBackup.AutoBackup.GetTimeOfLastBackup();
        var timeSpan = StringUtils.GetReadableRelativeTime(timeOfLastBackup);

        const string caption = "Oh no! Start up problems...";
        string message = "It looks like the last startup was incomplete.\n\n" +
                         "You can choose:\n\n" +
                         $"  YES to restore the latest backup of every project ({timeSpan})\n" +
                         "  NO to open the documentation\n" +
                         "  CANCEL to attempt starting anyway.\n";

        const string restore = "Restore backups";
        const string openDoc = "Open documentation";
        const string startup = "I don't care do it anyway!!!!";
        var result = BlockingWindow.Instance.ShowMessageBox(message, caption, restore, openDoc, startup);
        switch (result)
        {
            case restore:
            {
                var restoredCount = AutoBackup.AutoBackup.RestoreLatestBackups();
                if (restoredCount > 0)
                {
                    FlagStartupSequenceComplete();
                    BlockingWindow.Instance.ShowMessageBox($"{restoredCount} project(s) restored. Click OK to restart.\nFingers crossed!", "Complete", "Ok");
                    Environment.Exit(0);
                }
                else
                {
                    BlockingWindow.Instance.ShowMessageBox("Restoring backups failed.\nYou might want to try an earlier archive in <project>/.temp/Backup/...", "Failed",
                                                           "Ok");
                    Environment.Exit(0);
                }

                break;
            }
            case openDoc:
                CoreUi.Instance.OpenWithDefaultApplication(HelpUrl);
                Environment.Exit(0);
                break;

            case startup:
                break;
        }
    }

    /// <summary>
    /// Warns when a drive holding the settings folder or a project directory is nearly full —
    /// saving, backups, logs and compilation all fail in hard-to-diagnose ways once the disk
    /// runs out, so surfacing it before any of that happens beats scattered write errors.
    /// </summary>
    private static void WarnIfLowDiskSpace()
    {
        var lowDrives = new List<string>();
        foreach (var driveRoot in EnumerateRelevantDriveRoots())
        {
            try
            {
                var info = new DriveInfo(driveRoot);
                if (!info.IsReady)
                    continue;

                var freeMb = info.AvailableFreeSpace / (1024 * 1024);
                if (freeMb < MinFreeDiskSpaceMb)
                    lowDrives.Add($"{info.Name}  ({freeMb} MB free)");
            }
            catch (Exception e)
            {
                Log.Debug($"Could not check disk space for {driveRoot}: {e.Message}");
            }
        }

        if (lowDrives.Count == 0)
            return;

        var driveList = string.Join("\n", lowDrives);
        Log.Warning($"Low disk space:\n{driveList}");

        var message = "These drives are running out of space:\n\n"
                      + driveList + "\n\n"
                      + "TiXL needs room for saving projects, backups and compiling. "
                      + $"With less than {MinFreeDiskSpaceMb} MB free, saving is likely to fail and data could be lost.\n\n"
                      + "Please free up some disk space.";

        var result = BlockingWindow.Instance.ShowMessageBox(message, "Low disk space", "Continue anyway", "Exit");
        if (result == "Exit")
        {
            Log.Info("User exited because of low disk space.");
            EditorUi.Instance.ExitApplication();
        }
    }

    /// <summary>Drive roots of the settings folder and every configured project directory, deduplicated.</summary>
    private static IEnumerable<string> EnumerateRelevantDriveRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TryAddDriveRoot(roots, FileLocations.SettingsDirectory);
        TryAddDriveRoot(roots, FileLocations.DefaultProjectFolder);

        var projectDirectories = UserSettings.Config?.ProjectDirectories;
        if (projectDirectories != null)
        {
            foreach (var dir in projectDirectories)
            {
                TryAddDriveRoot(roots, dir);
            }
        }

        return roots;
    }

    private static void TryAddDriveRoot(HashSet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(root))
                roots.Add(root);
        }
        catch (Exception e)
        {
            Log.Debug($"Could not resolve drive root for {path}: {e.Message}");
        }
    }

    private const long MinFreeDiskSpaceMb = 100;
    private const string HelpUrl = "https://github.com/tixl3d/tixl/wiki/help.Installation";
    private static string StartUpLockFilePath => Path.Combine(FileLocations.SettingsDirectory, "startingUp");

}
