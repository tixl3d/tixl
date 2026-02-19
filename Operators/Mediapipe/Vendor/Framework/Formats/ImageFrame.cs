using Mediapipe.Core;
using Mediapipe.Extension;
using Mediapipe.PInvoke;

namespace Mediapipe.Framework.Formats;

public class ImageFrame : MpResourceHandle
{
    public delegate void Deleter(nint ptr);

    public static readonly uint DefaultAlignmentBoundary = 16;
    public static readonly uint GlDefaultAlignmentBoundary = 4;

    private static readonly Deleter _VoidDeleter = VoidDeleter;

    public ImageFrame()
    {
        UnsafeNativeMethods.mp_ImageFrame__(out IntPtr ptr).Assert();
        Ptr = ptr;
    }

    public ImageFrame(nint imageFramePtr, bool isOwner = true) : base(imageFramePtr, isOwner)
    {
    }

    public ImageFrame(ImageFormat.Types.Format format, int width, int height) : this(format, width, height,
        DefaultAlignmentBoundary)
    {
    }

    public ImageFrame(ImageFormat.Types.Format format, int width, int height, uint alignmentBoundary)
    {
        UnsafeNativeMethods.mp_ImageFrame__ui_i_i_ui(format, width, height, alignmentBoundary, out IntPtr ptr).Assert();
        Ptr = ptr;
    }

    public ImageFrame(ImageFormat.Types.Format format, int width, int height, int widthStep, nint pixelData,
        Deleter deleter)
    {
        UnsafeNativeMethods
            .mp_ImageFrame__ui_i_i_i_Pui8_PF(format, width, height, widthStep, pixelData, deleter, out IntPtr ptr)
            .Assert();
        Ptr = ptr;
    }

    public unsafe ImageFrame(ImageFormat.Types.Format format, int width, int height, int widthStep, byte[] pixelData,
        Deleter deleter)
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
    ///     Initialize an <see cref="ImageFrame" />.
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
    public ImageFrame(ImageFormat.Types.Format format, int width, int height, int widthStep, byte[] pixelData)
        : this(format, width, height, widthStep, pixelData, _VoidDeleter)
    {
    }

    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.mp_ImageFrame__delete(Ptr);
    }

    internal static void VoidDeleter(nint _)
    {
    }

    public bool IsEmpty()
    {
        return SafeNativeMethods.mp_ImageFrame__IsEmpty(MpPtr);
    }

    public bool IsContiguous()
    {
        return SafeNativeMethods.mp_ImageFrame__IsContiguous(MpPtr);
    }

    public bool IsAligned(uint alignmentBoundary)
    {
        SafeNativeMethods.mp_ImageFrame__IsAligned__ui(MpPtr, alignmentBoundary, out bool value).Assert();

        return value;
    }

    public ImageFormat.Types.Format Format()
    {
        return SafeNativeMethods.mp_ImageFrame__Format(MpPtr);
    }

    public int Width()
    {
        return SafeNativeMethods.mp_ImageFrame__Width(MpPtr);
    }

    public int Height()
    {
        return SafeNativeMethods.mp_ImageFrame__Height(MpPtr);
    }

    /// <returns>
    ///     The channel size.
    ///     If channels don't make sense, returns <c>0</c>.
    /// </returns>
    /// <remarks>
    ///     Unlike the original implementation, this API won't signal SIGABRT.
    /// </remarks>
    public int ChannelSize()
    {
        return Format().ChannelSize();
    }

    /// <returns>
    ///     The Number of channels.
    ///     If channels don't make sense, returns <c>0</c>.
    /// </returns>
    /// <remarks>
    ///     Unlike the original implementation, this API won't signal SIGABRT.
    /// </remarks>
    public int NumberOfChannels()
    {
        return Format().NumberOfChannels();
    }

    /// <returns>
    ///     The depth of each image channel in bytes.
    ///     If channels don't make sense, returns <c>0</c>.
    /// </returns>
    /// <remarks>
    ///     Unlike the original implementation, this API won't signal SIGABRT.
    /// </remarks>
    public int ByteDepth()
    {
        return Format().ByteDepth();
    }

    public int WidthStep()
    {
        return SafeNativeMethods.mp_ImageFrame__WidthStep(MpPtr);
    }

    public nint MutablePixelData()
    {
        return SafeNativeMethods.mp_ImageFrame__MutablePixelData(MpPtr);
    }

    public int PixelDataSize()
    {
        return Height() * WidthStep();
    }

    /// <returns>
    ///     The total size the pixel data would take if it was stored contiguously (which may not be the case).
    ///     If channels don't make sense, returns <c>0</c>.
    /// </returns>
    /// <remarks>
    ///     Unlike the original implementation, this API won't signal SIGABRT.
    /// </remarks>
    public int PixelDataSizeStoredContiguously()
    {
        return Width() * Height() * ByteDepth() * NumberOfChannels();
    }

    public void SetToZero()
    {
        UnsafeNativeMethods.mp_ImageFrame__SetToZero(MpPtr).Assert();
        GC.KeepAlive(this);
    }

    public void SetAlignmentPaddingAreas()
    {
        UnsafeNativeMethods.mp_ImageFrame__SetAlignmentPaddingAreas(MpPtr).Assert();
        GC.KeepAlive(this);
    }

    public void CopyToBuffer(byte[] buffer)
    {
        CopyToBuffer(UnsafeNativeMethods.mp_ImageFrame__CopyToBuffer__Pui8_i, buffer);
    }

    public void CopyToBuffer(ushort[] buffer)
    {
        CopyToBuffer(UnsafeNativeMethods.mp_ImageFrame__CopyToBuffer__Pui16_i, buffer);
    }

    public void CopyToBuffer(float[] buffer)
    {
        CopyToBuffer(UnsafeNativeMethods.mp_ImageFrame__CopyToBuffer__Pf_i, buffer);
    }

    private void CopyToBuffer<T>(CopyToBufferHandler handler, T[] buffer) where T : unmanaged
    {
        unsafe
        {
            fixed (T* bufferPtr = buffer)
            {
                handler(MpPtr, (nint)bufferPtr, buffer.Length).Assert();
            }
        }
    }

    private delegate MpReturnCode CopyToBufferHandler(nint ptr, nint buffer, int bufferSize);
}