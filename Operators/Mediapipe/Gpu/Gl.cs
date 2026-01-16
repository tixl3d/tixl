using Mediapipe.PInvoke;

namespace Mediapipe.Gpu;

public class Gl
{
    public const uint GL_TEXTURE_2D = 0x0DE1;

    public static void Flush()
    {
        UnsafeNativeMethods.glFlush();
    }

    public static void ReadPixels(int x, int y, int width, int height, uint glFormat, uint glType, nint pixels)
    {
        UnsafeNativeMethods.glReadPixels(x, y, width, height, glFormat, glType, pixels);
    }
}