using System.Runtime.InteropServices;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    #region OutputStreamPoller

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_OutputStreamPoller__delete(IntPtr poller);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_OutputStreamPoller__Reset(IntPtr poller);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode
        mp_OutputStreamPoller__Next_Ppacket(IntPtr poller, IntPtr packet, out bool result);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_OutputStreamPoller__SetMaxQueueSize(IntPtr poller, int queueSize);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_OutputStreamPoller__QueueSize(IntPtr poller, out int queueSize);

    #endregion
}