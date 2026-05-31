#nullable enable
using System.IO;
using T3.Core.Compilation;
using T3.Core.Settings;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Loads hand-authored release notes from <c>.help/release-notes/</c>. Alpha builds read the rolling
/// <c>alpha.md</c>; stable builds read <c>&lt;major&gt;.&lt;minor&gt;.md</c>. Content is plain markdown
/// (operator references as <c>[OpName]</c>), rendered by the editor's markdown view.
/// </summary>
internal static class ReleaseNotesLoader
{
    /// <summary>Returns the markdown for the running build, or null if no matching file exists.</summary>
    internal static string? TryLoadForCurrentVersion()
    {
        var fileName = RuntimeAssemblies.IsAlpha
                           ? "alpha.md"
                           : $"{RuntimeAssemblies.Version.Major}.{RuntimeAssemblies.Version.Minor}.md";

        var path = Path.Combine(ResolveReleaseNotesDirectory(), fileName);
        if (!File.Exists(path))
            return null;

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e)
        {
            Log.Debug($"Could not read release notes from {path}: {e.Message}");
            return null;
        }
    }

    private static string ResolveReleaseNotesDirectory()
    {
        // Walk up from the editor's bin folder to the repo root, mirroring TestSetParser. Non-dev
        // builds will need a packaged copy of .help/release-notes alongside the binaries.
        return Path.GetFullPath(Path.Combine(FileLocations.StartFolder, "..", "..", "..", "..", ".help", "release-notes"));
    }
}
