using System.Runtime.InteropServices;
using Google.Protobuf;
using Mediapipe.PInvoke;

namespace Mediapipe.External;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct SerializedProtoVector
{
    private readonly nint _data;
    private readonly int _size;

    public void Dispose()
    {
        UnsafeNativeMethods.mp_api_SerializedProtoArray__delete(_data, _size);
    }

    public List<T> Deserialize<T>(MessageParser<T> parser) where T : IMessage<T>
    {
        List<T> protos = new(_size);

        Deserialize(parser, protos);

        return protos;
    }

    /// <summary>
    ///     Deserializes the data as a list of <typeparamref name="T" />.
    /// </summary>
    /// <param name="protos">A list of <typeparamref name="T" /> to populate</param>
    public void Deserialize<T>(MessageParser<T> parser, List<T> protos) where T : IMessage<T>
    {
        protos.Clear();
        _ = WriteTo(parser, protos);
    }

    /// <summary>
    ///     Deserializes the data as a list of <typeparamref name="T" />.
    /// </summary>
    /// <remarks>
    ///     The deserialized data will be merged into <paramref name="protos" />.
    ///     You may want to clear each field of <typeparamref name="T" /> before calling this method.
    ///     If <see cref="_size" /> is less than <paramref name="protos" />.Count, the superfluous elements in
    ///     <paramref name="protos" /> will be untouched.
    /// </remarks>
    /// <param name="protos">A list of <typeparamref name="T" /> to populate</param>
    /// <returns>
    ///     The number of written elements in <paramref name="protos" />.
    /// </returns>
    public int WriteTo<T>(MessageParser<T> parser, List<T> protos) where T : IMessage<T>
    {
        unsafe
        {
            SerializedProto* protoPtr = (SerializedProto*)_data;

            // overwrite the existing list
            int len = Math.Min(_size, protos.Count);
            for (int i = 0; i < len; i++)
            {
                SerializedProto serializedProto = Marshal.PtrToStructure<SerializedProto>((nint)protoPtr++);
                serializedProto.WriteTo(protos[i]);
            }

            for (int i = protos.Count; i < _size; i++)
            {
                SerializedProto serializedProto = Marshal.PtrToStructure<SerializedProto>((nint)protoPtr++);
                protos.Add(serializedProto.Deserialize(parser));
            }
        }

        return _size;
    }
}