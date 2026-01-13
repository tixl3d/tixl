using System.Runtime.InteropServices;
using Mediapipe.External;
using Mediapipe.TranMarshal;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    #region common

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__(out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_Packet__delete(IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__At__Rt(IntPtr packet, IntPtr timestamp, out IntPtr newPacket);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__Timestamp(IntPtr packet, out IntPtr timestamp);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__DebugString(IntPtr packet, out IntPtr str);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__RegisteredTypeName(IntPtr packet, out IntPtr str);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__DebugTypeName(IntPtr packet, out IntPtr str);

    #endregion

    #region Bool

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode
        mp__MakeBoolPacket__b([MarshalAs(UnmanagedType.I1)] bool value, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeBoolPacket_At__b_Rt([MarshalAs(UnmanagedType.I1)] bool value,
        IntPtr timestamp, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeBoolPacket_At__b_ll([MarshalAs(UnmanagedType.I1)] bool value,
        long timestampMicrosec, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetBool(IntPtr packet, [MarshalAs(UnmanagedType.I1)] out bool value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsBool(IntPtr packet, out IntPtr status);

    #endregion

    #region BoolVector

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeBoolVectorPacket__Pb_i(bool[] value, int size, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeBoolVectorPacket_At__Pb_i_ll(bool[] value, int size,
        long timestampMicrosec, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetBoolVector(IntPtr packet, out StructArray<bool> value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsBoolVector(IntPtr packet, out IntPtr status);

    #endregion

    #region Double

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeDoublePacket__d(double value, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeDoublePacket_At__d_ll(double value, long timestampMicrosec,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetDouble(IntPtr packet, out double value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsDouble(IntPtr packet, out IntPtr status);

    #endregion

    #region Float

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeFloatPacket__f(float value, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeFloatPacket_At__f_Rt(float value, IntPtr timestamp, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeFloatPacket_At__f_ll(float value, long timestampMicrosec,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetFloat(IntPtr packet, out float value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsFloat(IntPtr packet, out IntPtr status);

    #endregion

    #region FloatArray

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeFloatArrayPacket__Pf_i(float[] value, int size, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeFloatArrayPacket_At__Pf_i_Rt(float[] value, int size, IntPtr timestamp,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeFloatArrayPacket_At__Pf_i_ll(float[] value, int size,
        long timestampMicrosec, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetFloatArray_i(IntPtr packet, int size, out IntPtr value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsFloatArray(IntPtr packet, out IntPtr status);

    #endregion

    #region FloatVector

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeFloatVectorPacket__Pf_i(float[] value, int size, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeFloatVectorPacket_At__Pf_i_Rt(float[] value, int size, IntPtr timestamp,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeFloatVectorPacket_At__Pf_i_ll(float[] value, int size,
        long timestampMicrosec, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetFloatVector(IntPtr packet, out StructArray<float> value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsFloatVector(IntPtr packet, out IntPtr status);

    #endregion

    #region Int

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeIntPacket__i(int value, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeIntPacket_At__i_Rt(int value, IntPtr timestamp, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeIntPacket_At__i_ll(int value, long timestampMicrosec, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetInt(IntPtr packet, out int value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsInt(IntPtr packet, out IntPtr status);

    #endregion

    #region String

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeStringPacket__PKc(string value, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode
        mp__MakeStringPacket_At__PKc_Rt(string value, IntPtr timestamp, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeStringPacket_At__PKc_ll(string value, long timestampMicrosec,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeStringPacket__PKc_i(byte[] bytes, int size, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeStringPacket_At__PKc_i_Rt(byte[] bytes, int size, IntPtr timestamp,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeStringPacket_At__PKc_i_ll(byte[] bytes, int size, long timestampMicrosec,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetString(IntPtr packet, out IntPtr value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetByteString(IntPtr packet, out IntPtr value, out int size);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ConsumeString(IntPtr packet, out IntPtr status, out IntPtr value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ConsumeByteString(IntPtr packet, out IntPtr status, out IntPtr value,
        out int size);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsString(IntPtr packet, out IntPtr status);

    #endregion

    #region Proto

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern unsafe MpReturnCode mp__PacketFromDynamicProto__PKc_PKc_i(string typeName, byte* proto,
        int size,
        out IntPtr status, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern unsafe MpReturnCode mp__PacketFromDynamicProto_At__PKc_PKc_i_ll(string typeName, byte* proto,
        int size, long timestampMicrosec,
        out IntPtr status, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetProtoMessageLite(IntPtr packet, out SerializedProto value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetVectorOfProtoMessageLite(IntPtr packet,
        out SerializedProtoVector value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsProtoMessageLite(IntPtr packet, out IntPtr status);

    #endregion

    #region PacketMap

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_PacketMap__(out IntPtr packetMap);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_PacketMap__delete(IntPtr packetMap);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_PacketMap__emplace__PKc_Rp(IntPtr packetMap, string key, IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_PacketMap__find__PKc(IntPtr packetMap, string key, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_PacketMap__erase__PKc(IntPtr packetMap, string key, out int count);

    #endregion
}