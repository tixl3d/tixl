using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using Mediapipe.Gpu;

namespace Mediapipe.PInvoke;

internal static partial class SafeNativeMethods
{
    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_Image__width(IntPtr image);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_Image__height(IntPtr image);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_Image__channels(IntPtr image);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_Image__step(IntPtr image);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mp_Image__UsesGpu(IntPtr image);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern ImageFormat.Types.Format mp_Image__image_format(IntPtr image);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern GpuBufferFormat mp_Image__format(IntPtr image);

    #region PixelWriteLock

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern IntPtr mp_PixelWriteLock__Pixels(IntPtr pixelWriteLock);

    #endregion
}