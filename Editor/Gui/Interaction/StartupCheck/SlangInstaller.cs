#nullable enable
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using T3.Core.Resource.ShaderCompiling;

namespace T3.Editor.Gui.Interaction.StartupCheck;

/// <summary>
/// Installs the pinned Slang compiler into the user's own folder.
/// </summary>
/// <remarks>
/// Of the things TiXL needs and cannot bundle, this is the one it can fetch itself: the release is a plain
/// archive and <see cref="SlangShaderCompiler.PinnedInstallDirectory"/> is under the user's home, so no
/// package manager and no root are involved. The Vulkan loader and the .NET SDK both need elevation, so for
/// those TiXL can only say what to run.
/// </remarks>
internal static class SlangInstaller
{
    /// <summary>True when there is a release build for this platform and architecture.</summary>
    public static bool CanInstall => AssetName != null;

    public static string TargetDirectory => SlangShaderCompiler.PinnedInstallDirectory;

    /// <summary>
    /// Downloads and unpacks the pinned release. Blocking: it runs before the main window exists, and the
    /// editor has nothing useful to do until it finishes.
    /// </summary>
    /// <returns>Null on success, otherwise why it failed.</returns>
    public static string? TryInstall()
    {
        var assetName = AssetName;
        if (assetName == null)
        {
            return $"There is no Slang {SlangShaderCompiler.PinnedVersion} build for this system.";
        }

        var url = $"https://github.com/shader-slang/slang/releases/download/v{SlangShaderCompiler.PinnedVersion}/{assetName}";
        var staging = TargetDirectory + ".incoming";
        var archive = Path.Combine(Path.GetTempPath(), assetName);

        try
        {
            Log.Info($"Downloading {url} ...");
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(DownloadTimeoutMinutes) })
            using (var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                using var target = File.Create(archive);
                response.Content.CopyToAsync(target).GetAwaiter().GetResult();
            }

            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            Directory.CreateDirectory(staging);
            Extract(archive, staging);

            // The compiler has to be in place as a whole or not at all, so unpack beside the target and
            // swap at the end: a half-extracted folder would look like a working install.
            if (Directory.Exists(TargetDirectory))
            {
                Directory.Delete(TargetDirectory, true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(TargetDirectory)!);
            Directory.Move(staging, TargetDirectory);

            var compiler = SlangShaderCompiler.FindCompiler();
            if (compiler == null)
            {
                return $"The archive unpacked, but no slangc turned up in {TargetDirectory}.";
            }

            var version = SlangShaderCompiler.QueryVersion(compiler);
            Log.Info($"Installed slangc {version ?? "?"} in {TargetDirectory}.");
            return null;
        }
        catch (Exception e)
        {
            return $"{e.GetType().Name}: {e.Message}";
        }
        finally
        {
            TryDelete(archive);
            TryDeleteDirectory(staging);
        }
    }

    private static void Extract(string archivePath, string targetDirectory)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, targetDirectory, overwriteFiles: true);
            return;
        }

        using var file = File.OpenRead(archivePath);
        using var decompressed = new GZipStream(file, CompressionMode.Decompress);

        // Restores the executable bit from the archive, which slangc needs to run at all.
        TarFile.ExtractToDirectory(decompressed, targetDirectory, overwriteFiles: true);
    }

    /// <summary>
    /// The release asset for this machine, or null where upstream publishes none.
    /// </summary>
    private static string? AssetName
    {
        get
        {
            var version = SlangShaderCompiler.PinnedVersion;
            var architecture = RuntimeInformation.OSArchitecture switch
                                   {
                                       Architecture.X64   => "x86_64",
                                       Architecture.Arm64 => "aarch64",
                                       _                  => null,
                                   };

            if (architecture == null)
            {
                return null;
            }

            if (OperatingSystem.IsWindows())
            {
                return $"slang-{version}-windows-{architecture}.zip";
            }

            if (OperatingSystem.IsMacOS())
            {
                return $"slang-{version}-macos-{architecture}.tar.gz";
            }

            if (!OperatingSystem.IsLinux())
            {
                return null;
            }

            // Upstream also ships a build without a glibc suffix, but these are linked against an older
            // glibc and so run on more distributions.
            return architecture == "x86_64"
                       ? $"slang-{version}-linux-x86_64-glibc-2.27.tar.gz"
                       : $"slang-{version}-linux-aarch64-glibc-2.28.tar.gz";
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Leftovers in the temp folder are not worth a failure.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception)
        {
        }
    }

    private const int DownloadTimeoutMinutes = 10;
}
