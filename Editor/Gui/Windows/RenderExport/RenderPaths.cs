#nullable enable
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using T3.Core.Settings;
using T3.Core.Video;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static partial class RenderPaths
{
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
            // The container must match the codec, whatever extension the stored path carries.
            var targetPath = WithCodecExtension(ResolveProjectRelativePath(RenderSettings.Current.VideoFilePath ?? string.Empty),
                                                settings.VideoCodec);
            if (settings.AutoIncrementVersionNumber)
            {
                if (!IsFilenameIncrementable(targetPath))
                {
                    targetPath = GetNextIncrementedPath(targetPath);
                }

                // Keep numbering consistent across container formats: jump past the highest existing version among
                // files sharing this name stem regardless of extension, so e.g. render-v08.mov isn't picked while
                // render-v08.mp4 already exists.
                var highestVersion = GetHighestExistingVersion(Path.GetDirectoryName(targetPath), Path.GetFileName(targetPath));
                if (highestVersion >= 0)
                {
                    targetPath = WithVersionNumber(targetPath, highestVersion + 1);
                }

                while (File.Exists(targetPath))
                {
                    targetPath = GetNextIncrementedPath(targetPath);
                }
            }
            return targetPath;
        }

        var folder = ResolveProjectRelativePath(RenderSettings.Current.SequenceFilePath ?? string.Empty);
        var subFolder = RenderSettings.Current.SequenceFileName ?? "v01";
        var prefix = RenderSettings.Current.SequencePrefix ?? "render";
        
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

    /// <summary>Replaces (or appends) the path's video extension with the one the codec's container requires,
    /// e.g. render-v01.mp4 → render-v01.mov for HAP. Leaves non-video extensions alone.</summary>
    public static string WithCodecExtension(string? path, VideoExportCodec codec)
    {
        path ??= string.Empty;
        var extension = codec.GetFileExtension();
        if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            return path;

        foreach (var known in _videoExtensions)
        {
            if (path.EndsWith(known, StringComparison.OrdinalIgnoreCase))
                return path[..^known.Length] + extension;
        }

        return path + extension;
    }

    public static bool IsFilenameIncrementable(string? path = null)
    {
        var filename = Path.GetFileName(path ?? RenderSettings.Current.VideoFilePath);
        return !string.IsNullOrEmpty(filename) && _matchFileVersionPattern.Match(filename).Success;
    }

    public static void TryIncrementVideoFileName()
    {
        var path = RenderSettings.Current.VideoFilePath;
        if (string.IsNullOrEmpty(path) || !IsFilenameIncrementable(path))
            return;

        RenderSettings.Current.VideoFilePath = GetNextIncrementedPath(path);
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

    // Highest version number among sibling files that share this name's stem (the parts before and after the
    // version), ignoring extension. Returns -1 when the folder holds no such file.
    private static int GetHighestExistingVersion(string? directory, string fileNameWithVersion)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return -1;

        var nameNoExt = Path.GetFileNameWithoutExtension(fileNameWithVersion);
        var match = _matchFileVersionPattern.Match(nameNoExt);
        if (!match.Success)
            return -1;

        var digits = match.Groups[1];
        var prefix = nameNoExt[..digits.Index];
        var suffix = nameNoExt[(digits.Index + digits.Length)..];

        var highest = -1;
        foreach (var file in Directory.EnumerateFiles(directory, prefix + "*")) // OS-side prefix filter keeps it cheap
        {
            var candidate = Path.GetFileNameWithoutExtension(file);
            if (candidate.Length <= prefix.Length + suffix.Length
                || !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var middle = candidate[prefix.Length..(candidate.Length - suffix.Length)];
            if (middle.Length is >= 2 and <= 4 && int.TryParse(middle, out var version) && version > highest)
                highest = version;
        }

        return highest;
    }

    // Replaces the version digits in path's filename with versionNumber, preserving the existing digit width (2-4).
    private static string WithVersionNumber(string path, int versionNumber)
    {
        var filename = Path.GetFileName(path);
        var match = _matchFileVersionPattern.Match(filename);
        if (!match.Success)
            return path;

        var versionGroup = match.Groups[1];
        var width = Math.Clamp(versionGroup.Value.Length, 2, 4);
        var newFilename = filename.Remove(versionGroup.Index, versionGroup.Length)
                                  .Insert(versionGroup.Index, versionNumber.ToString("D" + width));

        var directory = Path.GetDirectoryName(path);
        return directory == null ? newFilename : Path.Combine(directory, newFilename);
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

    private static readonly Regex _matchFileVersionPattern = FileVersionPatternRegex();

    // The containers the export can write — derived from the codec list, so GetFileExtension() stays the one
    // place that defines a codec's container.
    private static readonly string[] _videoExtensions = Enum.GetValues<VideoExportCodec>()
                                                            .Select(c => c.GetFileExtension())
                                                            .Distinct()
                                                            .ToArray();

    [GeneratedRegex(@"(?:^|[\s_\-.])v(\d{2,4})(?=$|[\s_\-.])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileVersionPatternRegex();
}