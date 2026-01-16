using Mediapipe.Core;
using Mediapipe.Framework.Formats;
using Mediapipe.Framework.Port;
using Mediapipe.PInvoke;

namespace Mediapipe.Gpu;

public class GlCalculatorHelper : MpResourceHandle
{
    public delegate void GlFunction();

    public delegate StatusArgs NativeGlStatusFunction();

    public GlCalculatorHelper()
    {
        UnsafeNativeMethods.mp_GlCalculatorHelper__(out IntPtr ptr).Assert();
        Ptr = ptr;
    }

    public uint Framebuffer => SafeNativeMethods.mp_GlCalculatorHelper__framebuffer(MpPtr);

    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.mp_GlCalculatorHelper__delete(Ptr);
    }

    public void InitializeForTest(GpuResources gpuResources)
    {
        UnsafeNativeMethods.mp_GlCalculatorHelper__InitializeForTest__Pgr(MpPtr, gpuResources.MpPtr).Assert();

        GC.KeepAlive(gpuResources);
        GC.KeepAlive(this);
    }

    /// <param name="nativeGlStatusFunction">
    ///     Function that is run in Gl Context.
    ///     Make sure that this function doesn't throw exceptions and won't be GCed.
    /// </param>
    public void RunInGlContext(NativeGlStatusFunction nativeGlStatusFunction)
    {
        UnsafeNativeMethods
            .mp_GlCalculatorHelper__RunInGlContext__PF(MpPtr, nativeGlStatusFunction, out IntPtr statusPtr).Assert();
        GC.KeepAlive(this);

        AssertStatusOk(statusPtr);
    }

    public void RunInGlContext(GlFunction glFunction)
    {
        RunInGlContext(() =>
        {
            try
            {
                glFunction();
                return StatusArgs.Ok();
            }
            catch (Exception e)
            {
                return StatusArgs.Internal(e.ToString());
            }
        });
    }

    public GlTexture CreateSourceTexture(ImageFrame imageFrame)
    {
        UnsafeNativeMethods
            .mp_GlCalculatorHelper__CreateSourceTexture__Rif(MpPtr, imageFrame.MpPtr, out IntPtr texturePtr).Assert();

        GC.KeepAlive(this);
        GC.KeepAlive(imageFrame);
        return new GlTexture(texturePtr);
    }

    public GlTexture CreateSourceTexture(GpuBuffer gpuBuffer)
    {
        UnsafeNativeMethods
            .mp_GlCalculatorHelper__CreateSourceTexture__Rgb(MpPtr, gpuBuffer.MpPtr, out IntPtr texturePtr).Assert();

        GC.KeepAlive(this);
        GC.KeepAlive(gpuBuffer);
        return new GlTexture(texturePtr);
    }

    public GlTexture CreateDestinationTexture(int width, int height, GpuBufferFormat format)
    {
        UnsafeNativeMethods
            .mp_GlCalculatorHelper__CreateDestinationTexture__i_i_ui(MpPtr, width, height, format,
                out IntPtr texturePtr).Assert();

        GC.KeepAlive(this);
        return new GlTexture(texturePtr);
    }

    public GlTexture CreateDestinationTexture(GpuBuffer gpuBuffer)
    {
        UnsafeNativeMethods
            .mp_GlCalculatorHelper__CreateDestinationTexture__Rgb(MpPtr, gpuBuffer.MpPtr, out IntPtr texturePtr)
            .Assert();

        GC.KeepAlive(this);
        GC.KeepAlive(gpuBuffer);
        return new GlTexture(texturePtr);
    }

    public void BindFramebuffer(GlTexture glTexture)
    {
        UnsafeNativeMethods.mp_GlCalculatorHelper__BindFrameBuffer__Rtexture(MpPtr, glTexture.MpPtr).Assert();

        GC.KeepAlive(glTexture);
        GC.KeepAlive(this);
    }

    public GlContext GetGlContext()
    {
        IntPtr glContextPtr = SafeNativeMethods.mp_GlCalculatorHelper__GetGlContext(MpPtr);

        GC.KeepAlive(this);
        return new GlContext(glContextPtr, false);
    }

    public bool Initialized()
    {
        return SafeNativeMethods.mp_GlCalculatorHelper__Initialized(MpPtr);
    }
}