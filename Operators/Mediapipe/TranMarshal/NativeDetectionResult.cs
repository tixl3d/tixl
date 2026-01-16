using System.Runtime.InteropServices;
using Mediapipe.PInvoke;

namespace Mediapipe.TranMarshal;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeDetection
{
    private readonly nint _categories;

    public readonly uint categoriesCount;

    public readonly NativeRect boundingBox;

    private readonly nint _keypoints;

    public readonly uint keypointsCount;

    public ReadOnlySpan<NativeCategory> Categories
    {
        get
        {
            unsafe
            {
                return new ReadOnlySpan<NativeCategory>((NativeCategory*)_categories, (int)categoriesCount);
            }
        }
    }

    public ReadOnlySpan<NativeNormalizedKeypoint> Keypoints
    {
        get
        {
            unsafe
            {
                return new ReadOnlySpan<NativeNormalizedKeypoint>((NativeNormalizedKeypoint*)_keypoints,
                    (int)keypointsCount);
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeDetectionResult
{
    private readonly nint _detections;
    public readonly uint detectionsCount;

    public ReadOnlySpan<NativeDetection> AsReadOnlySpan()
    {
        unsafe
        {
            return new ReadOnlySpan<NativeDetection>((NativeDetection*)_detections, (int)detectionsCount);
        }
    }

    public void Dispose()
    {
        UnsafeNativeMethods.mp_tasks_c_components_containers_CppCloseDetectionResult(this);
    }
}