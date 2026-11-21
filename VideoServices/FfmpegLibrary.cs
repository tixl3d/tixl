using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;
using T3.Core.Logging;
using SdcbLogLevel = Sdcb.FFmpeg.Raw.LogLevel;

namespace T3.VideoServices;

/// <summary>
/// One-time, thread-safe entry point for the bundled FFmpeg libraries. Triggers native load, records the
/// version, and enforces the LGPL distribution guarantee by rejecting GPL/non-free builds. Every decode
/// path must call <see cref="EnsureInitialized"/> before touching any libav* API.
/// </summary>
public static class FfmpegLibrary
{
    /// <summary>True once the native libraries loaded and passed the license check.</summary>
    public static bool IsAvailable { get; private set; }

    /// <summary>Human-readable FFmpeg version string (e.g. "7.0"), or null until initialized.</summary>
    public static string? VersionInfo { get; private set; }

    /// <summary>
    /// Non-null when initialization failed (native load failure or a disallowed GPL/non-free build).
    /// Operators surface this through <c>IStatusProvider</c>.
    /// </summary>
    public static string? StatusError { get; private set; }

    /// <summary>
    /// Dev/test escape hatch: when true, a GPL/non-free build is allowed (with a loud warning) instead of
    /// rejected. The GPL flag is a *distribution* concern — decode behavior is identical — so local testing
    /// against whatever build is installed is fine. Off by default so the shipped editor always enforces LGPL.
    /// </summary>
    internal static bool AllowRestrictedBuildForTesting;

    /// <summary>
    /// Idempotent. Returns true when FFmpeg is usable. Safe to call from any thread and every frame —
    /// the work runs exactly once.
    /// </summary>
    public static bool EnsureInitialized()
    {
        if (_initialized)
            return IsAvailable;

        lock (_initLock)
        {
            if (_initialized)
                return IsAvailable;

            Initialize();
            _initialized = true;
        }

        return IsAvailable;
    }

    private static void Initialize()
    {
        PreloadNativeLibraries();

        string configuration;
        try
        {
            // First libav* call — forces the native DLLs to resolve. Throws if they can't be found.
            VersionInfo = ffmpeg.av_version_info();
            configuration = ffmpeg.avcodec_configuration();
        }
        catch (Exception e)
        {
            // A TypeInitializationException's own message is generic; the real cause (DllNotFound, bad image,
            // missing dependency) is in the inner exception.
            var cause = e.InnerException ?? e;
            StatusError = $"FFmpeg libraries could not be loaded: {cause.GetType().Name}: {cause.Message}";
            Log.Warning(StatusError);
            Log.Warning("FFmpeg load failure detail: " + e);
            return;
        }

        // The first libav* call above also reveals where Sdcb's P/Invokes actually resolved the natives.
        // That need not be this assembly's folder (the operator package is loaded from its build output
        // while VideoServices can run from the editor's shadow copy), and the device demuxers must be
        // registered into the same libavformat Sdcb talks to, not the copy the preload happened to load.
        AdoptResolvedNativeFolder();

        // The editor ships an LGPL shared build. A GPL/non-free build dropped in by a user would silently
        // break the project's MIT/LGPL distribution guarantee, so refuse to use it. Developers can opt in to
        // running against whatever build they have installed via the TIXL_FFMPEG_ALLOW_RESTRICTED env var.
        var isRestricted = configuration.Contains("--enable-gpl") || configuration.Contains("--enable-nonfree");
        var allowRestricted = AllowRestrictedBuildForTesting
                              || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TIXL_FFMPEG_ALLOW_RESTRICTED"));
        if (isRestricted && !allowRestricted)
        {
            StatusError = "Bundled FFmpeg build is GPL/non-free and not permitted — an LGPL build is required. "
                          + "A system-wide FFmpeg may have been picked up instead of the bundled libraries (see the log); "
                          + "for development, TIXL_FFMPEG_ALLOW_RESTRICTED=1 allows it.";
            Log.Warning(StatusError);
            // Identifies WHICH build answered — the bundled LGPL build has no --enable-gpl, so seeing it here
            // means resolution fell through to a system-wide libavcodec.
            var configurationHead = configuration.Length <= 400 ? configuration : configuration[..400] + "…";
            Log.Warning($"Rejected FFmpeg configuration: {configurationHead}");
            return;
        }

        IsAvailable = true;
        RegisterDeviceDemuxers();
        ConfigureLogging();
        var licenseLabel = isRestricted ? "GPL/non-free — development build" : "LGPL";
        T3.Core.Resource.ThirdPartyRuntimeInfo.Register("FFmpeg", $"{VersionInfo} ({licenseLabel})");
        if (isRestricted)
            Log.Warning($"FFmpeg {VersionInfo} loaded but is a GPL/non-free build — for development/testing only, NOT for distribution.");
        else
            Log.Debug($"FFmpeg {VersionInfo} loaded (LGPL).");
    }

