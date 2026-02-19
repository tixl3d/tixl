using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;

namespace Mediapipe.PInvoke;

internal static partial class SafeNativeMethods
{
    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_GlTexture__width(IntPtr glTexture);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern int mp_GlTexture__height(IntPtr glTexture);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern uint mp_GlTexture__target(IntPtr glTexture);

    [Pure]
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern uint mp_GlTexture__name(IntPtr glTexture);
}