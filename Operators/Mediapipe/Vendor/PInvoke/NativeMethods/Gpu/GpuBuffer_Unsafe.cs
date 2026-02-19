using System.Runtime.InteropServices;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GpuBuffer__PSgtb(IntPtr glTextureBuffer, out IntPtr gpuBuffer);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_GpuBuffer__delete(IntPtr gpuBuffer);

    #region Packet

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeGpuBufferPacket__Rgb(IntPtr gpuBuffer, out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeGpuBufferPacket_At__Rgb_Rts(IntPtr gpuBuffer, IntPtr timestamp,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp__MakeGpuBufferPacket_At__Rgb_ll(IntPtr gpuBuffer, long timestampMicrosec,
        out IntPtr packet);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ConsumeGpuBuffer(IntPtr packet, out IntPtr status,
        out IntPtr gpuBuffer);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetGpuBuffer(IntPtr packet, out IntPtr gpuBuffer);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__ValidateAsGpuBuffer(IntPtr packet, out IntPtr status);

    #endregion
}