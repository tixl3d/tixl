using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;

namespace Mediapipe.PInvoke;

internal static partial class SafeNativeMethods
{
    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mp_ImageFrame__IsEmpty(IntPtr imageFrame);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mp_ImageFrame__IsContiguous(IntPtr imageFrame);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_ImageFrame__IsAligned__ui(
        IntPtr imageFrame, uint alignmentBoundary, [MarshalAs(UnmanagedType.I1)] out bool value);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern ImageFormat.Types.Format mp_ImageFrame__Format(IntPtr imageFrame);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_ImageFrame__Width(IntPtr imageFrame);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_ImageFrame__Height(IntPtr imageFrame);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_ImageFrame__WidthStep(IntPtr imageFrame);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern IntPtr mp_ImageFrame__MutablePixelData(IntPtr imageFrame);
}