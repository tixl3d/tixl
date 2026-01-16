using Google.Protobuf;
using Mediapipe.Core;
using Mediapipe.Framework.Formats;
using Mediapipe.Framework.Port;
using Mediapipe.Gpu;
using Mediapipe.PInvoke;

namespace Mediapipe.Framework;

public static class PacketHelper
{
    /// <summary>
    ///     Create a bool Packet.
    /// </summary>
    public static Packet<bool> CreateBool(bool value)
    {
        UnsafeNativeMethods.mp__MakeBoolPacket__b(value, out IntPtr ptr).Assert();

        return new Packet<bool>(ptr, true);
    }

    /// <summary>
    ///     Create a bool Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<bool> CreateBoolAt(bool value, long timestampMicrosec)
    {
        UnsafeNativeMethods.mp__MakeBoolPacket_At__b_ll(value, timestampMicrosec, out IntPtr ptr).Assert();

        return new Packet<bool>(ptr, true);
    }

    /// <summary>
    ///     Create a bool vector Packet.
    /// </summary>
    public static Packet<List<bool>> CreateBoolVector(bool[] value)
    {
        UnsafeNativeMethods.mp__MakeBoolVectorPacket__Pb_i(value, value.Length, out IntPtr ptr).Assert();

        return new Packet<List<bool>>(ptr, true);
    }

    /// <summary>
    ///     Create a bool vector Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<List<bool>> CreateBoolVectorAt(bool[] value, long timestampMicrosec)
    {
        UnsafeNativeMethods.mp__MakeBoolVectorPacket_At__Pb_i_ll(value, value.Length, timestampMicrosec, out IntPtr ptr)
            .Assert();

        return new Packet<List<bool>>(ptr, true);
    }

    /// <summary>
    ///     Create a double Packet.
    /// </summary>
    public static Packet<double> CreateDouble(double value)
    {
        UnsafeNativeMethods.mp__MakeDoublePacket__d(value, out IntPtr ptr).Assert();

        return new Packet<double>(ptr, true);
    }

    /// <summary>
    ///     Create a double Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<double> CreateDoubleAt(double value, long timestampMicrosec)
    {
        UnsafeNativeMethods.mp__MakeDoublePacket_At__d_ll(value, timestampMicrosec, out IntPtr ptr).Assert();

        return new Packet<double>(ptr, true);
    }

    /// <summary>
    ///     Create a float Packet.
    /// </summary>
    public static Packet<float> CreateFloat(float value)
    {
        UnsafeNativeMethods.mp__MakeFloatPacket__f(value, out IntPtr ptr).Assert();

        return new Packet<float>(ptr, true);
    }

    /// <summary>
    ///     Create a float Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<float> CreateFloatAt(float value, long timestampMicrosec)
    {
        UnsafeNativeMethods.mp__MakeFloatPacket_At__f_ll(value, timestampMicrosec, out IntPtr ptr).Assert();

        return new Packet<float>(ptr, true);
    }

    /// <summary>
    ///     Create a float array Packet.
    /// </summary>
    public static Packet<float[]> CreateFloatArray(float[] value)
    {
        UnsafeNativeMethods.mp__MakeFloatArrayPacket__Pf_i(value, value.Length, out IntPtr ptr).Assert();

        return new Packet<float[]>(ptr, true);
    }

    /// <summary>
    ///     Create a float array Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<float[]> CreateFloatArrayAt(float[] value, long timestampMicrosec)
    {
        UnsafeNativeMethods.mp__MakeFloatArrayPacket_At__Pf_i_ll(value, value.Length, timestampMicrosec, out IntPtr ptr)
            .Assert();

        return new Packet<float[]>(ptr, true);
    }

    /// <summary>
    ///     Create a float vector Packet.
    /// </summary>
    public static Packet<List<float>> CreateFloatVector(float[] value)
    {
        UnsafeNativeMethods.mp__MakeFloatVectorPacket__Pf_i(value, value.Length, out IntPtr ptr).Assert();

        return new Packet<List<float>>(ptr, true);
    }

    /// <summary>
    ///     Create a float vector Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<List<float>> CreateFloatVectorAt(float[] value, long timestampMicrosec)
    {
        UnsafeNativeMethods
            .mp__MakeFloatVectorPacket_At__Pf_i_ll(value, value.Length, timestampMicrosec, out IntPtr ptr).Assert();

        return new Packet<List<float>>(ptr, true);
    }

