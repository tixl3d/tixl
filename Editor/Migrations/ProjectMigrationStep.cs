#nullable enable
using T3.Editor.Compilation;
using T3.Editor.Migrations.ProjectFormats;

namespace T3.Editor.Migrations;

/// <summary>
/// When in the load pipeline a migration step runs. Only pre-load steps exist so far; a step that
/// needs loaded symbols (like the settings-clips-to-ops migration, currently outside the chain)
/// will add an AfterSymbolLoad phase and the runner support for it.
/// </summary>
internal enum MigrationPhase
{
    BeforeSymbolLoad,
}

/// <summary>
/// One incremental project-format migration: everything needed to bring a project from format
/// N-1 to format N (its <see cref="TargetFormat"/>). Steps are applied in order by
/// <see cref="ProjectFormatMigration"/> and never revisited once a project is stamped past them -
/// a shipped step is frozen, tested history.
/// </summary>
internal abstract class ProjectMigrationStep
{
    public abstract ProjectFormat TargetFormat { get; }

    /// <summary>The TiXL release this step first shipped with - documentation, not identity.</summary>
    public abstract Version ShipsWithEditorVersion { get; }

    public abstract string Description { get; }

    public virtual MigrationPhase Phase => MigrationPhase.BeforeSymbolLoad;

    /// <summary>
    /// Transforms the project directory and csproj from format <see cref="TargetFormat"/> - 1.
    /// Must be idempotent - a crashed run leaves the project un-stamped and the step re-applies
    /// on the next start. The runner stamps the format and saves the csproj afterwards.
    /// </summary>
    public abstract void Apply(CsProjectFile csProjectFile);
}
