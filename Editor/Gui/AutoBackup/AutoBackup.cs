#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.Settings;
using T3.Editor.Compilation;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.AutoBackup;

internal static class AutoBackup
{
    public static int SecondsBetweenSaves { get; set; } = 3 * 60;

    /// <summary>
    /// Per-project subfolder where backup zips are written, relative to a project's root folder.
    /// </summary>
    public const string BackupSubFolder = ".temp/Backup";

    /// <summary>
    /// Should be called after all frame operators are completed.
    /// </summary>
    public static void CheckForSave()
    {
        if (!UserSettings.Config.EnableAutoBackup
            || _isSaving
            || _stopwatch.ElapsedMilliseconds < SecondsBetweenSaves * 1000)
            return;

        _isSaving = true;

        // Refresh the layout snapshot here on the UI thread — the backup task
        // saves modified symbols and must not call into ImGui.
        ProjectView.SnapshotWindowLayoutForFocusedProject();
        Task.Run(CreateBackupCallback);
        _stopwatch.Restart();
    }

    private static void CreateBackupCallback()
    {
        try
        {
            if (T3Ui.IsCurrentlySaving)
            {
                Log.Debug("Skipped backup because saving is in progress.");
                return;
            }

            // Hold the lock across save AND zip - a recompile rewriting project files
            // mid-zip would produce an inconsistent archive.
            lock (ProjectSetup.SymbolDataLock)
            {
                T3Ui.Save(false);

                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

                foreach (var project in EditableSymbolProject.AllProjects)
                {
                    if (!project.HasHome)
                        continue;

                    BackupProject(project.Folder);
                }
            }
        }
        finally
        {
            _isSaving = false;
        }
    }

    private static void BackupProject(string projectFolder)
    {
        var backupDir = Path.Combine(projectFolder, BackupSubFolder);
        Directory.CreateDirectory(backupDir);

        // Clean up any leftover pending file from a crashed previous run.
        var pendingZipPath = Path.Join(backupDir, PendingZipName);
        if (File.Exists(pendingZipPath))
            DeleteFile(pendingZipPath);

        var latestArchive = GetLatestArchiveFilePath(backupDir);

        // The first backup for a project is always full, so the user has at least one
        // complete copy on disk regardless of the MinimalBackup toggle. Subsequent
        // backups follow the toggle.
        var isFirstBackup = latestArchive == null;
        var doMinimal = !isFirstBackup && UserSettings.Config.MinimalBackup;

        // Build the new zip to a temp path first, then byte-compare against the
        // previous backup. If identical, drop the new file and just refresh the
        // existing archive's timestamp. This is more reliable than comparing source
        // file mtimes — external tools (e.g. shader-tools) touch files without
        // changing their content.
        if (!TryWriteBackupZip(projectFolder, pendingZipPath, doMinimal))
        {
            DeleteFile(pendingZipPath);
            return;
        }

        if (latestArchive != null && FilesAreByteEqual(pendingZipPath, latestArchive))
        {
            DeleteFile(pendingZipPath);
            TouchLatestBackupTimestamp(latestArchive);
            return;
        }

        var index = GetIndexOfLastBackup(backupDir) + 1;
        ReduceNumberOfBackups(backupDir);

        var suffix = doMinimal ? "-minimal" : "";
        var finalZipPath = Path.Join(backupDir, $"#{index:D5}-{DateTime.Now:yyyy_MM_dd-HH_mm_ss_fff}{suffix}.zip");

        try
        {
            File.Move(pendingZipPath, finalZipPath);
        }
        catch (Exception e)
        {
            Log.Error("Failed to finalize backup {0}: {1}", finalZipPath, e.Message);
            DeleteFile(pendingZipPath);
        }
    }

    private static bool TryWriteBackupZip(string projectFolder, string zipFilePath, bool minimal)
    {
        try
        {
            using var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);
            foreach (var filepath in EnumerateProjectFiles(projectFolder))
            {
                var fileInfo = new FileInfo(filepath);

                if (minimal && !_minimalBackupExtensions.Contains(fileInfo.Extension))
                    continue;

                if (fileInfo.Length > MaxFileSizeBytes)
                    continue;

                var relativePath = filepath[projectFolder.Length..].TrimStart(Path.DirectorySeparatorChar);
                archive.CreateEntryFromFile(filepath, relativePath, CompressionLevel.Fastest);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Auto backup of {0} failed: {1}", projectFolder, ex.Message);
            return false;
        }
    }

