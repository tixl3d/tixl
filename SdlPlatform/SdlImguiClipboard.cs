using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>
/// Routes ImGui's copy and paste through SDL. ImGui only knows the Win32 clipboard; elsewhere its copies would
/// stay inside the application.
/// </summary>
public static unsafe class SdlImguiClipboard
{
    /// <summary>Installs the handlers into the current ImGui context. Call once per context, with it current.</summary>
    public static void Install()
    {
        var platformIo = ImGui.GetPlatformIO();
        platformIo.Platform_GetClipboardTextFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)&GetClipboardText;
        platformIo.Platform_SetClipboardTextFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&SetClipboardText;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr GetClipboardText(IntPtr context)
    {
        // ImGui reads the returned text until the next call, so it is kept in a buffer this class owns.
        if (_clipboardText != IntPtr.Zero)
            Marshal.FreeCoTaskMem(_clipboardText);

        _clipboardText = Marshal.StringToCoTaskMemUTF8(SDL_GetClipboardText() ?? string.Empty);
        return _clipboardText;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetClipboardText(IntPtr context, IntPtr text)
    {
        var managed = Marshal.PtrToStringUTF8(text);
        if (managed != null)
            SDL_SetClipboardText(managed);
    }

    private static IntPtr _clipboardText;
}
