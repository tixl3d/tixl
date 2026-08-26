#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using T3.Core.Settings;
using T3.Editor.Compilation;
using T3.Editor.Migrations.ProjectFormats;

namespace T3.Editor.Migrations.Steps;

/// <summary>
/// Format V1 -> V2: all operator files (.t3/.t3ui and their C# sources) move from their root-level
/// V1 namespace directories into <c>Symbols/</c>, which is the only place symbol discovery looks,
/// and the csproj's release content includes are rooted there.
/// </summary>
internal sealed partial class To2_SymbolsFolder : ProjectMigrationStep
{
    public override ProjectFormat TargetFormat => ProjectFormat.V2;
    public override Version ShipsWithEditorVersion => new(4, 3, 0);
    public override string Description => "Move operator files into the Symbols folder";

    public override void Apply(CsProjectFile csProjectFile)
    {
        var projectFolder = Path.GetFullPath(csProjectFile.Directory);
        var movedCount = MoveOperatorFilesIntoSymbolsFolder(projectFolder);
        RemoveEmptiedDirectories(projectFolder);
        csProjectFile.MigrateContentIncludesToSymbolsFolder();

        Log.Info($"Moved {movedCount} operator files of \"{csProjectFile.Name}\" into the Symbols folder.");
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

    private static bool IsInSkippedSubdirectory(string relativePath)
    {
        var firstSeparator = relativePath.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (firstSeparator < 0)
            return false; // root-level files (V1 kept the home symbol there) are migration candidates

        return ProjectFormats.V1.Layout.IsContentOrGeneratedDirectory(relativePath[..firstSeparator]);
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
            Log.Warning($"Can't inspect {path} during project format migration: {e.Message}");
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
