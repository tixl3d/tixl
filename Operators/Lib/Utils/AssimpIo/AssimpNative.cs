#nullable enable
using System.Runtime.InteropServices;
using System.Threading;
using Assimp.Unmanaged;

namespace Lib.Utils.AssimpIo;

/// <summary>
/// Loads the native Assimp library bundled with this package and serializes all calls into it.
/// </summary>
/// <remarks>
/// AssimpNet resolves its native library with its own loader, so the operator package's
/// NuGet <c>runtimes/&lt;rid&gt;/native</c> folder is probed explicitly. The bundled build is
/// preferred over a system-wide libassimp, whose ABI may not match the managed wrapper.
/// The bundled native build has no multithreading support, so every import and export
/// must hold <see cref="Lock"/>.
/// </remarks>
internal static class AssimpNative
{
    /// <summary>Held around every call into the native library.</summary>
    public static readonly object Lock = new();

    /// <summary>
    /// Loads the native library once. Safe to call from any thread; failures are cached so a missing
    /// library doesn't trigger a probe on every load request.
    /// </summary>
    public static bool TryEnsureLoaded([NotNullWhen(false)] out string? errorMessage)
    {
        lock (Lock)
        {
            if (_isLoaded)
            {
                errorMessage = null;
                return true;
            }

            if (_loadError != null)
            {
                errorMessage = _loadError;
                return false;
            }

            try
            {
                var library = AssimpLibrary.Instance;
                library.ThrowOnLoadFailure = false;

                if (!library.IsLibraryLoaded)
                {
                    foreach (var candidate in GetBundledLibraryCandidates())
                    {
                        if (File.Exists(candidate) && library.LoadLibrary(candidate))
                            break;
                    }
                }

                // Fall back to AssimpNet's own probing (app folder, system paths)
                if (!library.IsLibraryLoaded && !library.LoadLibrary())
                {
                    _loadError = $"Native Assimp library could not be loaded for {RuntimeInformation.RuntimeIdentifier}.";
                    errorMessage = _loadError;
                    return false;
                }

                var majorVersion = library.GetVersionMajor();
                if (majorVersion < MinimumNativeMajorVersion)
                {
                    library.FreeLibrary();
                    _loadError = $"Native Assimp {library.GetVersion()} is too old (needs {MinimumNativeMajorVersion}.x). "
                                 + $"Loaded from: {library.LibraryPath}";
                    errorMessage = _loadError;
                    return false;
                }

                Volatile.Write(ref _version, library.GetVersion());
                _isLoaded = true;
                errorMessage = null;
                return true;
            }
            catch (Exception e)
            {
                _loadError = $"Native Assimp library failed to load: {e.Message}";
                errorMessage = _loadError;
                return false;
            }
        }
    }

    /// <summary>
    /// Version string of the loaded native library, empty before the first successful load. Read from
    /// the render thread while a worker loads the library, hence the volatile access.
    /// </summary>
    public static string Version => Volatile.Read(ref _version);

    private static IEnumerable<string> GetBundledLibraryCandidates()
    {
        var fileName = OperatingSystem.IsWindows() ? "assimp.dll"
                       : OperatingSystem.IsMacOS() ? "libassimp.dylib"
                       : "libassimp.so";

        var osName = OperatingSystem.IsWindows() ? "win"
                     : OperatingSystem.IsMacOS() ? "osx"
                     : "linux";

        var architecture = RuntimeInformation.ProcessArchitecture switch
                               {
                                   Architecture.X64   => "x64",
                                   Architecture.Arm64 => "arm64",
                                   Architecture.X86   => "x86",
                                   _                  => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                               };

        var runtimeFolder = Path.Combine("runtimes", $"{osName}-{architecture}", "native");

        foreach (var baseFolder in GetBaseFolders())
        {
            yield return Path.Combine(baseFolder, runtimeFolder, fileName);
            yield return Path.Combine(baseFolder, fileName);
        }
    }

    private static IEnumerable<string> GetBaseFolders()
    {
        // Operator packages load from memory in some setups, which leaves Location empty
        var packageFolder = Path.GetDirectoryName(typeof(AssimpNative).Assembly.Location);
        if (!string.IsNullOrEmpty(packageFolder))
            yield return packageFolder;

        var wrapperFolder = Path.GetDirectoryName(typeof(AssimpLibrary).Assembly.Location);
        if (!string.IsNullOrEmpty(wrapperFolder) && wrapperFolder != packageFolder)
            yield return wrapperFolder;

        yield return AppContext.BaseDirectory;
    }

    private const int MinimumNativeMajorVersion = 6;

    private static bool _isLoaded;
    private static string? _loadError;
    private static string _version = string.Empty;
}
