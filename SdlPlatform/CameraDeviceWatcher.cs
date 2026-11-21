using System.Runtime.InteropServices;
using System.Text;

namespace T3.SdlPlatform;

/// <summary>
/// Tells whether the set of connected cameras changed, on platforms where SDL lists cameras only once at
/// startup (Media Foundation on Windows, AVFoundation on macOS — SDL has no hotplug there). Linux needs
/// nothing: SDL follows udev itself.
/// </summary>
internal static class CameraDeviceWatcher
{
    /// <summary>True on platforms where <see cref="ReadSignature"/> is meaningful.</summary>
    public static bool IsNeeded => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>
    /// A string that changes whenever a camera is connected or disconnected; null if it can't be read.
    /// Cheap (no device is opened), but allocates — call it at most about once a second.
    /// </summary>
    public static string? ReadSignature()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return ReadWindowsSignature();

            if (OperatingSystem.IsMacOS())
                return ReadMacSignature();
        }
        catch (Exception e)
        {
            T3.Core.Logging.Log.Debug($"Could not read the connected cameras: {e.Message}");
        }

        return null;
    }

    #region Windows
    /** The device-interface categories Media Foundation enumerates: webcams register the first, capture cards the second. */
    private static readonly Guid[] _windowsCameraCategories =
        [
            new("E5323777-F976-4F5B-9B55-B94699C46E44"), // KSCATEGORY_VIDEO_CAMERA
            new("65E8773D-8F56-11D0-A3B9-00A0C9223196"), // KSCATEGORY_CAPTURE
        ];

    private static string? ReadWindowsSignature()
    {
        // Sorted, because the interface-list order is not guaranteed to be stable between calls. Treating
        // a reordered list as a change restarts camera support, which closes the cameras that are running.
        var interfaces = new List<string>();
        foreach (var category in _windowsCameraCategories)
        {
            var categoryId = category;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (CM_Get_Device_Interface_List_SizeW(out var length, ref categoryId, null, CmGetDeviceInterfaceListPresent) != CrSuccess)
                    return null;

                var buffer = new char[length];
                var result = CM_Get_Device_Interface_ListW(ref categoryId, null, buffer, length, CmGetDeviceInterfaceListPresent);
                if (result == CrBufferSmall)
                    continue; // A device arrived between the two calls.

                if (result != CrSuccess)
                    return null;

                interfaces.AddRange(new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries));
                break;
            }
        }

        interfaces.Sort(StringComparer.Ordinal);
        return string.Join('\n', interfaces);
    }

    private const int CrSuccess = 0;
    private const int CrBufferSmall = 0x1A;
    private const uint CmGetDeviceInterfaceListPresent = 0;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string? deviceId, char[] buffer, uint bufferLength,
                                                            uint flags);
    #endregion

    #region macOS
    private static string? ReadMacSignature()
    {
        if (!_macInitialized)
        {
            _macInitialized = true;
            if (NativeLibrary.TryLoad("/System/Library/Frameworks/AVFoundation.framework/AVFoundation", out _))
            {
                _captureDeviceClass = objc_getClass("AVCaptureDevice");
                _selDevices = sel_registerName("devices");
                _selCount = sel_registerName("count");
                _selObjectAtIndex = sel_registerName("objectAtIndex:");
                _selUniqueId = sel_registerName("uniqueID");
            }
        }

        if (_captureDeviceClass == IntPtr.Zero)
            return null;

        var ids = new List<string>();
        var pool = objc_autoreleasePoolPush();
        try
        {
            var devices = objc_msgSend(_captureDeviceClass, _selDevices);
            if (devices == IntPtr.Zero)
                return string.Empty;

            var count = objc_msgSend_long(devices, _selCount);
            var buffer = new byte[256];
            for (var index = 0L; index < count; index++)
            {
                var device = objc_msgSend_long_arg(devices, _selObjectAtIndex, index);
                var uniqueId = device == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(device, _selUniqueId);
                if (uniqueId != IntPtr.Zero && CFStringGetCString(uniqueId, buffer, buffer.Length, CfStringEncodingUtf8) != 0)
                {
                    var length = Array.IndexOf(buffer, (byte)0);
                    ids.Add(Encoding.UTF8.GetString(buffer, 0, length >= 0 ? length : buffer.Length));
                }
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }

        ids.Sort(StringComparer.Ordinal);
        return string.Join('\n', ids);
    }

    private const string LibObjc = "/usr/lib/libobjc.A.dylib";
    private const string LibCoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int CfStringEncodingUtf8 = 0x08000100;

    private static bool _macInitialized;
    private static IntPtr _captureDeviceClass;
    private static IntPtr _selDevices;
    private static IntPtr _selCount;
    private static IntPtr _selObjectAtIndex;
    private static IntPtr _selUniqueId;

    [DllImport(LibObjc)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjc)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern long objc_msgSend_long(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_long_arg(IntPtr receiver, IntPtr selector, long argument);

    [DllImport(LibObjc)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(LibObjc)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(LibCoreFoundation)]
    private static extern byte CFStringGetCString(IntPtr cfString, byte[] buffer, nint bufferSize, int encoding);
    #endregion
}
