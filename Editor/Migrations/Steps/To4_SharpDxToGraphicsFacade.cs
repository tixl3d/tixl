#nullable enable
using System.IO;
using T3.Core.Settings;
using T3.Editor.Compilation;
using T3.Editor.Migrations.ProjectFormats;

namespace T3.Editor.Migrations.Steps;

/// <summary>
/// Format V3 -> V4: the project compiles against the graphics facade instead of SharpDX. The csproj's
/// global using aliases and assembly references move to <c>T3.Graphics</c>/<c>T3.Graphics.Compat</c>,
/// and the operator sources get the same rewrite as the library packages did, so slot types resolve
/// to the registered facade types again. Build output is discarded because the previous assembly
/// was compiled against SharpDX.
/// </summary>
internal sealed partial class To4_SharpDxToGraphicsFacade : ProjectMigrationStep
{
    public override ProjectFormat TargetFormat => ProjectFormat.V4;
    public override Version ShipsWithEditorVersion => new(4, 4, 0);
    public override string Description => "Move operator code from SharpDX onto the graphics facade";

    public override void Apply(CsProjectFile csProjectFile)
    {
        var projectFolder = Path.GetFullPath(csProjectFile.Directory);

        var filesStillUsingSharpDx = new List<string>();
        var rewrittenCount = RewriteSources(projectFolder, filesStillUsingSharpDx);

        var csprojChanged = csProjectFile.MigrateUsingsToGraphicsFacade(SourceRewriter.MapQualifiedName);

        // A source that still needs SharpDX (image loading, shader compilation) keeps the references,
        // otherwise the project would stop compiling at a place the rewrite can't fix.
        var keepSharpDxReferences = filesStillUsingSharpDx.Count > 0;
        csprojChanged |= csProjectFile.MigrateReferencesToGraphicsFacade(keepSharpDxReferences);

        if (rewrittenCount > 0 || csprojChanged)
        {
            TryDeleteDirectory(Path.Combine(projectFolder, FileLocations.TempSubfolder, "bin"));
        }

        Log.Info($"Rewrote {rewrittenCount} source files of \"{csProjectFile.Name}\" onto the graphics facade.");

        if (keepSharpDxReferences)
        {
            Log.Warning($"\"{csProjectFile.Name}\" still references SharpDX in {filesStillUsingSharpDx.Count} files, "
                        + "which need a look by hand:\n  " + string.Join("\n  ", filesStillUsingSharpDx));
        }
    }

    private static int RewriteSources(string projectFolder, List<string> filesStillUsingSharpDx)
    {
        var rewrittenCount = 0;
        var notes = new List<string>();

        foreach (var path in Directory.EnumerateFiles(projectFolder, "*" + UiModel.EditorSymbolPackage.SourceCodeExtension, SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(projectFolder, path);
            if (IsGeneratedOrForeign(relativePath))
                continue;

            string original;
            try
            {
                original = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Log.Warning($"Can't read {path} during project format migration: {e.Message}");
                continue;
            }

            notes.Clear();
            var rewritten = SourceRewriter.Rewrite(original, notes);

            if (rewritten != original)
            {
                try
                {
                    File.WriteAllText(path, rewritten);
                    rewrittenCount++;
                }
                catch (Exception e)
                {
                    Log.Warning($"Can't write {path} during project format migration: {e.Message}");
                    continue;
                }
            }

            if (notes.Count > 0)
            {
                filesStillUsingSharpDx.Add($"{relativePath}: {string.Join(", ", notes)}");
            }
        }

        return rewrittenCount;
    }

    /// <summary>
    /// Build output and exports hold copies of operator sources, backups hold the pre-migration state;
    /// none of them is project code. Matches the compile glob's excludes of a V3 project.
    /// </summary>
    private static bool IsGeneratedOrForeign(string relativePath)
    {
        var segments = relativePath.Split(_pathSeparators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            foreach (var skipped in _skippedDirectories)
            {
                if (string.Equals(segments[i], skipped, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception e)
        {
            Log.Warning($"Couldn't remove stale build output {path}: {e.Message}");
        }
    }

    private static readonly string[] _skippedDirectories =
        [
            "bin", "obj", ".git", FileLocations.TempSubfolder, FileLocations.MetaSubFolder,
            FileLocations.ExportSubFolder, FileLocations.AssetsSubfolder,
        ];

    private static readonly char[] _pathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
}
