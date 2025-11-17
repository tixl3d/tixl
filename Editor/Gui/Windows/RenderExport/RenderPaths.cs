#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using T3.Core.SystemUi;
using T3.Core.UserData;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static class RenderPaths
{
    private static readonly Regex _matchFileVersionPattern = new(@"\bv(\d{2,4})\b");

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

    public static bool ValidateOrCreateTargetFolder(string targetFile)
    {
        var directory = Path.GetDirectoryName(targetFile);
        if (targetFile != directory && File.Exists(targetFile))
        {
            var info = new FileInfo(targetFile);
            
            if (info.Length == 0 || !info.IsReadOnly) // It's fine to override 0 size files
            {
                
                var result = BlockingWindow.Instance.ShowMessageBox($"File {targetFile} exists. Overwrite?", "Render Video", "Yes", "No");
                return result == "Yes";
            }
        }

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
}