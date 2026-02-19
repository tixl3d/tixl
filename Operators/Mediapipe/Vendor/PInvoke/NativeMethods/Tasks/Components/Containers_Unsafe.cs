using System.Runtime.InteropServices;
using Mediapipe.TranMarshal;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetClassificationResult(IntPtr packet,
        out NativeClassificationResult value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetClassificationsVector(IntPtr packet,
        out NativeClassificationResult value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_tasks_c_components_containers_CppCloseClassificationResult(
        NativeClassificationResult data);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetClassificationResultVector(IntPtr packet,
        out NativeClassificationResultArray value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_api_ClassificationResultArray__delete(NativeClassificationResultArray data);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetDetectionResult(IntPtr packet, out NativeDetectionResult value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_tasks_c_components_containers_CppCloseDetectionResult(NativeDetectionResult data);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetLandmarksVector(IntPtr packet, out NativeLandmarksArray value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_api_LandmarksArray__delete(NativeLandmarksArray data);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern MpReturnCode mp_Packet__GetNormalizedLandmarksVector(IntPtr packet,
        out NativeNormalizedLandmarksArray value);

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    public static extern void mp_api_NormalizedLandmarksArray__delete(NativeNormalizedLandmarksArray data);
}