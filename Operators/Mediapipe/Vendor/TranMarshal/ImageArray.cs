using System.Runtime.InteropServices;
using Mediapipe.PInvoke;

namespace Mediapipe.TranMarshal;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ImageArray
{
    private readonly nint _data;
    private readonly int _size;

    public void Dispose()
    {
        UnsafeNativeMethods.mp_api_ImageArray__delete(_data);
    }

    public ReadOnlySpan<nint> AsReadOnlySpan()
    {
        unsafe
        {
            return new ReadOnlySpan<nint>((nint*)_data, _size);
        }
    }
}