using Mediapipe.Core;
using Mediapipe.PInvoke;

namespace Mediapipe.External;

public class StdString : MpResourceHandle
{
    public StdString(nint ptr, bool isOwner = true) : base(ptr, isOwner)
    {
    }

    public StdString(byte[] bytes)
    {
        UnsafeNativeMethods.std_string__PKc_i(bytes, bytes.Length, out IntPtr ptr).Assert();
        Ptr = ptr;
    }

    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.std_string__delete(Ptr);
    }

    public void Swap(StdString str)
    {
        UnsafeNativeMethods.std_string__swap__Rstr(MpPtr, str.MpPtr);
        GC.KeepAlive(this);
    }
}