using System.Runtime.InteropServices;
using Mediapipe.Gpu;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    #region GlTextureBuffer

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlTextureBuffer__WaitUntilComplete(IntPtr glTextureBuffer);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlTextureBuffer__WaitOnGpu(IntPtr glTextureBuffer);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlTextureBuffer__Reuse(IntPtr glTextureBuffer);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlTextureBuffer__Updated__Pgst(IntPtr glTextureBuffer, IntPtr prodToken);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlTextureBuffer__DidRead__Pgst(IntPtr glTextureBuffer, IntPtr consToken);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlTextureBuffer__WaitForConsumers(IntPtr glTextureBuffer);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GlTextureBuffer__WaitForConsumersOnGpu(IntPtr glTextureBuffer);

    #endregion

    #region SharedGlTextureBuffer

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_SharedGlTextureBuffer__delete(IntPtr glTextureBuffer);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_SharedGlTextureBuffer__reset(IntPtr glTextureBuffer);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_SharedGlTextureBuffer__ui_ui_i_i_ui_PF_PSgc(
        uint target, uint name, int width, int height, GpuBufferFormat format,
        [MarshalAs(UnmanagedType.FunctionPtr)] GlTextureBuffer.DeletionCallback deletionCallback,
        IntPtr producerContext, out IntPtr sharedGlTextureBuffer);

    #endregion
}