using System.Runtime.InteropServices;
using Mediapipe.External;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode google_InitGoogleLogging__PKc(string name);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode google_ShutdownGoogleLogging();

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void glog_FLAGS_logtostderr([MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void glog_FLAGS_stderrthreshold(int threshold);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void glog_FLAGS_minloglevel(int level);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void glog_FLAGS_log_dir(string dir);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void glog_FLAGS_v(int v);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void glog_LOG_INFO__PKc(string str);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void glog_LOG_WARNING__PKc(string str);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void glog_LOG_ERROR__PKc(string str);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void glog_LOG_FATAL__PKc(string str);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void google_FlushLogFiles(Glog.Severity severity);
}