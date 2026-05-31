#nullable enable
using System.IO;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Loads short documentation snippets that ship with the editor from <c>.help/embedded/&lt;id&gt;.md</c>.
/// Resolves the live repo folder in a dev checkout and the copy shipped next to the binaries in a
/// packaged release (see <see cref="ShippedContent"/>). Content is plain markdown.
/// </summary>
internal static class EmbeddedHelpLoader
{
    internal static string? TryLoad(string id)
    {
        var path = Path.Combine(ShippedContent.ResolveDirectory(".help", "embedded"), id + ".md");
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