    /// <summary>
    ///     Create an <see cref="GpuBuffer" /> Packet.
    /// </summary>
    public static Packet<GpuBuffer> CreateGpuBuffer(GpuBuffer value)
    {
        UnsafeNativeMethods.mp__MakeGpuBufferPacket__Rgb(value.MpPtr, out IntPtr ptr).Assert();
        value.Dispose(); // respect move semantics

        return new Packet<GpuBuffer>(ptr, true);
    }

    /// <summary>
    ///     Create an <see cref="GpuBuffer"> Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<GpuBuffer> CreateGpuBufferAt(GpuBuffer value, long timestampMicrosec)
    {
        UnsafeNativeMethods.mp__MakeGpuBufferPacket_At__Rgb_ll(value.MpPtr, timestampMicrosec, out IntPtr ptr).Assert();
        value.Dispose(); // respect move semantics

        return new Packet<GpuBuffer>(ptr, true);
    }

    /// <summary>
    ///     Create an <see cref="Image" /> Packet.
    /// </summary>
    public static Packet<Image> CreateImage(Image value)
    {
        UnsafeNativeMethods.mp__MakeImagePacket__PI(value.MpPtr, out IntPtr ptr).Assert();
        value.Dispose(); // respect move semantics

        return new Packet<Image>(ptr, true);
    }

    /// <summary>
    ///     Create an <see cref="Image"> Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<Image> CreateImageAt(Image value, long timestampMicrosec)
    {
        UnsafeNativeMethods.mp__MakeImagePacket_At__PI_ll(value.MpPtr, timestampMicrosec, out IntPtr ptr).Assert();
        value.Dispose(); // respect move semantics

        return new Packet<Image>(ptr, true);
    }

    /// <summary>
    ///     Create an <see cref="ImageFrame" /> Packet.
    /// </summary>
    public static Packet<ImageFrame> CreateImageFrame(ImageFrame value)
    {
        UnsafeNativeMethods.mp__MakeImageFramePacket__Pif(value.MpPtr, out IntPtr ptr).Assert();
        value.Dispose(); // respect move semantics

        return new Packet<ImageFrame>(ptr, true);
    }

    /// <summary>
    ///     Create an <see cref="ImageFrame" /> Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<ImageFrame> CreateImageFrameAt(ImageFrame value, long timestampMicrosec)
    {
        UnsafeNativeMethods.mp__MakeImageFramePacket_At__Pif_ll(value.MpPtr, timestampMicrosec, out IntPtr ptr)
            .Assert();
        value.Dispose(); // respect move semantics

        return new Packet<ImageFrame>(ptr, true);
    }

    /// <summary>
    ///     Create an int Packet.
    /// </summary>
    public static Packet<int> CreateInt(int value)
    {
        UnsafeNativeMethods.mp__MakeIntPacket__i(value, out IntPtr ptr).Assert();

        return new Packet<int>(ptr, true);
    }

    /// <summary>
    ///     Create a int Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<int> CreateIntAt(int value, long timestampMicrosec)
    {
        UnsafeNativeMethods.mp__MakeIntPacket_At__i_ll(value, timestampMicrosec, out IntPtr ptr).Assert();

        return new Packet<int>(ptr, true);
    }

    /// <summary>
    ///     Create a Matrix Packet.
    /// </summary>
    public static Packet<Matrix> CreateColMajorMatrix(float[] data, int row, int col)
    {
        UnsafeNativeMethods.mp__MakeColMajorMatrixPacket__Pf_i_i(data, row, col, out IntPtr ptr).Assert();

        return new Packet<Matrix>(ptr, true);
    }

    /// <summary>
    ///     Create a Matrix Packet.
    /// </summary>
    public static Packet<Matrix> CreateColMajorMatrix(Matrix value)
    {
        if (!value.IsColMajor) throw new ArgumentException("Matrix must be col-major");
        return CreateColMajorMatrix(value.data, value.rows, value.cols);
    }

    /// <summary>
    ///     Create a Matrix Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<Matrix> CreateColMajorMatrixAt(float[] data, int row, int col, long timestampMicrosec)
    {
        UnsafeNativeMethods
            .mp__MakeColMajorMatrixPacket_At__Pf_i_i_ll(data, row, col, timestampMicrosec, out IntPtr ptr).Assert();

        return new Packet<Matrix>(ptr, true);
    }

    /// <summary>
    ///     Create a Matrix Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<Matrix> CreateColMajorMatrixAt(Matrix value, long timestampMicrosec)
    {
        if (!value.IsColMajor) throw new ArgumentException("Matrix must be col-major");
        return CreateColMajorMatrixAt(value.data, value.rows, value.cols, timestampMicrosec);
    }

