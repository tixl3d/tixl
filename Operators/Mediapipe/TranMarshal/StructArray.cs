using System.Runtime.InteropServices;
using Mediapipe.PInvoke;

namespace Mediapipe.TranMarshal;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct StructArray<T> where T : unmanaged
{
    private readonly nint _data;
    private readonly int _size;

    public void Dispose()
    {
        UnsafeNativeMethods.delete_array__Pf(_data);
    }

    public List<T> Copy()
    {
        List<T> data = new(_size);

        CopyTo(data);
        return data;
    }

    public void CopyTo(List<T> data)
    {
        data.Clear();

        unsafe
        {
            T* ptr = (T*)_data;

            for (int i = 0; i < _size; i++) data.Add(*ptr++);
        }
    }
}