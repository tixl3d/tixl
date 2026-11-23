#nullable enable
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using T3.Core.Compilation;
using T3.Core.Settings;

namespace T3.Editor.ExtensionPackages;

/// <summary>
/// Optional operator packages installed into the settings folder (one per TiXL version, so an extension never meets
/// an editor it wasn't built for). Installing and removing only stage changes; they are applied at the next start,
/// before any package is loaded, so nothing in use is ever replaced.
/// </summary>
internal static class InstalledExtensions
{
    internal sealed record InstalledExtension(string Id, string Title, string Version, string Sha256);

    internal sealed record PendingChanges(Dictionary<string, InstalledExtension> Install, List<string> Remove)
    {
        public static PendingChanges Empty => new([], []);

        [JsonIgnore]
        public bool IsEmpty => Install.Count == 0 && Remove.Count == 0;
    }

    public static readonly string Directory = Path.Combine(FileLocations.SettingsDirectory, "Extensions");

    /** The platform name used in TixlPlatforms and in the extension index, e.g. "win-x64". */
    public static readonly string CurrentPlatform =
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux")
        + "-" + RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    /** Installed extensions by name (the package folder name, e.g. "AbletonLink"). */
    public static Dictionary<string, InstalledExtension> ReadInstalled() => Read<Dictionary<string, InstalledExtension>>(InstalledPath) ?? [];

    public static PendingChanges ReadPending() => Read<PendingChanges>(PendingPath) ?? PendingChanges.Empty;

    /** Folder that receives a downloaded extension before the restart moves it into place. */
    public static string StagingDirectory(string name) => Path.Combine(Directory, PendingFolderName, name);

    public static void WritePending(PendingChanges pending)
    {
        System.IO.Directory.CreateDirectory(Directory);
        if (pending.IsEmpty)
        {
            File.Delete(PendingPath);
            return;
        }

        File.WriteAllText(PendingPath, JsonSerializer.Serialize(pending, _jsonOptions));
    }

    /// <summary>
    /// Moves staged installs into place and deletes removed extensions. Call once at startup, before packages load.
    /// A failure leaves that extension as it was and is logged; the other changes still apply.
    /// </summary>
    public static void ApplyPendingChanges()
    {
        var pending = ReadPending();
        if (pending.IsEmpty)
            return;

        var installed = ReadInstalled();
        foreach (var name in pending.Remove)
        {
            try
            {
                var directory = Path.Combine(Directory, name);
                if (System.IO.Directory.Exists(directory))
                    System.IO.Directory.Delete(directory, recursive: true);
                installed.Remove(name);
                Log.Info($"Removed extension {name}");
            }
            catch (Exception e)
            {
                Log.Warning($"Could not remove extension {name}: {e.Message}");
            }
        }

        foreach (var (name, extension) in pending.Install)
        {
            var staging = StagingDirectory(name);
            if (!System.IO.Directory.Exists(staging))
            {
                Log.Warning($"Extension {name} was staged for installation, but its files are gone.");
                continue;
            }

            try
            {
                var directory = Path.Combine(Directory, name);
                if (System.IO.Directory.Exists(directory))
                    System.IO.Directory.Delete(directory, recursive: true);
                System.IO.Directory.Move(staging, directory);
                installed[name] = extension;
                Log.Info($"Installed extension {extension.Title} {extension.Version}");
            }
            catch (Exception e)
            {
                Log.Warning($"Could not install extension {name}: {e.Message}");
            }
        }

        File.WriteAllText(InstalledPath, JsonSerializer.Serialize(installed, _jsonOptions));
        WritePending(PendingChanges.Empty);
        var pendingFolder = Path.Combine(Directory, PendingFolderName);
        if (System.IO.Directory.Exists(pendingFolder))
            System.IO.Directory.Delete(pendingFolder, recursive: true);
    }

    /// <summary>
    /// The package folders of installed extensions that this editor can load. Skips extensions built for another
    /// TiXL version and those whose package id is already loaded (in a source build the package compiles from the
    /// repository instead).
    /// </summary>
    public static IEnumerable<string> LoadablePackageDirectories(Func<Guid, bool> isPackageLoaded, Version editorVersion)
    {
        if (!System.IO.Directory.Exists(Directory))
            yield break;

        foreach (var directory in System.IO.Directory.EnumerateDirectories(Directory))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.'))
                continue;

            if (!AssemblyInformation.TryLoadReleaseInfo(directory, out var releaseInfo))
                continue;

            if (releaseInfo.EditorVersion.Major != editorVersion.Major || releaseInfo.EditorVersion.Minor != editorVersion.Minor)
            {
                Log.Warning($"Skipping extension {name}: built for TiXL {releaseInfo.EditorVersion}, this is {editorVersion}. "
                            + "Install it again from Settings → Extensions.");
                continue;
            }

            if (isPackageLoaded(releaseInfo.PackageId))
            {
                Log.Debug($"Skipping installed extension {name}: the same package is already loaded.");
                continue;
            }

            yield return directory;
        }
    }

    private static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _jsonOptions);
        }
        catch (Exception e)
        {
            Log.Warning($"Could not read {path}: {e.Message}");
            return null;
        }
    }

    private const string PendingFolderName = ".pending";
    private static string InstalledPath => Path.Combine(Directory, "installed.json");
    private static string PendingPath => Path.Combine(Directory, "pending.json");
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
}
