using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;

namespace Mediapipe.PInvoke;

internal static partial class SafeNativeMethods
{
    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mp_CalculatorGraph__HasError(IntPtr graph);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mp_CalculatorGraph__HasInputStream__PKc(IntPtr graph, string name);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mp_CalculatorGraph__GraphInputStreamsClosed(IntPtr graph);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mp_CalculatorGraph__IsNodeThrottled__i(IntPtr graph, int nodeId);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mp_CalculatorGraph__UnthrottleSources(IntPtr graph);
}