    /// <summary>
    ///     Create a MediaPipe protobuf message Packet.
    /// </summary>
    public static Packet<TMessage> CreateProto<TMessage>(TMessage value) where TMessage : IMessage<TMessage>
    {
        unsafe
        {
            int size = value.CalculateSize();
            byte* arr = stackalloc byte[size];
            value.WriteTo(new Span<byte>(arr, size));

            UnsafeNativeMethods.mp__PacketFromDynamicProto__PKc_PKc_i(value.Descriptor.FullName, arr, size,
                out IntPtr statusPtr, out IntPtr ptr).Assert();

            Status.UnsafeAssertOk(statusPtr);
            return new Packet<TMessage>(ptr, true);
        }
    }

    /// <summary>
    ///     Create a MediaPipe protobuf message Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<TMessage> CreateProtoAt<TMessage>(TMessage value, long timestampMicrosec)
        where TMessage : IMessage<TMessage>
    {
        unsafe
        {
            int size = value.CalculateSize();
            byte* arr = stackalloc byte[size];
            value.WriteTo(new Span<byte>(arr, size));

            UnsafeNativeMethods.mp__PacketFromDynamicProto_At__PKc_PKc_i_ll(value.Descriptor.FullName, arr, size,
                timestampMicrosec, out IntPtr statusPtr, out IntPtr ptr).Assert();
            Status.UnsafeAssertOk(statusPtr);

            return new Packet<TMessage>(ptr, true);
        }
    }

    /// <summary>
    ///     Create a string Packet.
    /// </summary>
    public static Packet<string> CreateString(string value)
    {
        UnsafeNativeMethods.mp__MakeStringPacket__PKc(value ?? "", out IntPtr ptr).Assert();

        return new Packet<string>(ptr, true);
    }

    /// <summary>
    ///     Create a string Packet.
    /// </summary>
    public static Packet<string> CreateString(byte[] value)
    {
        UnsafeNativeMethods.mp__MakeStringPacket__PKc_i(value, value?.Length ?? 0, out IntPtr ptr).Assert();

        return new Packet<string>(ptr, true);
    }

    /// <summary>
    ///     Create a string Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<string> CreateStringAt(string value, long timestampMicrosec)
    {
        UnsafeNativeMethods.mp__MakeStringPacket_At__PKc_ll(value ?? "", timestampMicrosec, out IntPtr ptr).Assert();

        return new Packet<string>(ptr, true);
    }

    /// <summary>
    ///     Create a string Packet.
    /// </summary>
    /// <param name="timestampMicrosec">
    ///     The timestamp of the packet.
    /// </param>
    public static Packet<string> CreateStringAt(byte[] value, long timestampMicrosec)
    {
        UnsafeNativeMethods
            .mp__MakeStringPacket_At__PKc_i_ll(value, value?.Length ?? 0, timestampMicrosec, out IntPtr ptr).Assert();

        return new Packet<string>(ptr, true);
    }
}

public class Packet<TValue> : MpResourceHandle
{
    public Packet() : base()
    {
        UnsafeNativeMethods.mp_Packet__(out IntPtr ptr).Assert();
        Ptr = ptr;
    }

    internal Packet(nint ptr, bool isOwner) : base(ptr, isOwner)
    {
    }

    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.mp_Packet__delete(Ptr);
    }

    public Packet<TValue> At(Timestamp timestamp)
    {
        UnsafeNativeMethods.mp_Packet__At__Rt(MpPtr, timestamp.MpPtr, out IntPtr packetPtr).Assert();
        GC.KeepAlive(this);
        GC.KeepAlive(timestamp);

        Dispose();
        return new Packet<TValue>(packetPtr, true);
    }

    public long TimestampMicroseconds()
    {
        long value = SafeNativeMethods.mp_Packet__TimestampMicroseconds(MpPtr);
        GC.KeepAlive(this);

        return value;
    }

    public bool IsEmpty()
    {
        return SafeNativeMethods.mp_Packet__IsEmpty(MpPtr);
    }

    internal void SwitchNativePtr(nint packetPtr)
    {
        if (IsOwner)
            throw new InvalidOperationException(
                "This operation is permitted only when the packet instance is for reference");
        Ptr = packetPtr;
    }

    /// <summary>
    ///     Low-level API to reference the packet that <paramref name="ptr" /> points to.
    /// </summary>
    /// <remarks>
    ///     This method is to be used when you want to reference the packet whose lifetime is managed by native code.
    /// </remarks>
    /// <param name="ptr">
    ///     A pointer to a native Packet instance.
    /// </param>
    public static Packet<TValue> CreateForReference(nint ptr)
    {
        return new Packet<TValue>(ptr, false);
    }
}