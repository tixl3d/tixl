using System.Runtime.InteropServices;
using Mediapipe.External;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeRectPacket__PKc_i(byte[] serializedData, int size, out IntPtr packet_out);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeRectPacket_At__PKc_i_Rt(byte[] serializedData, int size, IntPtr timestamp,
        out IntPtr packet_out);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetRect(IntPtr packet, out SerializedProto serializedProto);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetRectVector(IntPtr packet,
        out SerializedProtoVector serializedProtoVector);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsRect(IntPtr packet, out IntPtr status);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeNormalizedRectPacket__PKc_i(byte[] serializedData, int size,
        out IntPtr packet_out);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeNormalizedRectPacket_At__PKc_i_Rt(byte[] serializedData, int size,
        IntPtr timestamp, out IntPtr packet_out);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetNormalizedRect(IntPtr packet, out SerializedProto serializedProto);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetNormalizedRectVector(IntPtr packet,
        out SerializedProtoVector serializedProtoVector);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsNormalizedRect(IntPtr packet, out IntPtr status);
}