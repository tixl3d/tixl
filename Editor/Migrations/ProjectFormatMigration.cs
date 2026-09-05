#nullable enable
using System.IO;
using T3.Editor.Compilation;
using T3.Editor.Migrations.ProjectFormats;

namespace T3.Editor.Migrations;

/// <summary>
/// Walks a project through the ordered <see cref="ProjectMigrationStep"/> chain until it reaches
/// <see cref="FormatHelper.Current"/>, with one pinned backup before the first applied step.
/// The project's position in the chain is the csproj's <c>ProjectFormatVersion</c>; artifacts
/// without a marker (formats before it existed, hand-unpacked share packages, restored backups)
/// are classified by <see cref="FormatHelper"/> content sniffing.
///
/// Symbol-data migrations that predate the format counter (asset paths, variations, settings-list
/// audio clips) run separately from the startup phase - they are content-gated and idempotent.
/// </summary>
internal static class ProjectFormatMigration
{
    /// <summary>Ordered by <see cref="ProjectMigrationStep.TargetFormat"/>.</summary>
    private static readonly ProjectMigrationStep[] _steps =
        [
            new Steps.To2_SymbolsFolder(),
            new Steps.To3_BuildOutputToTemp(),
        ];

    public static void MigrateIfNeeded(CsProjectFile csProjectFile)
    {
        var projectFolder = Path.GetFullPath(csProjectFile.Directory);

        // Built-in packages are migrated in the repository, never at runtime
        if (projectFolder.StartsWith(ProjectSetup.BuiltInOperatorDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        var format = csProjectFile.ProjectFormat;
        if (format == ProjectFormat.Unknown)
            format = FormatHelper.GuessFormatForDirectory(projectFolder);

        if (format == ProjectFormat.Unknown)
        {
            // An empty or freshly unpacked project without recognizable content: treat as oldest -
            // every step is idempotent and tolerates content that isn't there.
            format = ProjectFormat.V1;
        }

        if (format >= FormatHelper.Current)
        {
            // Up to date, but make sure the marker itself is stamped (it may have been inferred)
            if (csProjectFile.ProjectFormat != format)
                csProjectFile.SetProjectFormat(format);

            return;
        }

        try
        {
            if (Gui.AutoBackup.AutoBackup.CreatePinnedBackup(projectFolder, $"preFormat{(int)FormatHelper.Current}", out var backupPath))
            {
                Log.Info($"Created backup before project format migration: {backupPath}");
            }

            foreach (var step in _steps)
            {
                if (step.TargetFormat <= format)
                    continue;

                if (step.Phase != MigrationPhase.BeforeSymbolLoad)
                    throw new NotSupportedException($"Migration phase {step.Phase} is not supported yet.");

                Log.Info($"Migrating \"{csProjectFile.Name}\" to project format {(int)step.TargetFormat}: {step.Description}...");
                step.Apply(csProjectFile);
                csProjectFile.SetProjectFormat(step.TargetFormat);
                format = step.TargetFormat;
            }
        }
        catch (Exception e)
        {
            // The project stays stamped at the last completed step, so the next start resumes from
            // there; the pinned backup preserves the pre-migration state.
            Log.Error($"Failed to migrate project \"{csProjectFile.Name}\" to the current format: {e}");
        }
    }
}
