#nullable enable
using T3.Core.Settings;

namespace T3.Editor.UiModel;

/// <summary>
/// The canonical definition of what makes up a TiXL project on disk: the root csproj plus these
/// subdirectories. Backups, share export, and import all work from this allowlist - everything
/// else in a project folder (bin, obj, .git, .temp, Export, render output, stray user files) is
/// local state they deliberately ignore.
/// </summary>
internal static class ProjectLayout
{
    public static readonly string[] ContentSubdirectories =
        [
            FileLocations.SymbolsSubfolder,
            FileLocations.AssetsSubfolder,
            FileLocations.DependenciesFolder,
            FileLocations.MetaSubFolder,
        ];

    /// <summary>
    /// Generated or derived state that is never project content: build output, version control,
    /// backups/temp, and player exports. Conservative sweeps over legacy layouts skip these; media
    /// output folders (Render, Screenshots) are deliberately not listed - a legacy project may keep
    /// a namespace of the same name at its root.
    /// </summary>
    public static readonly string[] GeneratedStateDirectories =
        [
            "bin", "obj", ".git", ".temp", FileLocations.ExportSubFolder,
        ];
}
