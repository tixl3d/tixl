#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.UserData;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.AutoBackup;

internal static class AutoBackup
{
    public static int SecondsBetweenSaves { get; set; } = 3 * 60;
    
    /// <summary>
    /// Should be call after all frame operators are completed
    /// </summary>
    public static void CheckForSave()
    {
        if (!UserSettings.Config.EnableAutoBackup 
            || _isSaving 
            || _stopwatch.ElapsedMilliseconds < SecondsBetweenSaves * 1000)
            return;

        _isSaving = true;
        Task.Run(CreateBackupCallback);
        _stopwatch.Restart();
    }

    private static void CreateBackupCallback()
    {
        if (T3Ui.IsCurrentlySaving)
        {
            Log.Debug("Skipped backup because saving is in progress.");
            return;
        }

        T3Ui.Save(false);

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

        Directory.CreateDirectory(BackupDirectory);

        var index = GetIndexOfLastBackup();
        index++;
        ReduceNumberOfBackups();

        var zipFilePath = Path.Join(BackupDirectory, $"#{index:D5}-{DateTime.Now:yyyy_MM_dd-HH_mm_ss_fff}.zip");

        var excludedDirs = new[] { "bin", "obj", ".git", "Render", "ImageSequence", "Screenshots" };
        const long maxSizeBytes = 100 * 1024 * 1024; // 100 MB

        try
        {
            using var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);

            foreach (var sourcePath in SourcePaths)
            {
                if (!Directory.Exists(sourcePath))
                    continue;

                var projectRootName = Path.GetFileName(sourcePath);

                foreach (var filepath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    var relativeUnderProject = filepath[sourcePath.Length..].TrimStart(Path.DirectorySeparatorChar);
                    var relativePath = Path.Combine(projectRootName, relativeUnderProject);

                    var parts = relativePath.Split(Path.DirectorySeparatorChar);
                    if (parts.Any(part => excludedDirs.Contains(part, StringComparer.OrdinalIgnoreCase)))
                        continue;

                    var fileInfo = new FileInfo(filepath);
                    if (fileInfo.Length > maxSizeBytes)
                        continue;

                    archive.CreateEntryFromFile(filepath, relativePath, CompressionLevel.Fastest);
                }
            }
        }
        catch (Exception ex)
        {
            DeleteFile(zipFilePath);
            Log.Error("auto backup failed: {0}", ex.Message);
        }

