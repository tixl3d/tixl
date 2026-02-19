using System.Runtime.InteropServices;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlTexture__(out IntPtr glTexture);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_GlTexture__delete(IntPtr glTexture);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlTexture__Release(IntPtr glTexture);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlTexture__GetGpuBufferFrame(IntPtr glTexture, out IntPtr gpuBuffer);
}