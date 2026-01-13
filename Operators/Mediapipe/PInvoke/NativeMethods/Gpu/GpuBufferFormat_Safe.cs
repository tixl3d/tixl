using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using Mediapipe.Gpu;

namespace Mediapipe.PInvoke;

internal static partial class SafeNativeMethods
{
    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern ImageFormat.Types.Format mp__ImageFormatForGpuBufferFormat__ui(GpuBufferFormat format);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern ImageFormat.Types.Format
        mp__GpuBufferFormatForImageFormat__ui(ImageFormat.Types.Format format);
}