#nullable enable
using System.Runtime.InteropServices;

namespace Lib.io.video;

/// <summary>
/// macOS camera permission (TCC) status, read through the ObjC runtime so a denial can be reported as such
/// instead of surfacing as a mysterious "no capture devices found" / failed open: without camera access,
/// AVFoundation simply hides every device from the process. <c>authorizationStatusForMediaType:</c> is a
/// pure status query — unlike an open, it never shows a permission prompt.
/// Only meaningful on macOS; <see cref="TryGetVideoAuthorizationStatus"/> fails gracefully everywhere else.
/// </summary>
internal static class MacCameraPermission
{
    public enum AuthorizationStatus
    {
        NotDetermined = 0,
        Restricted = 1,
        Denied = 2,
        Authorized = 3,
    }

    public static bool IsGrantedOrUnknown()
    {
        return !OperatingSystem.IsMacOS()
               || !TryGetVideoAuthorizationStatus(out var status)
               || status is AuthorizationStatus.Authorized or AuthorizationStatus.NotDetermined;
    }

    /// <summary>
    /// Whether the process may access cameras. Returns false when the query itself is unavailable (not
    /// macOS, framework not loadable) — callers should then keep their generic messaging.
    /// </summary>
    public static bool TryGetVideoAuthorizationStatus(out AuthorizationStatus status)
    {
        status = AuthorizationStatus.NotDetermined;
        if (!OperatingSystem.IsMacOS())
            return false;

        try
        {
            // FFmpeg's avfoundation demuxer already pulls AVFoundation into the process, making this a
            // no-op; a run without FFmpeg still finds the framework at its absolute path.
            if (!NativeLibrary.TryLoad("/System/Library/Frameworks/AVFoundation.framework/AVFoundation", out _))
                return false;

            var deviceClass = objc_getClass("AVCaptureDevice");
            if (deviceClass == IntPtr.Zero)
                return false;

            // AVMediaTypeVideo is the toll-free-bridged CFString "vide".
            var mediaType = CFStringCreateWithCString(IntPtr.Zero, "vide", KcfStringEncodingUtf8);
            if (mediaType == IntPtr.Zero)
                return false;

            try
            {
                var selector = sel_registerName("authorizationStatusForMediaType:");
                // Class method: the receiver is the class object itself. Returns an NSInteger.
                status = (AuthorizationStatus)(long)objc_msgSend(deviceClass, selector, mediaType);
                return true;
            }
            finally
            {
                CFRelease(mediaType);
            }
        }
        catch
        {
            status = AuthorizationStatus.NotDetermined;
            return false;
        }
    }

    private const int KcfStringEncodingUtf8 = 0x08000100;

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    // objc_msgSend for a call with one object argument and an integer return.
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string cStr, int encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr handle);
}
