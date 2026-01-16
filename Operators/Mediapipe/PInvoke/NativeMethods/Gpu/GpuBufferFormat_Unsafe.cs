using System.Runtime.InteropServices;
using Mediapipe.Gpu;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__GlTextureInfoForGpuBufferFormat__ui_i_ui(
        GpuBufferFormat format, int plane, GlVersion glVersion, out GlTextureInfo glTextureInfo);
}