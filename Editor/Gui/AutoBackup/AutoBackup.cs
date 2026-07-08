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
    /// <summary>One backup archive of a project, parsed from its file name.</summary>
    public readonly record struct BackupEntry(
        int Index, DateTime Timestamp, string FilePath, long SizeBytes, bool IsPinned, bool IsMinimal, string? KeepTag);

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

    /// <summary>
    /// Writes a backup that the pruner will never delete, tagged with a reason (e.g. "preFormat3").
    /// Used to snapshot a project before a one-way format upgrade so the user can revert. Minimal
    /// (sources and symbol files only) — assets aren't touched by format migrations, so zipping them
    /// would only bloat a snapshot that is kept forever. No-op (returns false) if a pinned backup
    /// with the same tag already exists for the project.
    /// </summary>
    public static bool CreatePinnedBackup(string projectFolder, string tag, out string? createdZipPath)
    {
        createdZipPath = null;

        if (!IsValidKeepTag(tag))
        {
            Log.Warning($"Invalid backup keep-tag '{tag}'; must be letters, digits, or dots.");
            return false;
        }

        var backupDir = Path.Combine(projectFolder, BackupSubFolder);
        Directory.CreateDirectory(backupDir);

        if (PinnedBackupExists(backupDir, tag))
            return false;

        var pendingZipPath = Path.Join(backupDir, PendingZipName);
        if (File.Exists(pendingZipPath))
            DeleteFile(pendingZipPath);

        // Hold the lock across the zip so a recompile rewriting project files mid-archive
        // can't produce an inconsistent snapshot (same reason as CreateBackupCallback).
        lock (ProjectSetup.SymbolDataLock)
        {
            if (!TryWriteBackupZip(projectFolder, pendingZipPath, minimal: true))
            {
                DeleteFile(pendingZipPath);
                return false;
            }

            var index = GetIndexOfLastBackup(backupDir) + 1;
            var finalZipPath = Path.Join(backupDir, $"#{index:D5}-{DateTime.Now:yyyy_MM_dd-HH_mm_ss_fff}-minimal-keep-{tag}.zip");
            try
            {
                File.Move(pendingZipPath, finalZipPath);
                createdZipPath = finalZipPath;
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Failed to finalize pinned backup {0}: {1}", finalZipPath, e.Message);
                DeleteFile(pendingZipPath);
                return false;
            }
        }
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
                var relativePath = filepath[projectFolder.Length..].TrimStart(Path.DirectorySeparatorChar);

                // Full backups are complete, shareable containers (assets, thumbnails, everything).
                // Minimal backups keep only source, symbols, and .meta data (variations) — no bulky
                // assets and no regenerable thumbnails.
                if (minimal && !IsMinimalBackupFile(fileInfo.Extension, relativePath))
                    continue;

                // Minimal backups skip oversized files (and say so, rather than dropping them
                // silently). Full backups are complete containers and keep everything, large or not.
                if (minimal && fileInfo.Length > MinimalMaxFileSizeBytes)
                {
                    Log.Warning($"Backup: skipping oversized file in minimal backup: {relativePath} "
                                + $"({fileInfo.Length / (1024 * 1024)} MB)");
                    continue;
                }

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

    /// <summary>
    /// Whether a file belongs in a minimal backup: source/symbol files, plus .meta data (variations),
    /// but not the regenerable thumbnails. Full backups keep everything and don't use this.
    /// </summary>
    private static bool IsMinimalBackupFile(string extension, string relativePath)
    {
        if (_minimalBackupExtensions.Contains(extension))
            return true;

        return IsUnderMeta(relativePath) && !IsThumbnailPath(relativePath);
    }

    private static bool IsThumbnailPath(string relativePath)
    {
        return NormalizeSeparators(relativePath).StartsWith(_thumbnailsPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderMeta(string relativePath)
    {
        return NormalizeSeparators(relativePath).StartsWith(_metaPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSeparators(string path)
    {
        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static void TouchLatestBackupTimestamp(string latestArchive)
    {
        try
        {
            var fileName = Path.GetFileName(latestArchive);
            var match = _backupNameRegex.Match(fileName);
            if (!match.Success)
                return;

            var index = int.Parse(match.Groups[IndexGroup].Value);
            var minimalSuffix = match.Groups[MinimalMarkerGroup].Success ? "-minimal" : "";
            // Preserve the "-keep-<tag>" pin marker, otherwise the dedup rename would strip
            // it and the snapshot would become prunable.
            var keepSuffix = match.Groups[KeepMarkerGroup].Success ? match.Groups[KeepMarkerGroup].Value : "";
            var dir = Path.GetDirectoryName(latestArchive);
            if (dir == null)
                return;

            var renamed = Path.Combine(dir, $"#{index:D5}-{DateTime.Now:yyyy_MM_dd-HH_mm_ss_fff}{minimalSuffix}{keepSuffix}.zip");
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

        return RestoreFromArchive(projectFolder, latestArchive);
    }

    /// <summary>
    /// Replaces a project's source/symbol/.meta files with the contents of a backup zip. Build
    /// artifacts (bin/, obj/) and the existing graph files are deleted first — otherwise an operator
    /// renamed or removed after the backup would survive the restore and collide by Guid (same id,
    /// two files) with the version being restored. Assets and thumbnails are left in place: they
    /// never collide, and a minimal backup wouldn't carry them to put back. Entries that would escape
    /// the project folder are skipped. The caller is responsible for reloading — a restart is the safe
    /// way to pick up the restored files.
    /// </summary>
    public static bool RestoreFromArchive(string projectFolder, string zipFilePath)
    {
        try
        {
            // Open first so a corrupt or unreadable archive is caught before anything is deleted.
            using var archive = ZipFile.OpenRead(zipFilePath);

            // bin/obj are regenerable and can be large — free them before the space check.
            DeleteBuildArtifacts(projectFolder);

            if (!HasEnoughFreeSpaceToExtract(projectFolder, archive, out var neededMb, out var freeMb))
            {
                Log.Error($"Restore aborted: needs ~{neededMb} MB but only {freeMb} MB is free on the "
                          + "project's drive. No project files were removed.");
                return false;
            }

            // Abort before extracting if a stale file can't be cleared — extracting over it would
            // leave the colliding operator in place.
            if (!ClearGraphFilesBeforeRestore(projectFolder))
                return false;

            // Compare against the folder *with* a trailing separator so a sibling like "Proj_evil"
            // can't slip past the guard by prefix-matching "Proj".
            var projectRootWithSeparator = WithTrailingSeparator(Path.GetFullPath(projectFolder));

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var targetPath = Path.GetFullPath(Path.Combine(projectFolder, entry.FullName));

                // Defensive: refuse entries that would write outside the project folder.
                if (!targetPath.StartsWith(projectRootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning($"Skipped backup entry that would extract outside the project: {entry.FullName}");
                    continue;
                }

                var directory = Path.GetDirectoryName(targetPath);
                if (directory != null && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                entry.ExtractToFile(targetPath, true);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Restoring archive {zipFilePath} failed. Is the zip corrupted? " + e.Message);
            return false;
        }
        return true;
    }

    /// <summary>Lists a project's backups newest first (pinned snapshots sort by their original index).</summary>
    public static IReadOnlyList<BackupEntry> EnumerateBackupsFor(string projectFolder)
    {
        var backupDir = Path.Combine(projectFolder, BackupSubFolder);
        if (!Directory.Exists(backupDir))
            return [];

        var entries = new List<BackupEntry>();
        foreach (var path in Directory.EnumerateFiles(backupDir, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var match = _backupNameRegex.Match(Path.GetFileName(path));
            if (!match.Success)
                continue;

            var index = int.Parse(match.Groups[IndexGroup].Value);
            var timestamp = ParseTimestampFromName(path) ?? DateTime.MinValue;

            long size = 0;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                // Size is display-only; a stat failure shouldn't drop the entry.
            }

            var keepMarker = match.Groups[KeepMarkerGroup];
            var keepTag = keepMarker.Success ? keepMarker.Value["-keep-".Length..] : null;
            entries.Add(new BackupEntry(index, timestamp, path, size,
                                        keepMarker.Success, match.Groups[MinimalMarkerGroup].Success, keepTag));
        }

        entries.Sort((a, b) => b.Index.CompareTo(a.Index));
        return entries;
    }

    private static void DeleteBuildArtifacts(string projectFolder)
    {
        TryDeleteRecursively(Path.Combine(projectFolder, "bin"));
        TryDeleteRecursively(Path.Combine(projectFolder, "obj"));
    }

    /// <summary>
    /// Deletes the project's existing source/symbol/.meta files (the set a minimal backup manages)
    /// before a restore, so a stale operator left over from a post-backup rename or deletion can't
    /// survive and cause a Guid collision. Assets and thumbnails are deliberately left untouched.
    /// Returns false if any file could not be removed (e.g. locked) — the caller must then abort
    /// rather than extract over the leftover.
    /// </summary>
    private static bool ClearGraphFilesBeforeRestore(string projectFolder)
    {
        // Collect first — deleting while enumerating the directory is unsafe.
        var toDelete = new List<string>();
        foreach (var filepath in EnumerateProjectFiles(projectFolder))
        {
            var relativePath = filepath[projectFolder.Length..].TrimStart(Path.DirectorySeparatorChar);
            if (IsMinimalBackupFile(Path.GetExtension(filepath), relativePath))
                toDelete.Add(filepath);
        }

        var failed = 0;
        foreach (var filepath in toDelete)
        {
            if (!DeleteFile(filepath))
                failed++;
        }

        if (failed > 0)
            Log.Warning($"Restore aborted: {failed} existing project file(s) could not be removed (locked?). "
                        + "Extracting over them could leave a duplicate operator (Guid collision).");

        return failed == 0;
    }

    /// <summary>
    /// True if the project's drive has room for the archive's uncompressed contents (plus a margin).
    /// Cheap — reads entry sizes from the zip index, no decompression. Fails open (returns true) if the
    /// free space can't be determined, so an inability to check never blocks a restore.
    /// </summary>
    private static bool HasEnoughFreeSpaceToExtract(string projectFolder, ZipArchive archive, out long neededMb, out long freeMb)
    {
        long uncompressed = 0;
        foreach (var entry in archive.Entries)
            uncompressed += entry.Length;

        var needed = uncompressed + FreeSpaceMarginBytes;
        neededMb = needed / (1024 * 1024);
        freeMb = 0;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(projectFolder));
            if (string.IsNullOrEmpty(root))
                return true;

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return true;

            freeMb = drive.AvailableFreeSpace / (1024 * 1024);
            return drive.AvailableFreeSpace >= needed;
        }
        catch (Exception e)
        {
            Log.Debug($"Could not check free space before restore: {e.Message}");
            return true;
        }
    }

    private static string WithTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
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
            var index = int.Parse(match.Groups[IndexGroup].Value);
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
            var index = int.Parse(match.Groups[IndexGroup].Value);
            if (index > latestIndex)
            {
                latestIndex = index;
                latestPath = path;
            }
        }
        return latestPath;
    }

    private static bool PinnedBackupExists(string backupDir, string tag)
    {
        var marker = $"-keep-{tag}";
        foreach (var path in Directory.EnumerateFiles(backupDir, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var match = _backupNameRegex.Match(Path.GetFileName(path));
            if (match.Success
                && match.Groups[KeepMarkerGroup].Success
                && string.Equals(match.Groups[KeepMarkerGroup].Value, marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsValidKeepTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return false;

        foreach (var c in tag)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.')
                return false;
        }
        return true;
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
        var pinnedIndices = new HashSet<int>();
        var highestIndex = int.MinValue;

        foreach (var filename in Directory.EnumerateFiles(backupDir, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var match = _backupNameRegex.Match(Path.GetFileName(filename));
            if (!match.Success)
                continue;

            var index = int.Parse(match.Groups[IndexGroup].Value);
            if (index > highestIndex)
                highestIndex = index;
            backupFilePathsByIndex[index] = filename;
            if (match.Groups[KeepMarkerGroup].Success)
                pinnedIndices.Add(index);
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
                if (pinnedIndices.Contains(i))
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

    private static bool DeleteFile(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception e)
        {
            Log.Info("Failed to delete file: " + e.Message);
            return false;
        }
    }

    private const string PendingZipName = ".pending.zip";

    // Named capture-group indices into _backupNameRegex. Timestamp components (2..8) are
    // consumed positionally in ParseTimestampFromName and don't need names.
    private const int IndexGroup = 1;
    private const int MinimalMarkerGroup = 9;   // optional "-minimal" suffix that marks reduced backups
    private const int KeepMarkerGroup = 10;     // optional "-keep-<tag>" pin marker; pinned backups are never pruned

    private static readonly Regex _backupNameRegex = new(
        @"^#(\d{5})-(\d{4})_(\d{2})_(\d{2})-(\d{2})_(\d{2})_(\d{2})_(\d{3})(-minimal)?(-keep-[A-Za-z0-9.]+)?\.zip$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly char[] _pathSeparators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    private static readonly string[] _excludedDirs =
        { "bin", "obj", ".git", ".temp", "Render", "Export", "ImageSequence", "Screenshots" };

    // Relative-path prefixes (OS separator). ".meta/" data (variations) is kept even in minimal
    // backups; ".meta/Thumbnails/" is regenerable and kept only in full backups.
    private static readonly string _metaPrefix = FileLocations.MetaSubFolder + Path.DirectorySeparatorChar;
    private static readonly string _thumbnailsPrefix =
        FileLocations.MetaSubFolder + Path.DirectorySeparatorChar + "Thumbnails" + Path.DirectorySeparatorChar;

    // Minimal backups skip files larger than this (a guard against a stray huge source/json file);
    // full backups have no cap so they stay complete. Restore requires this much headroom on top of
    // the archive's uncompressed size before it will touch anything.
    private const long MinimalMaxFileSizeBytes = 100L * 1024 * 1024;
    private const long FreeSpaceMarginBytes = 50L * 1024 * 1024;

    private static readonly HashSet<string> _minimalBackupExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".cs", ".t3", ".t3ui", ".hlsl", ".json", ".txt"
    };

    private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private static bool _isSaving;
}
