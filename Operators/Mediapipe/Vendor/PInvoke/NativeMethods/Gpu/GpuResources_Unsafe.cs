using System.Runtime.InteropServices;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_SharedGpuResources__delete(IntPtr gpuResources);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_SharedGpuResources__reset(IntPtr gpuResources);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GpuResources_Create(out IntPtr status, out IntPtr gpuResources);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_GpuResources_Create__Pv(IntPtr externalContext, out IntPtr status,
        out IntPtr gpuResources);
}