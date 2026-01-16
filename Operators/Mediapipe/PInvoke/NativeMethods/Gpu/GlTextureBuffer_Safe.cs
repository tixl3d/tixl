using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using Mediapipe.Gpu;

namespace Mediapipe.PInvoke;

internal static partial class SafeNativeMethods
{
    #region SharedGlTextureBuffer

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern IntPtr mp_SharedGlTextureBuffer__get(IntPtr glTextureBuffer);

    #endregion

    #region GlTextureBuffer

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern uint mp_GlTextureBuffer__name(IntPtr glTextureBuffer);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern uint mp_GlTextureBuffer__target(IntPtr glTextureBuffer);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_GlTextureBuffer__width(IntPtr glTextureBuffer);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_GlTextureBuffer__height(IntPtr glTextureBuffer);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern GpuBufferFormat mp_GlTextureBuffer__format(IntPtr glTextureBuffer);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern IntPtr mp_GlTextureBuffer__GetProducerContext(IntPtr glTextureBuffer);

    #endregion
}