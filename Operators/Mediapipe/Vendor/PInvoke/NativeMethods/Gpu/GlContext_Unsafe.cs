using System.Runtime.InteropServices;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    #region GlContext

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_SharedGlContext__delete(IntPtr sharedGlContext);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_SharedGlContext__reset(IntPtr sharedGlContext);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlContext_GetCurrent(out IntPtr sharedGlContext);

    #endregion

    #region GlSyncToken

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_GlSyncToken__delete(IntPtr glSyncToken);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_GlSyncToken__reset(IntPtr glSyncToken);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlSyncPoint__Wait(IntPtr glSyncPoint);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlSyncPoint__WaitOnGpu(IntPtr glSyncPoint);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlSyncPoint__IsReady(IntPtr glSyncPoint, out bool value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlSyncPoint__GetContext(IntPtr glSyncPoint, out IntPtr sharedGlContext);

    #endregion
}