    // libavdevice's demuxers (v4l2 on Linux, dshow on Windows, ...) are invisible to av_find_input_format
    // until avdevice_register_all runs: shared FFmpeg has no load-time hook, the application must opt in
    // (the ffmpeg CLI does). Without it, a forced device format resolves to null and avformat falls back
    // to byte-probing the device node, which cameras without V4L2_CAP_READWRITE reject with EINVAL.
    //
    // The export is invoked through an explicitly loaded handle instead of a DllImport: a plain DllImport
    // can be hijacked by a system-wide libavdevice (a different FFmpeg copy entirely), whose registration
    // would land in the system libavformat and never reach the bundled one this process decodes with.
    private static void RegisterDeviceDemuxers()
    {
        var libraryName = OperatingSystem.IsWindows() ? "avdevice-61.dll"
                          : OperatingSystem.IsLinux() ? "libavdevice.so.61"
                          : "libavdevice.61.dylib";

        var libraryPath = FindBundledLibrary(libraryName);
        if (libraryPath == null)
        {
            Log.Warning($"FFmpeg device library '{libraryName}' not found next to VideoServices — device capture will be unavailable.");
            return;
        }

        try
        {
            // libavdevice binds its own libavformat dependency by soname at load time — make sure the bundled
            // copy is in the process first, or (with a system FFmpeg installed) the registration below would
            // land in the system's libavformat instead of the one this process decodes with. No-op when the
            // preload already loaded it. avfilter is loaded here too (from the same folder) because it
            // imports libavformat and would otherwise load the copy next to whichever module pulled it in.
            var formatName = OperatingSystem.IsWindows() ? "avformat-61.dll"
                             : OperatingSystem.IsMacOS() ? "libavformat.61.dylib"
                             : "libavformat.so.61";
            var formatPath = FindBundledLibrary(formatName);
            if (formatPath != null)
                NativeLibrary.TryLoad(formatPath, out _);

            if (FindBundledLibrary(FilterLibraryName) is { } filterPath)
                NativeLibrary.TryLoad(filterPath, out _);

            if (!NativeLibrary.TryLoad(libraryPath, out var handle))
            {
                Log.Warning($"Could not load '{libraryPath}' — device capture will be unavailable.");
                return;
            }

            var register = (RegisterDeviceDemuxersDelegate)Marshal.GetDelegateForFunctionPointer(
                               NativeLibrary.GetExport(handle, "avdevice_register_all"),
                               typeof(RegisterDeviceDemuxersDelegate));
            register();

            // The handle is intentionally never freed: avdevice_register_all stores pointers into
            // avdevice's own static memory in libavformat's demuxer list, so unloading the module leaves
            // dangling entries — NativeLibrary.Free here makes the next av_demuxer_iterate read unmapped
            // memory (an access violation). The module is part of the FFmpeg stack for the process lifetime.

            var deviceFormats = InputFormat.All
                                           .Select(f => f.Name)
                                           .ToList();
            var deviceNames = deviceFormats.Where(n => n.Contains("v4l2") || n.Contains("dshow") || n.Contains("avfoundation")).ToList();
            Log.Debug($"FFmpeg device demuxers registered: {(deviceNames.Count > 0 ? string.Join(", ", deviceNames) : "NONE FOUND")} " +
                      $"({deviceFormats.Count} demuxers total, natives from '{_nativeFolder}', " +
                      $"from '{typeof(FfmpegLibrary).Assembly.Location}')");
        }
        catch (Exception e)
        {
            Log.Warning($"Could not register FFmpeg device demuxers: {e.Message} — device capture will be unavailable.");
        }
    }

