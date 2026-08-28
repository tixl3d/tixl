#nullable enable
using System.IO;
using T3.Core.Settings;
using T3.Editor.UiModel;

namespace T3.Editor.Migrations.ProjectFormats.V1;

/// <summary>
/// Knowledge about project structure version 1 (projects saved before TiXL 4.3, no
/// <c>ProjectStructureVersion</c> in the csproj), where operator files lived at the project root
/// and in root-level namespace folders, next to generated state. Only code that handles V1 trees
/// may use this - the structure migration, pinned recovery snapshots, and previous-version import;
/// everything else works purely from <see cref="ProjectLayout"/>'s allowlist. Delete this class
/// once V1 projects no longer need supporting.
/// </summary>
internal static class Layout
{
    /// <summary>
    /// Generated or derived state that is never project content: build output, version control,
    /// backups/temp, and player exports. Walks over V1 trees must skip these because they contain
    /// *copies* of operator files (Release output, exports). Media output folders (Render,
    /// Screenshots) are deliberately not listed - a V1 project may keep a namespace of the same
    /// name at its root.
    /// </summary>
    public static readonly string[] GeneratedStateDirectories =
        [
            "bin", "obj", ".git", ".temp", FileLocations.ExportSubFolder,
        ];

    /// <summary>
    /// The files of a V1 project that live outside the <see cref="ProjectLayout.ContentSubdirectories"/>
    /// and outside generated state, including root-level files (V1 kept the home symbol at the
    /// project root; the csproj is excluded here since the content layout already covers it).
    /// </summary>
    public static IEnumerable<string> EnumerateFilesOutsideContentFolders(string projectFolder)
    {
        if (!Directory.Exists(projectFolder))
            yield break;

        foreach (var filepath in Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(projectFolder, filepath);
            var firstSeparator = relativePath.IndexOfAny(_pathSeparators);
            if (firstSeparator < 0)
            {
                if (!relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    yield return filepath;

                continue;
            }

            if (IsContentOrGeneratedDirectory(relativePath[..firstSeparator]))
                continue;

            yield return filepath;
        }
    }

    public static bool IsContentOrGeneratedDirectory(string directoryName)
    {
        foreach (var subdirectory in ProjectLayout.ContentSubdirectories)
        {
            if (string.Equals(directoryName, subdirectory, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var generated in GeneratedStateDirectories)
        {
            if (string.Equals(directoryName, generated, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static readonly char[] _pathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
}
