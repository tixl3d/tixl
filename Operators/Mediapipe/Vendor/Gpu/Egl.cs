using Mediapipe.PInvoke;

namespace Mediapipe.Gpu;

public class Egl
{
    public static nint GetCurrentContext()
    {
        return SafeNativeMethods.eglGetCurrentContext();
    }
}