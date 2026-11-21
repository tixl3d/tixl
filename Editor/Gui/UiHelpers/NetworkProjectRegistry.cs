#nullable enable
using System.IO;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Tracks which project folders participate in a show-control feature (boygrouping deploy
/// targets, collab downloads, failover pair members). The Hub shows these under "Network
/// projects" so a show machine's operator sees at a glance which projects are part of the
/// distributed show. Persisted in the user settings; entries whose folder disappeared are
/// pruned on save.
/// </summary>
internal static class NetworkProjectRegistry
{
    public const string BoygroupKind = "boygroup";
    public const string CollabKind = "collab";

    /// <summary>Registers a folder under a feature kind. Returns false when the folder doesn't exist.</summary>
    public static bool Register(string kind, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return false;

        var full = Normalize(folder);
        var refString = $"{kind}:{full}";
        var config = UserSettings.Config;
        if (config.NetworkProjectRefs.Contains(refString))
            return true;

        config.NetworkProjectRefs.Add(refString);
        UserSettings.Save();
        Log.Info($"Network projects: '{full}' registered for {kind}");
        return true;
    }

    /// <summary>The feature kind a folder is registered under, or null.</summary>
    public static string? KindFor(string folder)
    {
        var full = Normalize(folder);
        foreach (var entry in UserSettings.Config.NetworkProjectRefs)
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0)
                continue;

            if (string.Equals(entry[(separator + 1)..], full, StringComparison.OrdinalIgnoreCase))
                return entry[..separator];
        }

        return null;
    }

    /// <summary>All registered folders paired with their kind, pruned to existing folders.</summary>
    public static List<(string Kind, string Folder)> All()
    {
        var result = new List<(string, string)>();
        var config = UserSettings.Config;
        var changed = false;
        foreach (var entry in config.NetworkProjectRefs)
        {
            var separator = entry.IndexOf(':');
            if (separator <= 0)
            {
                changed = true;
                config.NetworkProjectRefs.Remove(entry);
                continue;
            }

            var kind = entry[..separator];
            var folder = entry[(separator + 1)..];
            if (!Directory.Exists(folder))
            {
                changed = true;
                config.NetworkProjectRefs.Remove(entry);
                continue;
            }

            result.Add((kind, folder));
        }

        if (changed)
            UserSettings.Save();

        return result;
    }

    private static string Normalize(string folder) =>
        Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
