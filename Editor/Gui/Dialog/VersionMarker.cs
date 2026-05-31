#nullable enable
using System.IO;
using T3.Core.Compilation;
using T3.Core.Settings;
using T3.Serialization;

namespace T3.Editor.Gui.Dialog;

/// <summary>
/// Records the last TiXL version that ran in this settings folder. Lets the editor tell when a
/// user is running a version they haven't run here before and show the welcome / what's-new popup.
/// </summary>
internal static class VersionMarker
{
    public enum LaunchKind
    {
        /// <summary>Same version as the previous run — no popup.</summary>
        Silent,

        /// <summary>No marker yet, or the marker is older than the running build — show the welcome popup.</summary>
        NewToUser,

        /// <summary>Marker records a newer version than the running build — the user downgraded; stay quiet and keep the marker.</summary>
        Downgrade,
    }

    /// <summary>Version recorded by the previous run, or <c>null</c> if this folder has no marker yet.</summary>
    public static Version? LastRunVersion => _lastRunVersion.Value;

    public static LaunchKind Classify()
    {
        var previous = LastRunVersion;
        if (previous == null)
            return LaunchKind.NewToUser;

        var comparison = RuntimeAssemblies.Version.CompareTo(previous);
        return comparison switch
                   {
                       > 0 => LaunchKind.NewToUser,
                       < 0 => LaunchKind.Downgrade,
                       _   => LaunchKind.Silent,
                   };
    }

    /// <summary>
    /// Stamps the running version into the marker file. Refuses to lower the recorded version so a
    /// downgrade run doesn't suppress the welcome popup for the newer build the user came from.
    /// </summary>
    public static void MarkCurrentVersionSeen()
    {
        var previous = LastRunVersion;
        if (previous != null && RuntimeAssemblies.Version.CompareTo(previous) < 0)
            return;

        var data = new MarkerData { LastRunVersion = RuntimeAssemblies.Version.ToString() };
        if (JsonUtils.TrySaveJson(data, FilePath))
            _lastRunVersion = new Lazy<Version?>(RuntimeAssemblies.Version);
    }

    private static Version? LoadLastRunVersion()
    {
        var data = JsonUtils.TryLoadingJson<MarkerData>(FilePath);
        if (data == null || string.IsNullOrEmpty(data.LastRunVersion))
            return null;

        return Version.TryParse(data.LastRunVersion, out var version) ? version : null;
    }

    private static string FilePath => Path.Combine(FileLocations.SettingsDirectory, "versionMarker.json");

    private static Lazy<Version?> _lastRunVersion = new(LoadLastRunVersion);

    private sealed class MarkerData
    {
        public string? LastRunVersion { get; set; }
    }
}
