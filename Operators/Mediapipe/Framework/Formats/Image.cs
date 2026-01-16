using Mediapipe.Core;
using Mediapipe.Gpu;
using Mediapipe.PInvoke;

namespace Mediapipe.Framework.Formats;

public class Image : MpResourceHandle
{
    private static readonly ImageFrame.Deleter _VoidDeleter = ImageFrame.VoidDeleter;

    public Image(nint imagePtr, bool isOwner = true) : base(imagePtr, isOwner)
    {
    }

    public Image(ImageFormat.Types.Format format, int width, int height, int widthStep, nint pixelData,
        ImageFrame.Deleter deleter)
    {
        UnsafeNativeMethods
            .mp_Image__ui_i_i_i_Pui8_PF(format, width, height, widthStep, pixelData, deleter, out IntPtr ptr).Assert();
        Ptr = ptr;
    }

    public unsafe Image(ImageFormat.Types.Format format, int width, int height, int widthStep, byte[] pixelData,
        ImageFrame.Deleter deleter)
    {
        fixed (void* source = pixelData)
        {
            UnsafeNativeMethods
                .mp_Image__ui_i_i_i_Pui8_PF(format, width, height, widthStep, new nint(source), deleter, out IntPtr ptr)
                .Assert();
            Ptr = ptr;
        }
    }

    /// <summary>
    ///     Initialize an <see cref="Image" />.
    /// </summary>
    /// <remarks>
    ///     <paramref name="pixelData" /> won't be released if the instance is disposed of.<br />
    ///     It's useful when:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>You can reuse the memory allocated to <paramref name="pixelData" />.</description>
    ///         </item>
    ///         <item>
    ///             <description>You've not allocated the memory (e.g. <see cref="Texture2D.GetRawTextureData" />).</description>
    ///         </item>
    ///     </list>
    /// </remarks>
    public Image(ImageFormat.Types.Format format, int width, int height, int widthStep, byte[] pixelData)
        : this(format, width, height, widthStep, pixelData, _VoidDeleter)
    {
    }

    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.mp_Image__delete(Ptr);
    }

    public int Width()
    {
        int ret = SafeNativeMethods.mp_Image__width(MpPtr);

        return ret;
    }

    public int Height()
    {
        int ret = SafeNativeMethods.mp_Image__height(MpPtr);

        return ret;
    }

    public int Channels()
    {
        int ret = SafeNativeMethods.mp_Image__channels(MpPtr);

        return ret;
    }

    public int Step()
    {
        int ret = SafeNativeMethods.mp_Image__step(MpPtr);

        return ret;
    }

    public bool UsesGpu()
    {
        bool ret = SafeNativeMethods.mp_Image__UsesGpu(MpPtr);

        return ret;
    }

    public ImageFormat.Types.Format ImageFormat()
    {
        ImageFormat.Types.Format ret = SafeNativeMethods.mp_Image__image_format(MpPtr);

        return ret;
    }

    public GpuBufferFormat Format()
    {
        GpuBufferFormat ret = SafeNativeMethods.mp_Image__format(MpPtr);

        return ret;
    }

    public bool ConvertToCpu()
    {
        UnsafeNativeMethods.mp_Image__ConvertToCpu(MpPtr, out bool result).Assert();

        return result;
    }

    public bool ConvertToGpu()
    {
        UnsafeNativeMethods.mp_Image__ConvertToGpu(MpPtr, out bool result).Assert();

        return result;
    }
}

public class PixelWriteLock : MpResourceHandle
{
    public PixelWriteLock(Image image)
    {
        UnsafeNativeMethods.mp_PixelWriteLock__RI(image.MpPtr, out IntPtr ptr).Assert();
        Ptr = ptr;
    }

    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.mp_PixelWriteLock__delete(Ptr);
    }

    public nint Pixels()
    {
        return SafeNativeMethods.mp_PixelWriteLock__Pixels(MpPtr);
    }
}