    private delegate void RegisterDeviceDemuxersDelegate();

    private static string FilterLibraryName => OperatingSystem.IsWindows() ? "avfilter-10.dll"
                                              : OperatingSystem.IsMacOS() ? "libavfilter.10.dylib"
                                              : "libavfilter.so.10";

    // Route libav* logging through TiXL's logger instead of FFmpeg's default raw-stderr callback. Benign,
    // repetitive notices (e.g. swscale's "deprecated pixel format" warning when decoding full-range sources)
    // land at Debug so they don't flood the console; genuine errors stay visible at Warning.
    private static void ConfigureLogging()
    {
        FFmpegLogger.LogLevel = SdcbLogLevel.Warning; // drop FFmpeg's Info/Verbose chatter at the source
        // Some libav* messages are logged at error level but are harmless metadata notices that recur on every
        // file open — keep them out of the warning stream (they land at Debug). Substring match keeps it cheap.
        static bool IsBenignNotice(string text)
            => text.Contains("Referenced QT chapter track not found")
               || text.Contains("unable to decode APP fields");

        // swscale repeats its "deprecated pixel format used" notice once per scaled frame of a full-range
        // (MJPEG, IP-camera) source — a per-frame fact, not a session fact, so even Debug floods the log
        // with it. Everyone who needs to know it already knows it; drop the line outright.
        static bool IsPerFrameNoise(string text)
            => text.Contains("deprecated pixel format used");

        FFmpegLogger.LogWriter = static (level, message) =>
                                 {
                                     var text = message?.TrimEnd();
                                     if (string.IsNullOrEmpty(text) || IsPerFrameNoise(text))
                                         return;

                                     // Webcams emit MJPEG frames with a malformed APPn segment; the decoder still outputs
                                     // the image, but the notice repeats per frame, so only the first of a burst is kept.
                                     if (text.Contains("unable to decode APP fields"))
                                     {
                                         var now = Environment.TickCount64;
                                         if (now - _lastAppFieldNoticeTick < AppFieldNoticeIntervalMs)
                                             return;

                                         _lastAppFieldNoticeTick = now;
                                     }

                                     if (level <= SdcbLogLevel.Error && !IsBenignNotice(text)) // Panic, Fatal, Error
                                         Log.Warning("FFmpeg: " + text);
                                     else
                                         Log.Debug("FFmpeg: " + text);
                                 };
    }

    /// <summary>
    /// Routes FFmpeg's log output into <paramref name="collect"/> while <paramref name="action"/> runs, then
    /// restores the standard logging configuration. Needed for the one log-scraping case in the codebase —
    /// avfoundation's <c>list_devices</c> device enumeration, which "prints" its device list through av_log
    /// at Info level: Sdcb's log level gate drops those messages before the writer sees them, so the floor
    /// has to be lowered for the duration. The writer and level are process-global and LogWriter is
    /// write-only (it cannot be saved/restored from outside), so only this class may open that window —
    /// keep <paramref name="action"/> short, other threads' FFmpeg messages land in the collector too.
    /// </summary>
    public static void WithLogCapture(Action<string> collect, Action action)
    {
        FFmpegLogger.LogLevel = SdcbLogLevel.Info;
        FFmpegLogger.LogWriter = (level, message) =>
                                 {
                                     if (level <= SdcbLogLevel.Info && message != null)
                                         collect(message);
                                 };
        try
        {
            action();
        }
        finally
        {
            ConfigureLogging();
        }
    }

