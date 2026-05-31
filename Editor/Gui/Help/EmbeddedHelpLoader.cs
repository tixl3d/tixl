#nullable enable
using System.IO;
using T3.Core.Settings;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Loads short documentation snippets that ship with the editor from <c>.help/embedded/&lt;id&gt;.md</c>.
/// The folder is copied next to the binaries at build time (see Editor.csproj), so the same path
/// resolves in both a dev checkout and a packaged release. Content is plain markdown.
/// </summary>
internal static class EmbeddedHelpLoader
{
    internal static string? TryLoad(string id)
    {
        var path = Path.Combine(FileLocations.StartFolder, ".help", "embedded", id + ".md");
        if (!File.Exists(path))
            return null;

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e)
        {
            Log.Debug($"Could not read embedded help '{id}': {e.Message}");
            return null;
        }
    }
}
