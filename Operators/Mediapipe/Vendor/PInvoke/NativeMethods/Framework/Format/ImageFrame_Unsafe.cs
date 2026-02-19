using System.Runtime.InteropServices;
using Mediapipe.Framework.Formats;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_ImageFrame__(out IntPtr imageFrame);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_ImageFrame__ui_i_i_ui(
        ImageFormat.Types.Format format, int width, int height, uint alignmentBoundary, out IntPtr imageFrame);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_ImageFrame__ui_i_i_i_Pui8_PF(
        ImageFormat.Types.Format format, int width, int height, int widthStep, IntPtr pixelData,
        [MarshalAs(UnmanagedType.FunctionPtr)] ImageFrame.Deleter deleter, out IntPtr imageFrame);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_ImageFrame__delete(IntPtr imageFrame);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_ImageFrame__SetToZero(IntPtr imageFrame);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_ImageFrame__SetAlignmentPaddingAreas(IntPtr imageFrame);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_ImageFrame__CopyToBuffer__Pui8_i(IntPtr imageFrame, IntPtr buffer,
        int bufferSize);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_ImageFrame__CopyToBuffer__Pui16_i(IntPtr imageFrame, IntPtr buffer,
        int bufferSize);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_ImageFrame__CopyToBuffer__Pf_i(IntPtr imageFrame, IntPtr buffer,
        int bufferSize);

    #region Packet

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeImageFramePacket__Pif(IntPtr imageFrame, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeImageFramePacket_At__Pif_Rt(IntPtr imageFrame, IntPtr timestamp,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeImageFramePacket_At__Pif_ll(IntPtr imageFrame, long timestampMicrosec,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ConsumeImageFrame(IntPtr packet, out IntPtr status,
        out IntPtr imageFrame);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetImageFrame(IntPtr packet, out IntPtr imageFrame);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsImageFrame(IntPtr packet, out IntPtr status);

    #endregion
}