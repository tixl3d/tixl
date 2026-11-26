#nullable enable
using System.Reflection;
using System.Runtime.InteropServices;
using NewTek;

namespace Lib.Utils;

/// <summary>
/// Locates the native NDI runtime for the managed binding, which only knows the Windows library name, and tells the
/// operators whether it is there. The extension ships the runtime for Linux and macOS; without it those platforms are
/// installed separately and found through the NDI SDK's <c>NDI_RUNTIME_DIR_V6</c> convention or the system library path.
/// On Linux it also hands the runtime the FFmpeg decoder it needs for NDI|HX streams; see <see cref="PreloadHxDecoder"/>.
/// </summary>
internal static class NdiRuntime
{
    /// <summary>True where the native library loads. Probed once; the result does not change while running.</summary>
    public static bool IsAvailable => _isAvailable ??= Probe();

    /// <summary>What an operator reports while the runtime is missing.</summary>
    public static string MissingMessage { get; } = BuildMissingMessage();

    static NdiRuntime()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NDIlib).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // The binding assembly outlives a reload of this package, so the resolver of the previous load is still
            // in place. It maps the same names.
        }
    }

    private static bool Probe()
    {
        EnsureHxDecoderPreloaded();

        var found = OperatingSystem.IsWindows()
                        ? NativeLibrary.TryLoad(WindowsLibraryName, typeof(NDIlib).Assembly, null, out _)
                        : TryLoadNative(out _);
        if (!found)
            Log.Warning(MissingMessage);

        return found;
    }

    /// <summary>Answers the binding's DllImport of the Windows name with the platform's library.</summary>
    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Zero hands the lookup back to the runtime's own search, which finds the shipped DLL on Windows.
        if (OperatingSystem.IsWindows() || !libraryName.StartsWith(WindowsLibraryPrefix, StringComparison.Ordinal))
            return IntPtr.Zero;

        // A receiver created before anyone asked IsAvailable resolves the binding through this path instead.
        EnsureHxDecoderPreloaded();

        return TryLoadNative(out var handle) ? handle : IntPtr.Zero;
    }

    private static bool TryLoadNative(out IntPtr handle)
    {
        foreach (var candidate in _candidates)
        {
            if (!NativeLibrary.TryLoad(candidate, out handle))
                continue;

            if (!_loggedRuntimePath)
            {
                _loggedRuntimePath = true;
                Log.Debug($"NDI runtime loaded from {candidate}");
            }

            return true;
        }

        handle = IntPtr.Zero;
        return false;
    }

    private static string[] BuildCandidates()
    {
        var fileName = OperatingSystem.IsMacOS() ? MacLibraryName : LinuxLibraryName;
        var candidates = new List<string>();

        // The extension ships the runtime for Linux and macOS under the standard NuGet native folder, so those users
        // don't have to install it themselves. It wins over an installed runtime to keep the decoded formats predictable.
        var assemblyDirectory = Path.GetDirectoryName(typeof(NdiRuntime).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDirectory))
        {
            candidates.Add(Path.Combine(assemblyDirectory, "runtimes", NativeRuntimeFolder, "native", fileName));
            candidates.Add(Path.Combine(assemblyDirectory, fileName));
        }

        var runtimeDir = Environment.GetEnvironmentVariable(RuntimeDirVariable);
        if (!string.IsNullOrEmpty(runtimeDir))
            candidates.Add(Path.Combine(runtimeDir, fileName));

        candidates.Add(fileName);

        if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/usr/local/lib/" + MacLibraryName); // where the NDI installer for macOS puts it
        }
        else
        {
            candidates.Add("libndi.so"); // an SDK unpacked by hand, without the versioned name
        }

        return candidates.ToArray();
    }

    private static string BuildMissingMessage()
    {
        if (OperatingSystem.IsWindows())
            return $"NDI runtime not found. {WindowsLibraryName} should sit next to the Ndi package; reinstalling it restores it.";

        if (OperatingSystem.IsMacOS())
            return $"NDI runtime not found. NDI Tools alone doesn't include it: install the NDI runtime ({MacRuntimeUrl}), then restart TiXL, or set {RuntimeDirVariable} to the folder containing {MacLibraryName}.";

        return $"NDI runtime not found. The Ndi extension ships it on Linux, so reinstalling the extension usually restores it. Alternatively install it (Arch: ndi-sdk from the AUR; other distributions: the NDI runtime from {RedistUrl}), or set {RuntimeDirVariable} to the folder containing {LinuxLibraryName}.";
    }

    /// <summary>
    /// Gives the NDI runtime the H.264/H.265 decoder it lacks on Linux. The free runtime has no HX decoder of its own:
    /// at receive time it dlopen()s FFmpeg 7.1 (<c>libavcodec.so.61</c>, <c>libavutil.so.59</c>) by name and shows its
    /// "Video decoder not found" placeholder when the distribution's FFmpeg has another major version (Arch ships 9,
    /// Ubuntu 24.04 ships 6). TiXL already carries an LGPL FFmpeg 7.1 next to <c>VideoServices.dll</c> for video
    /// playback, so those files are loaded here first and promoted to the global scope; libndi then finds the same
    /// objects by soname instead of searching the system paths.
    /// </summary>
    /// <remarks>
    /// The load must go through <c>dlopen</c> with <c>RTLD_GLOBAL</c>: <see cref="NativeLibrary"/> offers no flags and
    /// loads with <c>RTLD_LOCAL</c>, which keeps the symbols out of the scope libndi resolves in. Best effort - without
    /// it, full-bandwidth NDI still works and only HX sources fall back to the placeholder.
    /// </remarks>
    private static void PreloadHxDecoder()
    {
        var directory = FindFfmpegDirectory();
        if (directory == null)
        {
            Log.Warning("NDI|HX sources need FFmpeg 7.1 (libavcodec.so.61), which was not found next to VideoServices.dll "
                        + "or on the system library path. Full-bandwidth NDI still works; install an FFmpeg 7.1 runtime to "
                        + "decode HX sources instead of NDI's 'Video decoder not found' placeholder.");
            return;
        }

        foreach (var fileName in FfmpegLibraryNames)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                Log.Warning($"NDI|HX sources need FFmpeg 7.1, but {path} is missing. NDI will show its 'Video decoder not found' placeholder.");
                return;
            }

            if (dlopen(path, RtldNow | RtldGlobal) != IntPtr.Zero)
                continue;

            var error = Marshal.PtrToStringAnsi(dlerror());
            Log.Warning($"NDI|HX sources: failed to load {path}: {error ?? "unknown error"}. NDI will show its 'Video decoder not found' placeholder.");
            return;
        }

        Log.Debug($"NDI|HX decoding uses the FFmpeg 7.1 shipped with TiXL: {directory}");
    }

    private static string? FindFfmpegDirectory()
    {
        // The Video package, bundled with every release, carries FFmpeg next to VideoServices.dll. Asking the loaded
        // assembly covers both the release layout (Operators/Video) and a source build's .temp/bin output folder.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly.GetName().Name != VideoServicesAssemblyName)
                continue;

            var location = assembly.Location;
            if (string.IsNullOrEmpty(location))
                continue;

            var directory = Path.GetDirectoryName(location);
            if (directory != null && ContainsAvcodec(directory))
                return directory;
        }

        foreach (var directory in _ffmpegDirectoryCandidates)
        {
            if (ContainsAvcodec(directory))
                return directory;
        }

        return null;
    }

    private static bool ContainsAvcodec(string directory)
        => File.Exists(Path.Combine(directory, FfmpegLibraryNames[^1]));

    private static IEnumerable<string> BuildFfmpegDirectoryCandidates()
    {
        // Release layout, and the shape a distro package keeps when it moves the editor folder.
        yield return Path.Combine(AppContext.BaseDirectory, "Operators", "Video");

        // Escape hatches for a hand-unpacked SDK or a machine with a compatible FFmpeg elsewhere.
        var runtimeDir = Environment.GetEnvironmentVariable(RuntimeDirVariable);
        if (!string.IsNullOrEmpty(runtimeDir))
            yield return runtimeDir;

        var ffmpegDir = Environment.GetEnvironmentVariable("TIXL_FFMPEG_DIR");
        if (!string.IsNullOrEmpty(ffmpegDir))
            yield return ffmpegDir;
    }

    private static void EnsureHxDecoderPreloaded()
    {
        if (_hxDecoderPreloaded)
            return;

        _hxDecoderPreloaded = true;
        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            PreloadHxDecoder();
        }
        catch (Exception e)
        {
            // A broken FFmpeg setup must not take the editor down; HX sources just keep showing the placeholder.
            Log.Warning($"NDI|HX sources: could not preload FFmpeg: {e.Message}");
        }
    }

    [DllImport("libdl.so.2", EntryPoint = "dlopen", CharSet = CharSet.Ansi)]
    private static extern IntPtr dlopen(string fileName, int flags);

    [DllImport("libdl.so.2", EntryPoint = "dlerror")]
    private static extern IntPtr dlerror();

    private const string WindowsLibraryPrefix = "Processing.NDI.Lib";
    private const string WindowsLibraryName = "Processing.NDI.Lib.x64.dll";
    private const string LinuxLibraryName = "libndi.so.6";
    private const string MacLibraryName = "libndi.dylib";
    private const string MacRuntimeUrl = "https://downloads.ndi.tv/SDK/NDI_SDK_Mac/libNDI_for_Mac.pkg";

    /** Set by the NDI installers, and the SDK's documented way for applications to find the runtime. */
    private const string RuntimeDirVariable = "NDI_RUNTIME_DIR_V6";

    /** The SDK's standardized download location (NDILIB_REDIST_URL), for users who prefer an installed runtime. */
    private const string RedistUrl = "http://ndi.link/NDIRedistV6";

    private const string VideoServicesAssemblyName = "VideoServices";
    private const int RtldNow = 0x2;
    private const int RtldGlobal = 0x100;

    /** Where the extension ships its native runtime, mirroring the package's runtimes/<rid>/native folder. */
    private static string NativeRuntimeFolder
        => OperatingSystem.IsMacOS() ? "osx-arm64" : "linux-x64";

    /** What libndi dlopen()s for NDI|HX. Dependencies first, so libavcodec resolves against the loaded objects. */
    private static readonly string[] FfmpegLibraryNames =
        [
            "libavutil.so.59",
            "libswresample.so.5",
            "libswscale.so.8",
            "libavcodec.so.61",
        ];

    private static readonly string[] _ffmpegDirectoryCandidates = BuildFfmpegDirectoryCandidates().ToArray();

    private static readonly string[] _candidates = BuildCandidates();
    private static bool? _isAvailable;
    private static bool _hxDecoderPreloaded;
    private static bool _loggedRuntimePath;
}
