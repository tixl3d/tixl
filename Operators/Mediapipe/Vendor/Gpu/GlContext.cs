using Mediapipe.Core;
using Mediapipe.PInvoke;

namespace Mediapipe.Gpu;

public class GlContext : MpResourceHandle
{
    private SharedPtrHandle? _sharedPtrHandle;

    public GlContext(nint ptr, bool isOwner = true) : base(isOwner)
    {
        _sharedPtrHandle = new CSharedPtr(ptr, isOwner);
        Ptr = _sharedPtrHandle.Get();
    }

    public nint SharedPtr => _sharedPtrHandle == null ? nint.Zero : _sharedPtrHandle.MpPtr;

    public IntPtr eglDisplay => SafeNativeMethods.mp_GlContext__egl_display(MpPtr);

    public IntPtr eglConfig => SafeNativeMethods.mp_GlContext__egl_config(MpPtr);

    public IntPtr eglContext => SafeNativeMethods.mp_GlContext__egl_context(MpPtr);

    // NOTE: On macOS, native libs cannot be built with GPU enabled, so it cannot be used actually.
    public IntPtr nsglContext => SafeNativeMethods.mp_GlContext__nsgl_context(MpPtr);
    public IntPtr eaglContext => SafeNativeMethods.mp_GlContext__eagl_context(MpPtr);

    public int glMajorVersion => SafeNativeMethods.mp_GlContext__gl_major_version(MpPtr);

    public int glMinorVersion => SafeNativeMethods.mp_GlContext__gl_minor_version(MpPtr);

    public long glFinishCount => SafeNativeMethods.mp_GlContext__gl_finish_count(MpPtr);

    public static GlContext? GetCurrent()
    {
        UnsafeNativeMethods.mp_GlContext_GetCurrent(out IntPtr glContextPtr).Assert();

        return glContextPtr == nint.Zero ? null : new GlContext(glContextPtr);
    }

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

    public bool IsCurrent()
    {
        return SafeNativeMethods.mp_GlContext__IsCurrent(MpPtr);
    }

    private class CSharedPtr : SharedPtrHandle
    {
        public CSharedPtr(nint ptr, bool isOwner = true) : base(ptr, isOwner)
        {
        }

        protected override void DeleteMpPtr()
        {
            UnsafeNativeMethods.mp_SharedGlContext__delete(Ptr);
        }

        public override nint Get()
        {
            return SafeNativeMethods.mp_SharedGlContext__get(MpPtr);
        }

        public override void Reset()
        {
            UnsafeNativeMethods.mp_SharedGlContext__reset(MpPtr);
        }
    }
}