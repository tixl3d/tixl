using System.Runtime.InteropServices;
using Google.Protobuf;
using Mediapipe.PInvoke;

namespace Mediapipe.External;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct SerializedProto
{
    private readonly nint _str;
    private readonly int _length;

    public void Dispose()
    {
        UnsafeNativeMethods.delete_array__PKc(_str);
    }

    public unsafe T Deserialize<T>(MessageParser<T> parser) where T : IMessage<T>
    {
        ReadOnlySpan<byte> bytes = new((byte*)_str, _length);
        return parser.ParseFrom(bytes);
    }

    public unsafe void WriteTo<T>(T proto) where T : IMessage<T>
    {
        ReadOnlySpan<byte> bytes = new((byte*)_str, _length);
        proto.MergeFrom(bytes);
    }
}