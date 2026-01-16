using Mediapipe.Core;
using Mediapipe.PInvoke;

namespace Mediapipe.Gpu;

public class GlTextureBuffer : MpResourceHandle
{
    /// <remarks>
    ///     In the original MediaPipe repo, DeletionCallback only receives GlSyncToken.
    ///     However, IL2CPP does not support marshaling delegates that point to instance methods to native code,
    ///     so it receives also the texture name to specify the target instance.
    /// </remarks>
    public delegate void DeletionCallback(uint name, nint glSyncToken);

    private SharedPtrHandle? _sharedPtrHandle;

    public GlTextureBuffer(nint ptr, bool isOwner = true) : base(isOwner)
    {
        _sharedPtrHandle = new CSharedPtr(ptr, isOwner);
        Ptr = _sharedPtrHandle.Get();
    }

    /// <param name="callback">
    ///     A function called when the texture buffer is deleted.
    ///     Make sure that this function doesn't throw exceptions and won't be GCed.
    /// </param>
    public GlTextureBuffer(uint target, uint name, int width, int height,
        GpuBufferFormat format, DeletionCallback callback, GlContext? glContext)
    {
        IntPtr sharedContextPtr = glContext == null ? nint.Zero : glContext.SharedPtr;
        UnsafeNativeMethods.mp_SharedGlTextureBuffer__ui_ui_i_i_ui_PF_PSgc(
            target, name, width, height, format, callback, sharedContextPtr, out IntPtr ptr).Assert();

        _sharedPtrHandle = new CSharedPtr(ptr);
        Ptr = _sharedPtrHandle.Get();
    }

    public GlTextureBuffer(uint name, int width, int height, GpuBufferFormat format, DeletionCallback callback,
        GlContext? glContext = null) :
        this(Gl.GL_TEXTURE_2D, name, width, height, format, callback, glContext)
    {
    }

    public nint SharedPtr => _sharedPtrHandle == null ? nint.Zero : _sharedPtrHandle.MpPtr;

    protected override void DisposeManaged()
    {
        if (_sharedPtrHandle != null)
        {
            _sharedPtrHandle.Dispose();
            _sharedPtrHandle = null;
        }

        base.DisposeManaged();
    }

    protected override void DeleteMpPtr()
    {
        // Do nothing
    }

    public uint Name()
    {
        return SafeNativeMethods.mp_GlTextureBuffer__name(MpPtr);
    }

    public uint Target()
    {
        return SafeNativeMethods.mp_GlTextureBuffer__target(MpPtr);
    }

    public int Width()
    {
        return SafeNativeMethods.mp_GlTextureBuffer__width(MpPtr);
    }

    public int Height()
    {
        return SafeNativeMethods.mp_GlTextureBuffer__height(MpPtr);
    }

    public GpuBufferFormat Format()
    {
        return SafeNativeMethods.mp_GlTextureBuffer__format(MpPtr);
    }

    public void WaitUntilComplete()
    {
        UnsafeNativeMethods.mp_GlTextureBuffer__WaitUntilComplete(MpPtr).Assert();
    }

    public void WaitOnGpu()
    {
        UnsafeNativeMethods.mp_GlTextureBuffer__WaitOnGpu(MpPtr).Assert();
    }

    public void Reuse()
    {
        UnsafeNativeMethods.mp_GlTextureBuffer__Reuse(MpPtr).Assert();
    }

    public void Updated(GlSyncPoint prodToken)
    {
        UnsafeNativeMethods.mp_GlTextureBuffer__Updated__Pgst(MpPtr, prodToken.SharedPtr).Assert();
    }

    public void DidRead(GlSyncPoint consToken)
    {
        UnsafeNativeMethods.mp_GlTextureBuffer__DidRead__Pgst(MpPtr, consToken.SharedPtr).Assert();
    }

    public void WaitForConsumers()
    {
        UnsafeNativeMethods.mp_GlTextureBuffer__WaitForConsumers(MpPtr).Assert();
    }

    public void WaitForConsumersOnGpu()
    {
        UnsafeNativeMethods.mp_GlTextureBuffer__WaitForConsumersOnGpu(MpPtr).Assert();
    }

    public GlContext GetProducerContext()
    {
        return new GlContext(SafeNativeMethods.mp_GlTextureBuffer__GetProducerContext(MpPtr), false);
    }

    internal class CSharedPtr(nint ptr, bool isOwner = true) : SharedPtrHandle(ptr, isOwner)
    {
        protected override void DeleteMpPtr()
        {
            UnsafeNativeMethods.mp_SharedGlTextureBuffer__delete(Ptr);
        }

        public override nint Get()
        {
            return SafeNativeMethods.mp_SharedGlTextureBuffer__get(MpPtr);
        }

        public override void Reset()
        {
            UnsafeNativeMethods.mp_SharedGlTextureBuffer__reset(MpPtr);
        }
    }
}