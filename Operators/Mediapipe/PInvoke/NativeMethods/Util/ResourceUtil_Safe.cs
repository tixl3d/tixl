using System.Runtime.InteropServices;
using Mediapipe.Utils;

namespace Mediapipe.PInvoke;

internal static partial class SafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp__SetCustomGlobalResourceProvider__P(
        [MarshalAs(UnmanagedType.FunctionPtr)] ResourceUtil.NativeResourceProvider provider);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp__SetCustomGlobalPathResolver__P(
        [MarshalAs(UnmanagedType.FunctionPtr)] ResourceUtil.PathResolver resolver);
}