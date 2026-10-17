#nullable enable
using System.Reflection;
using System.Runtime.InteropServices;
using NewTek;

namespace Lib.Utils;

/// <summary>
/// Locates the native NDI runtime for the managed binding, which only knows the Windows library name, and tells the
/// operators whether it is there. Windows builds ship the runtime. On Linux and macOS it is installed separately and
/// found through the NDI SDK's <c>NDI_RUNTIME_DIR_V6</c> convention or on the system library path.
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

        return TryLoadNative(out var handle) ? handle : IntPtr.Zero;
    }

    private static bool TryLoadNative(out IntPtr handle)
    {
        foreach (var candidate in _candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out handle))
                return true;
        }

        handle = IntPtr.Zero;
        return false;
    }

    private static string[] BuildCandidates()
    {
        var fileName = OperatingSystem.IsMacOS() ? MacLibraryName : LinuxLibraryName;
        var candidates = new List<string>();

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
            return $"NDI runtime not found. {WindowsLibraryName} should sit next to the editor; reinstalling TiXL restores it.";

        if (OperatingSystem.IsMacOS())
            return $"NDI runtime not found. Install NDI Tools or the NDI SDK from ndi.video, or set {RuntimeDirVariable} to the folder containing {MacLibraryName}.";

        return $"NDI runtime not found. Install it (Arch: ndi-sdk from the AUR; other distributions: the NDI SDK from ndi.video), or set {RuntimeDirVariable} to the folder containing {LinuxLibraryName}.";
    }

    private const string WindowsLibraryPrefix = "Processing.NDI.Lib";
    private const string WindowsLibraryName = "Processing.NDI.Lib.x64.dll";
    private const string LinuxLibraryName = "libndi.so.6";
    private const string MacLibraryName = "libndi.dylib";

    /** Set by the NDI installers, and the SDK's documented way for applications to find the runtime. */
    private const string RuntimeDirVariable = "NDI_RUNTIME_DIR_V6";

    private static readonly string[] _candidates = BuildCandidates();
    private static bool? _isAvailable;
}