    private static bool FilesAreByteEqual(string pathA, string pathB)
    {
        try
        {
            var infoA = new FileInfo(pathA);
            var infoB = new FileInfo(pathB);
            if (!infoA.Exists || !infoB.Exists)
                return false;
            if (infoA.Length != infoB.Length)
                return false;

            const int bufferSize = 64 * 1024;
            using var streamA = File.OpenRead(pathA);
            using var streamB = File.OpenRead(pathB);
            var bufferA = new byte[bufferSize];
            var bufferB = new byte[bufferSize];

            while (true)
            {
                var readA = streamA.Read(bufferA, 0, bufferSize);
                var readB = streamB.Read(bufferB, 0, bufferSize);
                if (readA != readB)
                    return false;
                if (readA == 0)
                    return true;
                if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                    return false;
            }
        }
        catch (Exception e)
        {
            Log.Debug("Failed to compare backup archives: " + e.Message);
            return false;
        }
    }

    private static IEnumerable<string> EnumerateProjectFiles(string projectFolder)
    {
        if (!Directory.Exists(projectFolder))
            yield break;

        foreach (var filepath in Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = filepath[projectFolder.Length..].TrimStart(Path.DirectorySeparatorChar);
            if (IsPathExcluded(relativePath))
                continue;
            // Skip files that external tooling rewrites independently of the user's edits
            // (e.g. Assets/shadertoolsconfig.json). Otherwise their mtime churn defeats
            // dedup and we accumulate identical zips.
            if (FileLocations.IgnoredFiles.Contains(Path.GetFileName(filepath)))
                continue;
            yield return filepath;
        }
    }