        _isSaving = false;
    }

    private static void DeleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e)
        {
            Log.Info("Failed to delete file:" + e.Message);
        }
    }

    public static bool RestoreLast()
    {
        var latestArchivePath = GetLatestArchiveFilePath();
        if (latestArchivePath == null)
            return false;

        try
        {
            using var archive = ZipFile.Open(latestArchivePath, ZipArchiveMode.Read);

            foreach (var file in archive.Entries)
            {
                var targetFilePath = file.FullName;

                if (File.Exists(targetFilePath))
                {
                    File.Delete(targetFilePath);
                }

                var directory = Path.GetDirectoryName(targetFilePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Failed to create target directory for restoring{targetFilePath}:" + e.Message);
                        continue;
                    }
                }

                file.ExtractToFile(targetFilePath, true);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Restoring archive {latestArchivePath} failed. Is Zip archive corrupted?" + e.Message);
            return false;
        }

        return true;
    }

    public static DateTime? GetTimeOfLastBackup()
    {
        var lastFilePath = GetLatestArchiveFilePath();
        if (lastFilePath == null)
            return null;

        var result = Regex.Match(lastFilePath, @"(#\d\d\d\d\d)?-(\d\d\d\d)_(\d\d)_(\d\d)-(\d\d)_(\d\d)_(\d\d)_(\d\d\d)");

        if (!result.Success)
            return null;

        var year = result.Groups[2].Value;
        var month = result.Groups[3].Value;
        var day = result.Groups[4].Value;
        var hour = result.Groups[5].Value;
        var min = result.Groups[6].Value;
        var second = result.Groups[7].Value;

        var timeFromName = year + "-" + month + "-" + day + " " + hour + ":" + min + ":" + second;

        var date = DateTime.Parse(timeFromName);
        return date;
    }

    private static int GetIndexOfLastBackup()
    {
        var lastFilePath = GetLatestArchiveFilePath();
        if (lastFilePath == null)
            return -1;

        var result = Regex.Match(lastFilePath, @"#(\d\d\d\d\d)-(\d\d\d\d)_(\d\d)_(\d\d)-(\d\d)_(\d\d)_(\d\d)_(\d\d\d)");

        if (!result.Success)
            return -1;

        var index = int.Parse(result.Groups[1].Value);
        return index;
    }

    public static string? GetLatestArchiveFilePath()
    {
        Directory.CreateDirectory(BackupDirectory);
        return Directory.EnumerateFiles(BackupDirectory, "*.zip", SearchOption.TopDirectoryOnly)
                        .Reverse()
                        .FirstOrDefault();
    }

    /*
     * Reduce the number of backups by removing some of the older backups. The older the backup the
     * less versions are kept. We're using the binary representation of the backup index to separate
     * the deleted versions from the ones we keep.
     *
     * This algorithm is a hard to describe in words, but it basically thins out the backup-copies
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
     * This means that we're keeping N*log2 backups (e.g. 3*16 out of 65536 saved versions) where N is the backup density.
     */
    private static void ReduceNumberOfBackups(int backupDensity = 3)
    {
        // Gather list of backups with indexes and find latest index
        var regexMatchIndex = new Regex(@"#(\d\d\d\d\d)-(\d\d\d\d)_(\d\d)_(\d\d)-(\d\d)_(\d\d)_(\d\d)_(\d\d\d)");
        var backupFilePathsByIndex = new Dictionary<int, string>();
        var highestIndex = int.MinValue;

        foreach (var filename in Directory.GetFiles(BackupDirectory))
        {
            var result = regexMatchIndex.Match(filename);
            if (!result.Success)
                continue;

            var index = int.Parse(result.Groups[1].Value);
            if (index > highestIndex)
                highestIndex = index;

            backupFilePathsByIndex[index] = filename;
        }

        // Iterate over all files and thin out the backups
        var limit = 0;
        var limitCount = 0;
        for (var i = highestIndex - 1; i >= 0; i--)
        {
            var b = GetSignificantBit(0xffffff - i) + 1;

            // Keep
            if (b > limit)
            {
                limitCount++;
                if (limitCount >= backupDensity)
                {
                    limitCount = 0;
                    limit++;
                }
            }
            // Remove
            else
            {
                if (!backupFilePathsByIndex.ContainsKey(i))
                    continue;

                //Log.Debug($"removing... old backup {backupFilePathsByIndex[i]} (level 2^{b})...");
                File.Delete(backupFilePathsByIndex[i]);
            }
        }
    }

    /**
     * Get the significant bit in an integer
     */
    private static int GetSignificantBit(int n)
    {
        var a = new bool[32];
        var rest = n;

        // Break down integer into bits
        while (rest > 0)
        {
            var h = (int)Math.Floor(Math.Log(rest, 2));
            rest = rest - (int)Math.Pow(2, h);
            a[h] = true;
        }

        rest = n + 1;
        while (rest > 0)
        {
            var h = (int)Math.Floor(Math.Log(rest, 2));
            rest = rest - (int)Math.Pow(2, h);
            if (a[h] == false)
            {
                return h;
            }
        }

        return 0;
    }

    private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private static bool _isSaving;
    internal static string BackupDirectory => Path.Combine(FileLocations.SettingsDirectory, "Backup");

    private static string[] SourcePaths
    {
        get
        {
            return EditableSymbolProject.AllProjects
                                        .Where(x => x.HasHome)
                                        .Select(x => x.Folder)
                                        .Concat(_nonProjectSourcePaths)
                                        .ToArray();
        }
    }

    private static readonly string[] _nonProjectSourcePaths =
        {
            //ThemeHandling.ThemeFolder,
            //LayoutHandling.LayoutFolder
        };
}