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
        var settings = RenderSettings.Current;
        if (mode == RenderSettings.RenderModes.Video)
        {
            var targetPath = ResolveProjectRelativePath(UserSettings.Config.RenderVideoFilePath ?? string.Empty);
            if (settings.AutoIncrementVersionNumber)
            {
                if (!IsFilenameIncrementable(targetPath))
                {
                    targetPath = GetNextIncrementedPath(targetPath);
                }

                while (File.Exists(targetPath))
                {
                    targetPath = GetNextIncrementedPath(targetPath);
                }
            }
            return targetPath;
        }

        var folder = ResolveProjectRelativePath(UserSettings.Config.RenderSequenceFilePath ?? string.Empty);
        var subFolder = UserSettings.Config.RenderSequenceFileName ?? "v01";
        var prefix = UserSettings.Config.RenderSequencePrefix ?? "render";
        
        if (settings.AutoIncrementSubFolder)
        {
            var targetToIncrement = settings.CreateSubFolder ? subFolder : prefix;
            if (!IsFilenameIncrementable(targetToIncrement))
            {
                targetToIncrement = GetNextIncrementedPath(targetToIncrement);
            }

            while (true)
            {
                var checkPath = settings.CreateSubFolder 
                                    ? Path.Combine(folder, targetToIncrement, prefix) 
                                    : Path.Combine(folder, targetToIncrement);
                
                if (FileExists(checkPath))
                {
                    targetToIncrement = GetNextIncrementedPath(targetToIncrement);
                }
                else
                {
                    break;
                }
            }

            if (settings.CreateSubFolder) subFolder = targetToIncrement;
            else prefix = targetToIncrement;
        }

        if (settings.CreateSubFolder)
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
            return targetPath;
        }

        // Image sequence path
        var frameCount = RenderTiming.ComputeFrameCount(settings);
        var frameRange = frameCount <= 1 ? "0000" : $"0000..{(frameCount - 1):D4}";
        return $"{targetPath}_{frameRange}.{settings.FileFormat.ToString().ToLower()}";
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
        var path = UserSettings.Config.RenderVideoFilePath;
        if (string.IsNullOrEmpty(path) || !IsFilenameIncrementable(path))
            return;

        UserSettings.Config.RenderVideoFilePath = GetNextIncrementedPath(path);
        UserSettings.Save();
    }

    public static string GetVersionString(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) 
            return string.Empty;
        
        var match = FileVersionPatternRegex().Match(name);
        if (match.Success)
        {
            return "v" + match.Groups[1].Value;
        }

        return string.Empty;
    }

    public static string GetNextVersionString(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "v01";

        var filename = Path.GetFileName(path);
        var match = FileVersionPatternRegex().Match(filename);
        if (match.Success)
        {
            var versionGroup = match.Groups[1];
            if (int.TryParse(versionGroup.Value, out var versionNumber))
            {
                var digits = Math.Clamp(versionGroup.Value.Length, 2, 4);
                return "v" + (versionNumber + 1).ToString("D" + digits);
            }
        }

        return "v01";
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
            var extension = Path.GetExtension(filename);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            newFilename = nameWithoutExtension + "_v01" + extension;
        }
        else
        {
            var versionGroup = match.Groups[1];
            var versionString = versionGroup.Value;
            
            if (!int.TryParse(versionString, out var versionNumber))
            {
                var extension = Path.GetExtension(filename);
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
                newFilename = nameWithoutExtension + "_v01" + extension;
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

    [GeneratedRegex(@"(?:^|[\s_\-.])v(\d{2,4})(?=$|[\s_\-.])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileVersionPatternRegex();
}