#nullable enable
using System.IO;
using T3.Core.Compilation;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Loads hand-authored release notes from <c>.help/release-notes/v&lt;major&gt;.&lt;minor&gt;.md</c>.
/// One file per minor version, shared by its alpha and stable builds. Content is plain markdown
/// (operator references as <c>[OpName]</c>), rendered by the editor's markdown view.
/// </summary>
internal static class ReleaseNotesLoader
{
    /// <summary>Returns the markdown for the running build's version, or null if no matching file exists.</summary>
    internal static string? TryLoadForCurrentVersion()
    {
        var fileName = $"v{RuntimeAssemblies.Version.Major}.{RuntimeAssemblies.Version.Minor}.md";
        var path = Path.Combine(ShippedContent.ResolveDirectory(".help", "release-notes"), fileName);
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
}