    private static bool IsPathExcluded(string relativePath)
    {
        var parts = relativePath.Split(_pathSeparators);
        for (var i = 0; i < parts.Length; i++)
        {
            for (var e = 0; e < _excludedDirs.Length; e++)
            {
                if (string.Equals(parts[i], _excludedDirs[e], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static void TouchLatestBackupTimestamp(string latestArchive)
    {
        try
        {
            var fileName = Path.GetFileName(latestArchive);
            var match = _backupNameRegex.Match(fileName);
            if (!match.Success)
                return;

            var index = int.Parse(match.Groups[1].Value);
            var minimalSuffix = match.Groups[9].Success ? "-minimal" : "";
            var dir = Path.GetDirectoryName(latestArchive);
            if (dir == null)
                return;

            var renamed = Path.Combine(dir, $"#{index:D5}-{DateTime.Now:yyyy_MM_dd-HH_mm_ss_fff}{minimalSuffix}.zip");
            if (string.Equals(renamed, latestArchive, StringComparison.OrdinalIgnoreCase))
                return;

            File.Move(latestArchive, renamed);
            File.SetLastWriteTime(renamed, DateTime.Now);
        }
        catch (Exception e)
        {
            Log.Debug("Failed to refresh backup timestamp: " + e.Message);
        }
    }

    /// <summary>
    /// Restore the latest backup of every project that has one. Build artifacts
    /// (bin/, obj/) under each restored project are deleted first so stale
    /// build output does not conflict with the restored sources.
    /// </summary>
    /// <returns>The number of projects that were successfully restored.</returns>
    public static int RestoreLatestBackups()
    {
        var restoredCount = 0;
        foreach (var projectFolder in EnumerateCandidateProjectFolders())
        {
            if (RestoreLatestForProject(projectFolder))
                restoredCount++;
        }
        return restoredCount;
    }

    private static bool RestoreLatestForProject(string projectFolder)
    {
        var backupDir = Path.Combine(projectFolder, BackupSubFolder);
        var latestArchive = GetLatestArchiveFilePath(backupDir);
        if (latestArchive == null)
            return false;

        DeleteBuildArtifacts(projectFolder);

        try
        {
            using var archive = ZipFile.Open(latestArchive, ZipArchiveMode.Read);
            var projectFolderFull = Path.GetFullPath(projectFolder);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var targetPath = Path.GetFullPath(Path.Combine(projectFolder, entry.FullName));

                // Defensive: refuse entries that would write outside the project folder.
                if (!targetPath.StartsWith(projectFolderFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                var directory = Path.GetDirectoryName(targetPath);
                if (directory != null && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                entry.ExtractToFile(targetPath, true);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Restoring archive {latestArchive} failed. Is the zip corrupted? " + e.Message);
            return false;
        }
        return true;
    }

    private static void DeleteBuildArtifacts(string projectFolder)
    {
        TryDeleteRecursively(Path.Combine(projectFolder, "bin"));
        TryDeleteRecursively(Path.Combine(projectFolder, "obj"));
    }

    private static void TryDeleteRecursively(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to delete {folder} before restore: {e.Message}");
        }
    }

    /// <summary>
    /// Returns the time of the most recent backup across all projects, or null if
    /// no backup exists. Safe to call before EditableSymbolProject.AllProjects has
    /// been populated.
    /// </summary>
    public static DateTime? GetTimeOfLastBackup()
    {
        DateTime? latest = null;
        foreach (var projectFolder in EnumerateCandidateProjectFolders())
        {
            var backupDir = Path.Combine(projectFolder, BackupSubFolder);
            var latestArchive = GetLatestArchiveFilePath(backupDir);
            if (latestArchive == null)
                continue;

            var time = ParseTimestampFromName(latestArchive);
            if (time.HasValue && (!latest.HasValue || time.Value > latest.Value))
                latest = time;
        }
        return latest;
    }

    /// <summary>
    /// True if any user project folder contains at least one backup zip. Safe to
    /// call before EditableSymbolProject.AllProjects has been populated.
    /// </summary>
    public static bool HasAnyBackups()
    {
        foreach (var projectFolder in EnumerateCandidateProjectFolders())
        {
            var backupDir = Path.Combine(projectFolder, BackupSubFolder);
            if (GetLatestArchiveFilePath(backupDir) != null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Yields every project root under any of the user's configured project
    /// directories. Used by the startup-time crash recovery flow when
    /// EditableSymbolProject.AllProjects is not yet populated.
    /// </summary>
    private static IEnumerable<string> EnumerateCandidateProjectFolders()
    {
        var projectDirectories = UserSettings.Config?.ProjectDirectories;
        if (projectDirectories == null)
            yield break;

        foreach (var topDir in projectDirectories)
        {
            if (string.IsNullOrWhiteSpace(topDir) || !Directory.Exists(topDir))
                continue;

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(topDir);
            }
            catch (Exception e)
            {
                Log.Debug($"Failed to enumerate {topDir}: {e.Message}");
                continue;
            }

            foreach (var subDir in subdirs)
                yield return subDir;
        }
    }

    private static DateTime? ParseTimestampFromName(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var match = _backupNameRegex.Match(fileName);
        if (!match.Success)
            return null;

        try
        {
            var year = int.Parse(match.Groups[2].Value);
            var month = int.Parse(match.Groups[3].Value);
            var day = int.Parse(match.Groups[4].Value);
            var hour = int.Parse(match.Groups[5].Value);
            var min = int.Parse(match.Groups[6].Value);
            var sec = int.Parse(match.Groups[7].Value);
            var ms = int.Parse(match.Groups[8].Value);
            return new DateTime(year, month, day, hour, min, sec, ms);
        }
        catch
        {
            return null;
        }
    }

    private static int GetIndexOfLastBackup(string backupDir)
    {
        if (!Directory.Exists(backupDir))
            return -1;

        var highestIndex = -1;
        foreach (var path in Directory.EnumerateFiles(backupDir, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var match = _backupNameRegex.Match(Path.GetFileName(path));
            if (!match.Success)
                continue;
            var index = int.Parse(match.Groups[1].Value);
            if (index > highestIndex)
                highestIndex = index;
        }
        return highestIndex;
    }

    private static string? GetLatestArchiveFilePath(string backupDir)
    {
        if (!Directory.Exists(backupDir))
            return null;

        string? latestPath = null;
        var latestIndex = -1;
        foreach (var path in Directory.EnumerateFiles(backupDir, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var match = _backupNameRegex.Match(Path.GetFileName(path));
            if (!match.Success)
                continue;
            var index = int.Parse(match.Groups[1].Value);
            if (index > latestIndex)
            {
                latestIndex = index;
                latestPath = path;
            }
        }
        return latestPath;
    }

    /*
     * Reduce the number of backups by removing some of the older backups. The older the backup the
     * less versions are kept. We're using the binary representation of the backup index to separate
     * the deleted versions from the ones we keep.
     *
     * This algorithm is hard to describe in words, but it basically thins out the backup-copies
     * according to their respective binary code:
     *
     *     bits    significant bit
     *     43210   bit         threshold for 2 saves per generation
     *     ------- ----------- ------------------------------------
     * 10. 01011 - 1           +0 keep(level0/1st)          <- example of 10 saved versions
     *  9. 01001 - 0           +0 keep(level0/2nd)
     *  8. 01000 - 4           +1 keep(level1/1st)
     *  7. 00111 - 0            1 remove
     *  6. 00110 - 1           +1 keep(level1/2nd)
     *  5. 00101 - 0            2 remove
     *  4. 00100 - 2           +2 Keep(level2/1st)
     *  3. 00011 - 0            2 remove
     *  2. 00010 - 1            2 remove
     *  1. 00001 - 0            2 remove
     *  0. 00000   inf         +2 keep(level2/2nd)
     *
     * This means that we're keeping N*log2 backups (e.g. 3*16 out of 65536 saved versions)
     * where N is the backup density.
     */
    private static void ReduceNumberOfBackups(string backupDir, int backupDensity = 3)
    {
        var backupFilePathsByIndex = new Dictionary<int, string>();
        var highestIndex = int.MinValue;

        foreach (var filename in Directory.EnumerateFiles(backupDir, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var match = _backupNameRegex.Match(Path.GetFileName(filename));
            if (!match.Success)
                continue;

            var index = int.Parse(match.Groups[1].Value);
            if (index > highestIndex)
                highestIndex = index;
            backupFilePathsByIndex[index] = filename;
        }

        var limit = 0;
        var limitCount = 0;
        for (var i = highestIndex - 1; i >= 0; i--)
        {
            var b = GetSignificantBit(0xffffff - i) + 1;
            if (b > limit)
            {
                limitCount++;
                if (limitCount >= backupDensity)
                {
                    limitCount = 0;
                    limit++;
                }
            }
            else
            {
                if (!backupFilePathsByIndex.TryGetValue(i, out var path))
                    continue;
                DeleteFile(path);
            }
        }
    }

    private static int GetSignificantBit(int n)
    {
        var a = new bool[32];
        var rest = n;
        while (rest > 0)
        {
            var h = (int)Math.Floor(Math.Log(rest, 2));
            rest -= (int)Math.Pow(2, h);
            a[h] = true;
        }

        rest = n + 1;
        while (rest > 0)
        {
            var h = (int)Math.Floor(Math.Log(rest, 2));
            rest -= (int)Math.Pow(2, h);
            if (!a[h])
                return h;
        }
        return 0;
    }

    private static void DeleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e)
        {
            Log.Info("Failed to delete file: " + e.Message);
        }
    }

    private const string PendingZipName = ".pending.zip";

    // Group 9 captures the optional "-minimal" suffix that marks reduced backups.
    private static readonly Regex _backupNameRegex = new(
        @"^#(\d{5})-(\d{4})_(\d{2})_(\d{2})-(\d{2})_(\d{2})_(\d{2})_(\d{3})(-minimal)?\.zip$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly char[] _pathSeparators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    private static readonly string[] _excludedDirs =
        { "bin", "obj", ".git", ".temp", "Render", "Export", "ImageSequence", "Screenshots" };

    private const long MaxFileSizeBytes = 100L * 1024 * 1024;

    private static readonly HashSet<string> _minimalBackupExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".cs", ".t3", ".t3ui", ".hlsl", ".json", ".txt"
    };

    private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private static bool _isSaving;
}