    // The FFmpeg libraries live next to this assembly, which is not on the OS library search path inside the
    // editor's custom operator load context. So a library's inter-dependencies (avcodec → avutil/swresample)
    // won't resolve by name. Pre-load each by full path, dependencies first, so dependencies are already in
    // the process by the time something needs them - the loader then matches them by name and loads nothing
    // twice. The order is load-bearing beyond convenience: when libavdevice gets loaded before libavformat,
    // its by-soname dependency bind falls through to a SYSTEM-wide libavformat if one is installed, and
    // avdevice_register_all then registers its demuxers into that copy - invisible to the bundled libavformat
    // this process decodes with.
    private static void PreloadNativeLibraries()
    {
        var directory = Path.GetDirectoryName(typeof(FfmpegLibrary).Assembly.Location);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        // The shipped layout has the libraries flat next to this assembly; NuGet package references keep them
        // under runtimes/<rid>/native (test hosts, project-reference consumers). Without a full-path preload, a
        // name-based request for e.g. "libavcodec.so.61" falls through to the OS loader and can pick up a
        // system-wide FFmpeg — a different build with a different codec set (and possibly GPL) — before the
        // bundled one is ever seen.
        //
        // Pick exactly one folder for the whole process. Loading more than one copy of libavformat is the
        // failure this must avoid: avdevice_register_all then registers its demuxers into whichever copy
        // libavdevice bound to, while Sdcb's enumeration reads the other one — the libraries load fine and
        // file decoding works, but dshow/v4l2 report as "not found in this FFmpeg build".
        _nativeFolder = BundledLibraryFolders(directory!).FirstOrDefault(f => Directory.EnumerateFiles(f).Any(IsFfmpegLibrary));

        if (OperatingSystem.IsMacOS() && _nativeFolder == null)
            UseInstalledLibrariesOnMac();

        if (_nativeFolder == null)
            return;

        foreach (var path in OrderedFfmpegLibraries(_nativeFolder))
        {
            // avfilter/avformat/avdevice are deliberately deferred: avfilter imports avformat, so loading
            // it here would pin libavformat to this folder before we know where Sdcb's P/Invokes resolve
            // their natives — and avdevice_register_all must register into that same libavformat.
            // RegisterDeviceDemuxers loads the three from the resolved folder once it is known.
            var fileName = Path.GetFileName(path);
            if (fileName.Contains("avfilter", StringComparison.Ordinal)
                || fileName.Contains("avformat", StringComparison.Ordinal)
                || fileName.Contains("avdevice", StringComparison.Ordinal))
            {
                continue;
            }

            NativeLibrary.TryLoad(path, out _);
        }
    }

