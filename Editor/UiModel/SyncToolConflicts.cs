#nullable enable
using System.IO;

namespace T3.Editor.UiModel;

/// <summary>
/// Identifies project-folder access failures that are most likely caused by a file-syncing tool
/// (OneDrive, Dropbox, ...) interfering with TiXL. We deliberately do not probe for sync tools
/// themselves (placeholder attributes, reparse points, env vars) — that list is never complete.
/// Instead we use one narrow indicator: an <see cref="UnauthorizedAccessException"/> raised inside
/// a folder that Windows manages (Documents, Desktop, ...), because those are exactly the folders
/// sync tools silently take over.
/// </summary>
internal static class SyncToolConflicts
{
    internal static bool IsLikelySyncConflict(Exception? exception, string? folderPath)
    {
        return ContainsUnauthorizedAccess(exception) && IsInsideWindowsManagedFolder(folderPath);
    }

    /// <summary>
    /// The access-denied exception usually arrives wrapped (e.g. MSBuild's InvalidProjectFileException),
    /// so the inner-exception chain is searched too.
    /// </summary>
    private static bool ContainsUnauthorizedAccess(Exception? exception)
    {
        for (var depth = 0; exception != null && depth < 10; depth++)
        {
            if (exception is UnauthorizedAccessException)
                return true;

            if (exception is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (ContainsUnauthorizedAccess(inner))
                        return true;
                }
            }

            exception = exception.InnerException;
        }

        return false;
    }

    internal static bool IsInsideWindowsManagedFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(folderPath);
        }
        catch (Exception)
        {
            return false;
        }

        foreach (var specialFolder in _managedSpecialFolders)
        {
            // Returns the redirected location (e.g. .../OneDrive/Documents) when a sync tool has
            // taken the folder over — the OS reporting its own paths, not sync-tool sniffing.
            var managedPath = Environment.GetFolderPath(specialFolder);
            if (string.IsNullOrEmpty(managedPath))
                continue;

            if (fullPath.StartsWith(managedPath, StringComparison.OrdinalIgnoreCase)
                && (fullPath.Length == managedPath.Length || fullPath[managedPath.Length] is '\\' or '/'))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly Environment.SpecialFolder[] _managedSpecialFolders =
        [
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos,
        ];
}
