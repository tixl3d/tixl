using Mediapipe.Core;
using Mediapipe.PInvoke;

namespace Mediapipe.Gpu;

public class GpuBuffer : MpResourceHandle
{
    public GpuBuffer(nint ptr, bool isOwner = true) : base(ptr, isOwner)
    {
    }

    public GpuBuffer(GlTextureBuffer glTextureBuffer)
    {
        UnsafeNativeMethods.mp_GpuBuffer__PSgtb(glTextureBuffer.SharedPtr, out IntPtr ptr).Assert();
        glTextureBuffer.Dispose(); // respect move semantics
        Ptr = ptr;
    }

    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.mp_GpuBuffer__delete(Ptr);
    }

    public GpuBufferFormat Format()
    {
        return SafeNativeMethods.mp_GpuBuffer__format(MpPtr);
    }

    public int Width()
    {
        return SafeNativeMethods.mp_GpuBuffer__width(MpPtr);
    }

    public int Height()
    {
        return SafeNativeMethods.mp_GpuBuffer__height(MpPtr);
    }
}