    /// <summary>
    /// Points <see cref="_nativeFolder"/> at the folder the just-loaded native libraries actually came
    /// from, when that differs from the preloaded folder.
    /// </summary>
    /// <remarks>
    /// Sdcb.FFmpeg resolves libav* through its own resolver, which does not necessarily use
    /// Sdcb.FFmpeg.dll's directory: in the editor the operator package is loaded from its build output
    /// while VideoServices runs from a shadow copy, and Sdcb's P/Invokes land in the build output. When
    /// both folders are preloaded into one process there are two libavformat copies: avdevice binds to the
    /// first loaded one (this assembly's), while Sdcb enumerates the other — file decoding works but
    /// dshow/v4l2 report as "not found in this FFmpeg build". The preload skips avformat/avdevice for
    /// exactly this reason, and this method makes the registration target the folder Sdcb uses.
    /// </remarks>
    private static void AdoptResolvedNativeFolder()
    {
        var utilName = OperatingSystem.IsWindows() ? "avutil-59.dll"
                       : OperatingSystem.IsMacOS() ? "libavutil.59.dylib"
                       : "libavutil.so.59";

        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var loadedFolders = process.Modules
                                     .Cast<System.Diagnostics.ProcessModule>()
                                     .Where(m => string.Equals(Path.GetFileName(m.FileName), utilName, StringComparison.OrdinalIgnoreCase))
                                     .Select(m => Path.GetDirectoryName(m.FileName))
                                     .Where(d => !string.IsNullOrEmpty(d))
                                     .ToList();

            // Prefer a copy that is not the one this assembly preloaded — that is the one Sdcb resolved.
            var resolved = loadedFolders.FirstOrDefault(d => !SameFolder(d!, _nativeFolder))
                           ?? loadedFolders.FirstOrDefault();

            if (!string.IsNullOrEmpty(resolved) && !SameFolder(resolved, _nativeFolder))
            {
                Log.Debug($"FFmpeg: native libraries resolved from '{resolved}' (preloaded from '{_nativeFolder ?? "none"}').");
                _nativeFolder = resolved;
            }
        }
        catch (Exception e)
        {
            // Module enumeration is a diagnostic aid, not a correctness requirement.
            Log.Debug("FFmpeg: could not inspect loaded native modules: " + e.Message);
        }
    }

    private static bool SameFolder(string? a, string? b)
        => a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The folders that may hold the FFmpeg libraries, best first: the directory of Sdcb.FFmpeg's own
    /// assembly, then flat next to this assembly, then NuGet's runtimes/&lt;rid&gt;/native for this
    /// platform's RIDs.
    /// </summary>
    /// <remarks>
    /// Sdcb's assembly directory is only a first guess. Its P/Invokes do not necessarily resolve there:
    /// in the editor the operator package is loaded from its build output while Sdcb.FFmpeg.dll itself
    /// runs from a shadow copy, and the native resolution follows the package folder. Whichever folder
    /// wins, <see cref="AdoptResolvedNativeFolder"/> corrects <see cref="_nativeFolder"/> after the first
    /// P/Invoke, so the device libraries are loaded and registered from the same place Sdcb queries.
    /// </remarks>
    private static IEnumerable<string> BundledLibraryFolders(string directory)
    {
        var sdcbDirectory = Path.GetDirectoryName(typeof(Sdcb.FFmpeg.Raw.ffmpeg).Assembly.Location);
        if (!string.IsNullOrEmpty(sdcbDirectory)
            && Directory.Exists(sdcbDirectory)
            && !string.Equals(Path.GetFullPath(sdcbDirectory), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
        {
            yield return sdcbDirectory;
        }

        yield return directory;

        var runtimesRoot = Path.Combine(directory, "runtimes");
        if (!Directory.Exists(runtimesRoot))
            yield break;

        var architecture = RuntimeInformation.ProcessArchitecture;
        string[] rids;
        if (OperatingSystem.IsWindows())
            rids = architecture == Architecture.Arm64 ? ["win-arm64"] : ["win-x64"];
        else if (OperatingSystem.IsMacOS())
            rids = architecture == Architecture.Arm64 ? ["osx-arm64"] : ["osx-x64"];
        else
            rids = architecture == Architecture.Arm64 ? ["linux-arm64"] : ["linux-x64"];

        foreach (var rid in rids)
        {
            var nativeDir = Path.Combine(runtimesRoot, rid, "native");
            if (Directory.Exists(nativeDir))
                yield return nativeDir;
        }
    }

    /// <summary>
    /// Finds a bundled FFmpeg library by exact file name in the folder the preload chose, or null when this
    /// installation carries none. It must be the same folder for every caller — see
    /// <see cref="BundledLibraryFolders"/> for why two copies must not be loaded.
    /// </summary>
    internal static string? FindBundledLibrary(string fileName)
    {
        if (_nativeFolder == null)
            return null;

        var path = Path.Combine(_nativeFolder, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// macOS has no LGPL FFmpeg package to ship yet (the DMG build compiles one via Installer/macOS/
    /// build-ffmpeg.sh; the DMG carries it next to the Video package, which the preload above picks up), so
    /// a development machine or CI uses an installed one: the folder in TIXL_FFMPEG_DIR, or Homebrew's
    /// ffmpeg@7 (FFmpeg 7.x = avcodec-61, which these bindings need; plain "ffmpeg" is already 8.x).
    /// Sdcb.FFmpeg asks for "libavutil.59.dylib" by name and finds nothing outside the standard paths, so
    /// the request falls through to this handler.
    /// </summary>
    /// <remarks>
    /// Loaded by the resolved path, never copied: Homebrew's libraries reference each other by absolute
    /// path, and a copy next to this assembly would put a second avutil into the process. Self-built dylibs
    /// reference the (temporary) build prefix by install name, so they must be preloaded in dependency
    /// order — dyld then matches each dependency against the already-loaded image instead of the dead path.
    /// Homebrew builds are GPL, so the license check below still refuses them unless
    /// TIXL_FFMPEG_ALLOW_RESTRICTED is set.
    /// </remarks>
    private static void UseInstalledLibrariesOnMac()
    {
        string[] candidates =
            [
                Environment.GetEnvironmentVariable("TIXL_FFMPEG_DIR") ?? string.Empty,
                "/opt/homebrew/opt/ffmpeg@7/lib",
                "/usr/local/opt/ffmpeg@7/lib",
            ];

        var folder = candidates.FirstOrDefault(c => c.Length > 0 && Directory.Exists(c));
        if (folder == null)
        {
            Log.Debug("FFmpeg: no libraries next to the Video package and none installed (brew install ffmpeg@7).");
            return;
        }

        var loaded = false;
        foreach (var path in OrderedFfmpegLibraries(folder))
            loaded |= NativeLibrary.TryLoad(path, out _);

        var bindings = AssemblyLoadContext.GetLoadContext(typeof(ffmpeg).Assembly) ?? AssemblyLoadContext.Default;
        bindings.ResolvingUnmanagedDll += (_, name) =>
                                          {
                                              var path = Path.Combine(folder, name);
                                              if (!File.Exists(path))
                                                  return IntPtr.Zero;

                                              var resolved = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
                                              return NativeLibrary.TryLoad(resolved, out var handle) ? handle : IntPtr.Zero;
                                          };

        Log.Debug($"FFmpeg: using the libraries installed in {folder} ({(loaded ? "preloaded" : "resolved on demand")}).");
    }

    /// <summary>The FFmpeg libraries in a folder, dependencies first (avutil → swresample → swscale →
    /// avcodec → avformat → avfilter → avdevice).</summary>
    private static IEnumerable<string> OrderedFfmpegLibraries(string folder)
    {
        static int LoadOrder(string fileName)
            => fileName.Contains("avutil") ? 0
               : fileName.Contains("swresample") ? 1
               : fileName.Contains("swscale") ? 2
               : fileName.Contains("avcodec") ? 3
               : fileName.Contains("avformat") ? 4
               : fileName.Contains("avdevice") ? 6
               : 5;

        return Directory.EnumerateFiles(folder)
                        .Where(IsFfmpegLibrary)
                        .OrderBy(path => LoadOrder(Path.GetFileName(path)));
    }

    /// <summary>
    /// Matches the libav* family under either naming scheme: "avcodec-61.dll" on Windows, "libavcodec.so.61"
    /// elsewhere - the version suffix rules out matching by extension.
    /// </summary>
    private static bool IsFfmpegLibrary(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("lib", StringComparison.Ordinal))
            name = name[3..];

        if (!name.StartsWith("av", StringComparison.Ordinal)
            && !name.StartsWith("sw", StringComparison.Ordinal)
            && !name.StartsWith("postproc", StringComparison.Ordinal))
        {
            return false;
        }

        return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".so", StringComparison.Ordinal)
               || name.EndsWith(".dylib", StringComparison.Ordinal);
    }

    private static readonly object _initLock = new();
    private static volatile bool _initialized;

    /// <summary>The one folder every native FFmpeg library is loaded from; set once by the preload.</summary>
    private static volatile string? _nativeFolder;

    private const long AppFieldNoticeIntervalMs = 10_000;
    private static long _lastAppFieldNoticeTick = -AppFieldNoticeIntervalMs;
}
