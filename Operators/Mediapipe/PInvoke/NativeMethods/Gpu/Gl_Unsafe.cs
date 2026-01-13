using System.Runtime.InteropServices;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary)]
    public static extern void glFlush();

    [DllImport(LibName.MediaPipeLibrary)]
    public static extern void glReadPixels(int x, int y, int width, int height, uint glFormat, uint glType,
        IntPtr pixels);
}