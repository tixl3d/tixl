using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using Mediapipe.Gpu;

namespace Mediapipe.PInvoke;

internal static partial class SafeNativeMethods
{
    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_GpuBuffer__width(IntPtr gpuBuffer);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_GpuBuffer__height(IntPtr gpuBuffer);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern GpuBufferFormat mp_GpuBuffer__format(IntPtr gpuBuffer);
}