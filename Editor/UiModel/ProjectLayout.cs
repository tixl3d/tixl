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
}
