using System.Runtime.InteropServices;
using Mediapipe.TranMarshal;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    #region Packet

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsMatrix(IntPtr packet, out IntPtr status);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetMpMatrix(IntPtr packet, out NativeMatrix matrix);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeColMajorMatrixPacket__Pf_i_i(float[] data, int rows, int cols,
        out IntPtr packet_out);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeColMajorMatrixPacket_At__Pf_i_i_ll(float[] data, int rows, int cols,
        long timestampMicrosec, out IntPtr packet_out);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_api_Matrix__delete(NativeMatrix matrix);

    #endregion
}