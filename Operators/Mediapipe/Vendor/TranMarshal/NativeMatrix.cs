using System.Runtime.InteropServices;
using Mediapipe.PInvoke;

namespace Mediapipe.TranMarshal;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeMatrix
{
    private readonly nint _data;
    public readonly int rows;
    public readonly int cols;
    public readonly int layout;

    public void Dispose()
    {
        UnsafeNativeMethods.mp_api_Matrix__delete(this);
    }

    public ReadOnlySpan<float> AsReadOnlySpan()
    {
        unsafe
        {
            return new ReadOnlySpan<float>((float*)_data, rows * cols);
        }
    }
}