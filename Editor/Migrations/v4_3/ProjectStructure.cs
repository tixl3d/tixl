#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using T3.Core.Settings;
using T3.Editor.Compilation;

namespace T3.Editor.Migrations.v4_3;

/// <summary>
/// One-way migration of a project directory to the Symbols-folder structure: all operator files
/// (.t3/.t3ui and their C# sources) move from their legacy root-level namespace directories into
/// <c>Symbols/</c>, which is the only place symbol discovery looks. Runs silently on load, after a
/// pinned backup. Built-in packages are skipped - their layout is maintained in the repository.
/// </summary>
internal static partial class ProjectStructure
{
    public static void MigrateIfNeeded(CsProjectFile csProjectFile)
    {
        if (csProjectFile.HasCurrentProjectStructure)
            return;

        var projectFolder = Path.GetFullPath(csProjectFile.Directory);
        if (projectFolder.StartsWith(ProjectSetup.BuiltInOperatorDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (Gui.AutoBackup.AutoBackup.CreatePinnedBackup(projectFolder, "preSymbolsFolder", out var backupPath))
            {
                Log.Info($"Created backup before project structure migration: {backupPath}");
            }

            var movedCount = MoveOperatorFilesIntoSymbolsFolder(projectFolder);
            RemoveEmptiedDirectories(projectFolder);
            csProjectFile.MarkStructureMigratedToSymbolsFolder();

            Log.Info($"Migrated project \"{csProjectFile.Name}\" to the Symbols folder structure ({movedCount} files moved).");
        }
        catch (Exception e)
        {
            // Leave the project un-stamped so the next start retries; partially moved files are
            // picked up again (moves are idempotent) and the backup preserves the original state.
            Log.Error($"Failed to migrate project \"{csProjectFile.Name}\" to the Symbols folder structure: {e}");
        }
    }

    private static int MoveOperatorFilesIntoSymbolsFolder(string projectFolder)
    {
        var symbolsFolder = Path.Combine(projectFolder, FileLocations.SymbolsSubfolder);
        var movedCount = 0;

        // Collect first, then move - moving while enumerating is undefined
        var filesToMove = new List<(string SourcePath, string TargetPath)>();

        foreach (var path in Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(projectFolder, path);
            if (IsInSkippedSubdirectory(relativePath))
                continue;

            if (!IsOperatorFile(path))
                continue;

            filesToMove.Add((path, Path.Combine(symbolsFolder, relativePath)));
        }

        foreach (var (sourcePath, targetPath) in filesToMove)
        {
            if (File.Exists(targetPath))
            {
                Log.Warning($"Skipping migration of {sourcePath}: {targetPath} already exists.");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Move(sourcePath, targetPath);
            movedCount++;
        }

        return movedCount;
    }

    /// <summary>
    /// Directories that are not symbol locations: the target folder itself, assets and other
    /// non-operator content, and generated state. Deliberately NOT skipping media folders like
    /// Render/ or Screenshots/ - a namespace may use those names (Lib's "render" does), and they
    /// contain no operator files anyway.
    /// </summary>
    private static readonly string[] _skippedSubdirectories =
        [
            FileLocations.SymbolsSubfolder,
            FileLocations.AssetsSubfolder,
            FileLocations.DependenciesFolder,
            FileLocations.ExportSubFolder,
            FileLocations.MetaSubFolder,
            ".temp", ".git", "bin", "obj",
        ];

    private static bool IsInSkippedSubdirectory(string relativePath)
    {
        foreach (var skipped in _skippedSubdirectories)
        {
            if (relativePath.StartsWith(skipped + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith(skipped + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Symbol definitions always move; a C# file moves when it is an operator source - it carries a
    /// [Guid] attribute and derives from Instance (matching how symbol discovery identifies sources),
    /// or sits next to its symbol file. Helper code without either stays where the user put it.
    /// </summary>
    private static bool IsOperatorFile(string path)
    {
        if (path.EndsWith(T3.Core.Model.SymbolPackage.SymbolExtension, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(UiModel.EditorSymbolPackage.SymbolUiExtension, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!path.EndsWith(UiModel.EditorSymbolPackage.SourceCodeExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        var symbolTwinPath = Path.ChangeExtension(path, T3.Core.Model.SymbolPackage.SymbolExtension);
        if (File.Exists(symbolTwinPath))
            return true;

        try
        {
            var code = File.ReadAllText(path);
            return GuidAttributeRegex().IsMatch(code) && code.Contains("Instance<", StringComparison.Ordinal);
        }
        catch (Exception e)
        {
            Log.Warning($"Can't inspect {path} during project structure migration: {e.Message}");
            return false;
        }
    }

    [GeneratedRegex("\\[\\s*Guid\\s*\\(\\s*\"")]
    private static partial Regex GuidAttributeRegex();

    /// <summary>Removes directories the migration emptied out, deepest first. Non-empty ones stay.</summary>
    private static void RemoveEmptiedDirectories(string projectFolder)
    {
        var directories = Directory.GetDirectories(projectFolder, "*", SearchOption.AllDirectories);
        Array.Sort(directories, static (a, b) => b.Length.CompareTo(a.Length));

        foreach (var directory in directories)
        {
            var relativePath = Path.GetRelativePath(projectFolder, directory);
            if (IsInSkippedSubdirectory(relativePath + Path.DirectorySeparatorChar))
                continue;

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception e)
            {
                Log.Debug($"Couldn't remove emptied directory {directory}: {e.Message}");
            }
        }
    }
}
