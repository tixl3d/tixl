using System.Runtime.InteropServices;
using Mediapipe.External;
using Mediapipe.Tasks.Core;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_tasks_core_TaskRunner_Create__PKc_i_PF_Pgr(byte[] serializedConfig, int size,
        int callbackId, [MarshalAs(UnmanagedType.FunctionPtr)] TaskRunner.NativePacketsCallback packetsCallback,
        IntPtr gpuResources,
        out IntPtr status, out IntPtr taskRunner);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_tasks_core_TaskRunner_Create__PKc_i_PF(byte[] serializedConfig, int size,
        int callbackId, [MarshalAs(UnmanagedType.FunctionPtr)] TaskRunner.NativePacketsCallback packetsCallback,
        out IntPtr status, out IntPtr taskRunner);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_tasks_core_TaskRunner__delete(IntPtr taskRunner);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_tasks_core_TaskRunner__Process__Ppm(IntPtr taskRunner, IntPtr inputs,
        out IntPtr status, out IntPtr packetMap);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_tasks_core_TaskRunner__Send__Ppm(IntPtr taskRunner, IntPtr inputs,
        out IntPtr status);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_tasks_core_TaskRunner__Close(IntPtr taskRunner, out IntPtr status);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_tasks_core_TaskRunner__Restart(IntPtr taskRunner, out IntPtr status);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_tasks_core_TaskRunner__GetGraphConfig(IntPtr taskRunner,
        out SerializedProto serializedProto);
}