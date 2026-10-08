using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>
/// Colours the native macOS title bar. SDL has no setting for it, so this talks to the window's NSWindow through
/// the Objective-C runtime: a transparent title bar shows the window's background colour, which lets it blend
/// into the app's own menu bar below. Does nothing on other systems.
/// </summary>
public static unsafe class SdlMacTitleBar
{
    public static void TrySetColor(SDL_Window* window, float r, float g, float b)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var nsWindow = SDL_GetPointerProperty(SDL_GetWindowProperties(window), SDL_PROP_WINDOW_COCOA_WINDOW_POINTER, IntPtr.Zero);
        if (nsWindow == IntPtr.Zero)
            return;

        var color = MsgSendColor(objc_getClass("NSColor"), sel_registerName("colorWithSRGBRed:green:blue:alpha:"), r, g, b, 1);
        if (color == IntPtr.Zero)
            return;

        MsgSendBool(nsWindow, sel_registerName("setTitlebarAppearsTransparent:"), true);
        MsgSendId(nsWindow, sel_registerName("setBackgroundColor:"), color);
    }

    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjCLibrary)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjCLibrary)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSendColor(IntPtr receiver, IntPtr selector, double r, double g, double b, double a);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void MsgSendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void MsgSendId(IntPtr receiver, IntPtr selector, IntPtr value);
}
