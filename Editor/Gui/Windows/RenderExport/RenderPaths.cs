#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using T3.Core.UserData;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static partial class RenderPaths
{
    private static readonly Regex _matchFileVersionPattern = FileVersionPatternRegex();

    public static string ResolveProjectRelativePath(string path)
    {
        var project = ProjectView.Focused?.OpenedProject;
        if (project != null && path.StartsWith('.'))
        {
            return Path.Combine(project.Package.Folder, path);
        }

        // TODO: Make project directory selection smarter
        return path.StartsWith('.')
                   ? Path.Combine(UserSettings.Config.ProjectDirectories[0], FileLocations.RenderSubFolder, path)
                   : path;
    }

    public static string GetTargetFilePath(RenderSettings.RenderModes mode)
    {
        if (mode == RenderSettings.RenderModes.Video)
        {
            return ResolveProjectRelativePath(UserSettings.Config.RenderVideoFilePath ?? string.Empty);
        }

        var folder = ResolveProjectRelativePath(UserSettings.Config.RenderSequenceFilePath ?? string.Empty);
        var subFolder = UserSettings.Config.RenderSequenceFileName ?? "v01";
        var prefix = UserSettings.Config.RenderSequencePrefix ?? "render";

        if (RenderSettings.Current.CreateSubFolder)
        {
            return Path.Combine(folder, subFolder, prefix);
        }

        return Path.Combine(folder, prefix);
    }

    public static string GetExpectedTargetDisplayPath(RenderSettings.RenderModes mode)
    {
        var targetPath = GetTargetFilePath(mode);
        var settings = RenderSettings.Current;

        if (mode == RenderSettings.RenderModes.Video)
        {
            if (settings.AutoIncrementVersionNumber && !IsFilenameIncrementable(targetPath))
            {
                targetPath = GetNextIncrementedPath(targetPath);
            }
            return targetPath;
        }

        // Image sequence
        var subFolder = UserSettings.Config.RenderSequenceFileName ?? "v01";
        var prefix = UserSettings.Config.RenderSequencePrefix ?? "render";
        
        if (settings.AutoIncrementSubFolder)
        {
            var targetToIncrement = settings.CreateSubFolder ? subFolder : prefix;
            if (!IsFilenameIncrementable(targetToIncrement))
            {
                var incremented = GetNextIncrementedPath(targetToIncrement);
                if (settings.CreateSubFolder) subFolder = incremented;
                else prefix = incremented;
            }
        }

        var folder = ResolveProjectRelativePath(UserSettings.Config.RenderSequenceFilePath ?? string.Empty);
        var finalBase = settings.CreateSubFolder ? Path.Combine(folder, subFolder, prefix) : Path.Combine(folder, prefix);
        
        return $"{finalBase}_####.{settings.FileFormat.ToString().ToLower()}";
    }

    public static bool FileExists(string targetPath)
    {
        if (RenderSettings.Current.RenderMode == RenderSettings.RenderModes.Video)
        {
            return File.Exists(targetPath);
        }

        // For image sequences, check if the first frame or the folder exists
        if (RenderSettings.Current.CreateSubFolder)
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (directory != null && Directory.Exists(directory))
            {
                // If the directory exists, check if it contains any files or subdirectories
                try
                {
                    return Directory.EnumerateFileSystemEntries(directory).Any();
                }
                catch
                {
                    return true; // Assume exists if we can't access
                }
            }
        }

        var firstFrame = $"{targetPath}_0000.{RenderSettings.Current.FileFormat.ToString().ToLower()}";
        return File.Exists(firstFrame);
    }

    public static bool ValidateOrCreateTargetFolder(string targetFile)
    {
        var directory = Path.GetDirectoryName(targetFile);
        if (directory == null || Directory.Exists(directory))
            return true;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to create target folder '{directory}': {e.Message}");
            return false;
        }

        return true;
    }

    public static string SanitizeFilename(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return "output";

        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
            filename = filename.Replace(c.ToString(), "_");

        return filename.Trim();
    }

    public static bool IsFilenameIncrementable(string? path = null)
    {
        var filename = Path.GetFileName(path ?? UserSettings.Config.RenderVideoFilePath);
        return !string.IsNullOrEmpty(filename) && _matchFileVersionPattern.Match(filename).Success;
    }

    public static void TryIncrementVideoFileNameInUserSettings()
    {
        var filename = Path.GetFileName(UserSettings.Config.RenderVideoFilePath);
        if (string.IsNullOrEmpty(filename))
            return;

        var result = _matchFileVersionPattern.Match(filename);
        if (!result.Success)
            return;

        var versionString = result.Groups[1].Value;
        if (!int.TryParse(versionString, out var versionNumber))
            return;

        var digits = Math.Clamp(versionString.Length, 2, 4);
        var newVersionString = "v" + (versionNumber + 1).ToString("D" + digits);
        var newFilename = filename.Replace("v" + versionString, newVersionString);

        var directoryName = Path.GetDirectoryName(UserSettings.Config.RenderVideoFilePath);
        UserSettings.Config.RenderVideoFilePath = directoryName == null
                                                      ? newFilename
                                                      : Path.Combine(directoryName, newFilename);
        UserSettings.Save();
    }

    public static string GetNextIncrementedPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "output";

        var filename = Path.GetFileName(path);
        var directory = Path.GetDirectoryName(path);
        string newFilename;

        var match = _matchFileVersionPattern.Match(filename);
        if (!match.Success)
        {
            newFilename = filename + "_v01";
        }
        else
        {
            var versionGroup = match.Groups[1];
            var versionString = versionGroup.Value;
            
            if (!int.TryParse(versionString, out var versionNumber))
            {
                newFilename = filename + "_v01";
            }
            else
            {
                var digits = Math.Clamp(versionString.Length, 2, 4);
                var newVersionNumberString = (versionNumber + 1).ToString("D" + digits);
                
                // Replace only the version number part within the matched group
                newFilename = filename.Remove(versionGroup.Index, versionGroup.Length)
                                      .Insert(versionGroup.Index, newVersionNumberString);
            }
        }

        return directory == null ? newFilename : Path.Combine(directory, newFilename);
    }

    [GeneratedRegex(@"(?:^|[\s_\-.])v(\d{2,4})(?:\b|$)")]
    private static partial Regex FileVersionPatternRegex();
}