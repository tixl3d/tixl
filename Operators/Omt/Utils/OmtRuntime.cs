#nullable enable
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using libomtnet;

namespace Lib.Utils;

/// <summary>
/// Finds the native VMX codec (<c>libvmx</c>) that libomtnet compresses video with, and owns the process-wide OMT
/// discovery. The managed protocol is built with the solution; the codec ships per platform under
/// <c>runtimes/&lt;rid&gt;/native</c> and is loaded once here by full path, so libomtnet's <c>DllImport("libvmx")</c>
/// binds to that copy on every OS without depending on the library search path.
/// </summary>
internal static class OmtRuntime
{
    /// <summary>True where the codec loaded. Probed once; the result does not change while running.</summary>
    public static bool IsAvailable => _isAvailable.Value;

    /// <summary>What an operator reports while the codec is missing.</summary>
    public static string MissingMessage { get; } = BuildMissingMessage();

    /// <summary>
    /// Sources currently announced on the network, as "MACHINE (Source Name)". A snapshot refreshed by a background
    /// thread, so it is safe to call from UI code; empty until discovery has found something.
    /// </summary>
    public static IReadOnlyList<string> DiscoveredSources
    {
        get
        {
            EnsureDiscoveryStarted();
            return _discoveredSources;
        }
    }

    /// <summary>A hint for when nothing is discovered: where discovery depends on a system service, say which.</summary>
    public static string NoSourcesHint =>
        OperatingSystem.IsLinux()
            ? "No OMT sources discovered. Discovery needs avahi-daemon running; a source can also be entered as omt://host:port."
            : "No OMT sources discovered. A source can also be entered as omt://host:port.";

    /// <summary>
    /// Starts OMT's discovery on a background thread, once. Its first use binds the platform's DNS-SD service, which
    /// must not happen on the render or UI thread.
    /// </summary>
    public static void EnsureDiscoveryStarted()
    {
        if (!IsAvailable || Interlocked.Exchange(ref _discoveryStarted, 1) != 0)
            return;

        // A package reload unloads this assembly; a thread still looping in it would keep the old load context alive.
        var loadContext = AssemblyLoadContext.GetLoadContext(typeof(OmtRuntime).Assembly);
        if (loadContext != null)
            loadContext.Unloading += _ => _stopDiscovery = true;

        var thread = new Thread(DiscoveryThreadProc)
                         {
                             IsBackground = true,
                             Name = "OmtDiscovery",
                         };
        thread.Start();
    }

    private static void DiscoveryThreadProc()
    {
        OMTDiscovery? discovery = null;
        while (!_stopDiscovery)
        {
            try
            {
                discovery ??= OMTDiscovery.GetInstance();
                var addresses = discovery.GetAddresses() ?? [];
                Array.Sort(addresses, StringComparer.OrdinalIgnoreCase);
                if (!SameSources(addresses, _discoveredSources))
                    _discoveredSources = addresses;
            }
            catch (Exception e)
            {
                // Kept alive: a transient DNS-SD failure must not end discovery for the rest of the session.
                Log.Debug($"OMT discovery: {e.Message}");
            }

            Thread.Sleep(DiscoveryRefreshIntervalMs);
        }
    }

    private static bool SameSources(string[] a, string[] b)
    {
        if (a.Length != b.Length)
            return false;

        for (var index = 0; index < a.Length; index++)
        {
            if (!string.Equals(a[index], b[index], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool Probe()
    {
        RouteOmtLogToTixl();

        try
        {
            foreach (var candidate in Candidates())
            {
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out _))
                    return true;
            }

            // A system-wide installation is fine as well.
            if (NativeLibrary.TryLoad(FileName, out _))
                return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Probing the OMT video codec failed: {e.Message}");
        }

        Log.Warning(MissingMessage);
        return false;
    }

    /// <summary>
    /// OMT would otherwise start a log file of its own per process (in ~/.OMT/logs or %LOCALAPPDATA%\OMT\logs).
    /// Its lines go to TiXL's log at debug level instead, so connection problems show up where users look.
    /// </summary>
    private static void RouteOmtLogToTixl()
    {
        try
        {
            OMTLogging.SetFilename(null);
            OMTLogging.SetCallback(_omtLogCallback);
        }
        catch (Exception e)
        {
            Log.Debug($"Routing the OMT log failed: {e.Message}");
        }
    }

    private static void WriteOmtLogLine(string line)
    {
        try
        {
            Log.Debug("OMT " + line);
        }
        catch
        {
            // Runs on OMT's logging thread; nothing may escape from here.
        }
    }

    /// <summary>Beside the package assembly, then the NuGet runtimes layout, then the same below the app folder.</summary>
    private static IEnumerable<string> Candidates()
    {
        foreach (var root in SearchRoots())
        {
            yield return Path.Combine(root, FileName);

            foreach (var rid in RuntimeIdentifiers())
                yield return Path.Combine(root, "runtimes", rid, "native", FileName);
        }
    }

    private static IEnumerable<string> SearchRoots()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(OmtRuntime).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDirectory))
            yield return assemblyDirectory;

        if (!string.IsNullOrEmpty(AppContext.BaseDirectory) && AppContext.BaseDirectory != assemblyDirectory)
            yield return AppContext.BaseDirectory;
    }

    private static IEnumerable<string> RuntimeIdentifiers()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
                               {
                                   Architecture.Arm64 => "arm64",
                                   Architecture.X86   => "x86",
                                   _                  => "x64",
                               };

        if (OperatingSystem.IsWindows())
        {
            yield return $"win-{architecture}";
        }
        else if (OperatingSystem.IsMacOS())
        {
            // The macOS codec is a universal binary, published without an architecture.
            yield return "osx";
            yield return $"osx-{architecture}";
        }
        else
        {
            yield return $"linux-{architecture}";
        }
    }

    private static string BuildMissingMessage()
    {
        var platform = $"{RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}";
        return $"""
                OMT video codec ({FileName}) not found for {platform}.
                It ships in the Omt package's runtimes folder; reinstalling TiXL restores it. Other platforms can build it from github.com/openmediatransport/libvmx.
                """;
    }

    private static string FileName =>
        OperatingSystem.IsWindows() ? "libvmx.dll"
            : OperatingSystem.IsMacOS() ? "libvmx.dylib"
                : "libvmx.so";

    private const int DiscoveryRefreshIntervalMs = 1000;

    private static readonly OMTLogging.LogCallbackDelegate _omtLogCallback = WriteOmtLogLine;
    private static readonly Lazy<bool> _isAvailable = new(Probe);
    private static int _discoveryStarted;
    private static volatile bool _stopDiscovery;
    private static volatile string[] _discoveredSources = [];
}
