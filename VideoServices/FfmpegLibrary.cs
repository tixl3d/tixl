using System.Runtime.InteropServices;
using System.Runtime.Loader;
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

        // The editor ships an LGPL shared build. A GPL/non-free build dropped in by a user would silently
        // break the project's MIT/LGPL distribution guarantee, so refuse to use it. Developers can opt in to
        // running against whatever build they have installed via the TIXL_FFMPEG_ALLOW_RESTRICTED env var.
        var isRestricted = configuration.Contains("--enable-gpl") || configuration.Contains("--enable-nonfree");
        var allowRestricted = AllowRestrictedBuildForTesting
                              || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TIXL_FFMPEG_ALLOW_RESTRICTED"));
        if (isRestricted && !allowRestricted)
        {
            StatusError = "Bundled FFmpeg build is GPL/non-free and not permitted — an LGPL build is required. "
                          + "For development, TIXL_FFMPEG_ALLOW_RESTRICTED=1 allows it.";
            Log.Warning(StatusError);
            return;
        }

        IsAvailable = true;
        ConfigureLogging();
        var licenseLabel = isRestricted ? "GPL/non-free — development build" : "LGPL";
        T3.Core.Resource.ThirdPartyRuntimeInfo.Register("FFmpeg", $"{VersionInfo} ({licenseLabel})");
        if (isRestricted)
            Log.Warning($"FFmpeg {VersionInfo} loaded but is a GPL/non-free build — for development/testing only, NOT for distribution.");
        else
            Log.Debug($"FFmpeg {VersionInfo} loaded (LGPL).");
    }

    // Route libav* logging through TiXL's logger instead of FFmpeg's default raw-stderr callback. Benign,
    // repetitive notices (e.g. swscale's "deprecated pixel format" warning when decoding full-range sources)
    // land at Debug so they don't flood the console; genuine errors stay visible at Warning.
    private static void ConfigureLogging()
    {
        FFmpegLogger.LogLevel = SdcbLogLevel.Warning; // drop FFmpeg's Info/Verbose chatter at the source
        // Some libav* messages are logged at error level but are harmless metadata notices that recur on every
        // file open — keep them out of the warning stream (they land at Debug). Substring match keeps it cheap.
        static bool IsBenignNotice(string text)
            => text.Contains("Referenced QT chapter track not found");

        FFmpegLogger.LogWriter = static (level, message) =>
                                 {
                                     var text = message?.TrimEnd();
                                     if (string.IsNullOrEmpty(text))
                                         return;

                                     if (level <= SdcbLogLevel.Error && !IsBenignNotice(text)) // Panic, Fatal, Error
                                         Log.Warning("FFmpeg: " + text);
                                     else
                                         Log.Debug("FFmpeg: " + text);
                                 };
    }

    // The FFmpeg libraries live next to this assembly, which is not on the OS library search path inside the
    // editor's custom operator load context. So a library's inter-dependencies (avcodec → avutil/swresample)
    // won't resolve by name. Pre-load each by full path, avutil first, so dependencies are already in the
    // process by the time something needs them - the loader then matches them by name and loads nothing twice.
    private static void PreloadNativeLibraries()
    {
        var directory = Path.GetDirectoryName(typeof(FfmpegLibrary).Assembly.Location);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        if (OperatingSystem.IsMacOS() && !Directory.EnumerateFiles(directory).Any(IsFfmpegLibrary))
            UseInstalledLibrariesOnMac();

        static int LoadOrder(string fileName)
            => fileName.Contains("avutil") ? 0
               : fileName.Contains("swresample") ? 1
               : fileName.Contains("swscale") ? 2
               : 3;

        var libraries = Directory.GetFiles(directory)
                                 .Where(IsFfmpegLibrary)
                                 .OrderBy(path => LoadOrder(Path.GetFileName(path)));

        foreach (var path in libraries)
            NativeLibrary.TryLoad(path, out _);
    }

    /// <summary>
    /// macOS has no LGPL FFmpeg package to ship yet, so a development machine uses an installed one: the folder
    /// in TIXL_FFMPEG_DIR, or Homebrew's ffmpeg@7 (FFmpeg 7.x = avcodec-61, which these bindings need; plain
    /// "ffmpeg" is already 8.x). Sdcb.FFmpeg asks for "libavutil.59.dylib" by name and finds nothing outside
    /// the standard paths, so the request falls through to this handler.
    /// </summary>
    /// <remarks>
    /// Loaded by the resolved Cellar path, never copied: Homebrew's libraries reference each other by absolute
    /// path, and a copy next to this assembly would put a second avutil into the process. Homebrew builds are
    /// GPL, so the license check below still refuses them unless TIXL_FFMPEG_ALLOW_RESTRICTED is set.
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

        var bindings = AssemblyLoadContext.GetLoadContext(typeof(ffmpeg).Assembly) ?? AssemblyLoadContext.Default;
        bindings.ResolvingUnmanagedDll += (_, name) =>
                                          {
                                              var path = Path.Combine(folder, name);
                                              if (!File.Exists(path))
                                                  return IntPtr.Zero;

                                              var resolved = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
                                              return NativeLibrary.TryLoad(resolved, out var handle) ? handle : IntPtr.Zero;
                                          };

        Log.Debug($"FFmpeg: using the libraries installed in {folder}.");
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
}
