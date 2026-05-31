#nullable enable
using System.IO;
using T3.Core.Settings;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Resolves content folders that ship with the editor (release notes, guided tests, embedded help).
/// Prefers the repo source so a dev checkout edits live files; falls back to the copy placed next to
/// the binaries at build time, which is the only thing present in a packaged release.
/// </summary>
internal static class ShippedContent
{
    internal static string ResolveDirectory(params string[] segments)
    {
        var relative = Path.Combine(segments);

        // Dev layout: <repo>/Editor/bin/<config>/<tfm>/ → four levels up is the repo root.
        var repoPath = Path.GetFullPath(Path.Combine(FileLocations.StartFolder, "..", "..", "..", "..", relative));
        if (Directory.Exists(repoPath))
            return repoPath;

        // Packaged release: the folder was copied next to the binaries (see Editor.csproj copy targets).
        return Path.Combine(FileLocations.StartFolder, relative);
    }
}
