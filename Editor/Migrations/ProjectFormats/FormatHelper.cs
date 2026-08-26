#nullable enable
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace T3.Editor.Migrations.ProjectFormats;

/// <summary>
/// The on-disk format of a TiXL project, identified by the csproj's <c>ProjectFormatVersion</c>
/// property and counted up by one per <see cref="ProjectMigrationStep"/>. Each version's specific
/// knowledge (folder layout, sweep rules) nests in its own namespace (<c>V1</c>, <c>V2</c>, ...).
/// </summary>
internal enum ProjectFormat
{
    Unknown = 0,

    /// <summary>Operator files at the project root and in root-level namespace folders (before TiXL 4.3).</summary>
    V1 = 1,

    /// <summary>Operator files live in the Symbols/ folder; discovery is limited to it.</summary>
    V2 = 2,
}

/// <summary>
/// Identifies the <see cref="ProjectFormat"/> of projects and archives. The csproj marker is
/// authoritative; content sniffing is the fallback for artifacts written before the marker existed.
/// This is what lets a backup or share package from years ago be recognized and routed through the
/// migration chain instead of being misread with current-format assumptions.
/// </summary>
internal static partial class FormatHelper
{
    public const ProjectFormat Current = ProjectFormat.V2;

    public static ProjectFormat GuessFormatForDirectory(string projectFolder)
    {
        if (!Directory.Exists(projectFolder))
            return ProjectFormat.Unknown;

        foreach (var csprojPath in Directory.EnumerateFiles(projectFolder, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            if (TryReadFormatMarker(ReadTextSafely(csprojPath), out var format))
                return format;
        }

        // No marker - sniff the layout by where the symbol files live
        var symbolsFolder = Path.Combine(projectFolder, Core.Settings.FileLocations.SymbolsSubfolder);
        if (Directory.Exists(symbolsFolder)
            && Directory.EnumerateFiles(symbolsFolder, "*" + T3.Core.Model.SymbolPackage.SymbolExtension, SearchOption.AllDirectories).Any())
        {
            return ProjectFormat.V2;
        }

        foreach (var path in V1.Layout.EnumerateFilesOutsideContentFolders(projectFolder))
        {
            if (path.EndsWith(T3.Core.Model.SymbolPackage.SymbolExtension, StringComparison.OrdinalIgnoreCase))
                return ProjectFormat.V1;
        }

        return ProjectFormat.Unknown;
    }

    public static ProjectFormat GuessFormatForBackupArchive(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            // The csproj travels in every backup and share package - its marker is authoritative
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains('/'))
                    continue;

                using var reader = new StreamReader(entry.Open());
                if (TryReadFormatMarker(reader.ReadToEnd(), out var format))
                    return format;
            }

            // No marker - sniff by entry paths
            var sawV1SymbolFile = false;
            var symbolsPrefix = Core.Settings.FileLocations.SymbolsSubfolder + '/';
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(T3.Core.Model.SymbolPackage.SymbolExtension, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entry.FullName.StartsWith(symbolsPrefix, StringComparison.OrdinalIgnoreCase))
                    return ProjectFormat.V2;

                var firstSegment = entry.FullName.Split('/')[0];
                if (!V1.Layout.IsContentOrGeneratedDirectory(firstSegment))
                    sawV1SymbolFile = true;
            }

            return sawV1SymbolFile ? ProjectFormat.V1 : ProjectFormat.Unknown;
        }
        catch (Exception e)
        {
            Log.Warning($"Can't inspect archive {zipPath}: {e.Message}");
            return ProjectFormat.Unknown;
        }
    }

    /// <summary>
    /// Reads the format marker from csproj text without an MSBuild evaluation - detection must also
    /// work on files inside archives and on projects too broken to load. Accepts the pre-release
    /// property name <c>ProjectStructureVersion</c>, which briefly stamped "2" during 4.3 development.
    /// </summary>
    private static bool TryReadFormatMarker(string? csprojText, out ProjectFormat format)
    {
        format = ProjectFormat.Unknown;
        if (csprojText == null)
            return false;

        var match = FormatMarkerRegex().Match(csprojText);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out var version))
            return false;

        format = (ProjectFormat)version;
        return true;
    }

    [GeneratedRegex(@"<(ProjectFormatVersion|ProjectStructureVersion)>\s*(\d+)\s*</\1>")]
    private static partial Regex FormatMarkerRegex();

    private static string? ReadTextSafely(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e)
        {
            Log.Warning($"Can't read {path}: {e.Message}");
            return null;
        }
    }
}
