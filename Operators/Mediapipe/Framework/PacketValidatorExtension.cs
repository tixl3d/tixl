using Google.Protobuf;
using Mediapipe.Core;
using Mediapipe.Framework.Formats;
using Mediapipe.Framework.Port;
using Mediapipe.Gpu;
using Mediapipe.PInvoke;

namespace Mediapipe.Framework;

public static class PacketValidatorExtension
{
    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is a boolean.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain bool data.
    /// </exception>
    public static void Validate(this Packet<bool> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsBool(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsBool(this Packet<bool> packet)
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is a std::vector&lt;bool&gt;.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain std::vector&lt;bool&gt;.
    /// </exception>
    public static void Validate(this Packet<List<bool>> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsBoolVector(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsBoolVector(this Packet<List<bool>> packet)
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is a double.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain double data.
    /// </exception>
    public static void Validate(this Packet<double> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsDouble(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsDouble(this Packet<double> packet)
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is a float.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain float data.
    /// </exception>
    public static void Validate(this Packet<float> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsFloat(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsFloat(this Packet<float> packet)
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is a float array.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain a float array.
    /// </exception>
    public static void Validate(this Packet<float[]> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsFloatArray(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsFloatArray(this Packet<float[]> packet)
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is std::vector&lt;float&gt;.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain std::vector&lt;bool&gt;.
    /// </exception>
    public static void Validate(this Packet<List<float>> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsFloatVector(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsFloatVector(this Packet<List<float>> packet)
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is an <see cref="GpuBuffer" />.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain <see cref="GpuBuffer" />.
    /// </exception>
    public static void Validate(this Packet<GpuBuffer> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsGpuBuffer(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsGpuBuffer(this Packet<GpuBuffer> packet)
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is an <see cref="Image" />.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain <see cref="Image" />.
    /// </exception>
    public static void Validate(this Packet<Image> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsImage(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsImage(this Packet<Image> packet)
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is an <see cref="ImageFrame" />.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain <see cref="ImageFrame" />.
    /// </exception>
    public static void Validate(this Packet<ImageFrame> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsImageFrame(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsImageFrame(this Packet<ImageFrame> packet)
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is an <see langword="int" />.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain <see langword="int" />.
    /// </exception>
    public static void Validate(this Packet<int> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsInt(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsInt(this Packet<int> packet)
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is mediapipe::Matrix.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain mediapipe::Matrix.
    /// </exception>
    public static void Validate(this Packet<Matrix> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsMatrix(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is a proto message.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain proto messages.
    /// </exception>
    public static void Validate<T>(this Packet<T> packet) where T : IMessage<T>
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsProtoMessageLite(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsProtoMessageLite<T>(this Packet<T> packet) where T : IMessage<T>
    {
        packet.Validate();
    }

    /// <summary>
    ///     Validate if the content of the <see cref="Packet" /> is a string.
    /// </summary>
    /// <exception cref="BadStatusException">
    ///     If the <see cref="Packet" /> doesn't contain string.
    /// </exception>
    public static void Validate(this Packet<string> packet)
    {
        UnsafeNativeMethods.mp_Packet__ValidateAsString(packet.MpPtr, out IntPtr statusPtr).Assert();

        Status.UnsafeAssertOk(statusPtr);
    }

    [Obsolete("Use Validate instead")]
    public static void ValidateAsString(this Packet<string> packet)
    {
        packet.Validate();
    }
}