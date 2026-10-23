using System;
using System.Runtime.InteropServices;

namespace T3.Editor.UiContentDrawing;

/// <summary>
/// Zero-geometry ImGui draw commands that switch the sampler used by the UI renderer for the commands
/// that follow. <see cref="WindowsUiContentDrawer"/> recognizes the two pointers and binds the point or
/// linear sampler accordingly; the native callbacks themselves are no-ops so a backend that dispatches
/// them (e.g. the OpenGL one) stays safe.
/// </summary>
internal static class ImGuiSamplerCallbacks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DrawCallback(IntPtr parentList, IntPtr cmd);

    private static readonly DrawCallback PointFilteringOnDelegate = OnPointFiltering;
    private static readonly DrawCallback PointFilteringOffDelegate = OffPointFiltering;

    /// <summary>Switches subsequent draw commands to point sampling.</summary>
    public static readonly IntPtr PointFilteringOn = Marshal.GetFunctionPointerForDelegate(PointFilteringOnDelegate);

    /// <summary>Switches subsequent draw commands back to the default linear sampling.</summary>
    public static readonly IntPtr PointFilteringOff = Marshal.GetFunctionPointerForDelegate(PointFilteringOffDelegate);

    private static void OnPointFiltering(IntPtr parentList, IntPtr cmd)
    {
    }

    private static void OffPointFiltering(IntPtr parentList, IntPtr cmd)
    {
    }
}
