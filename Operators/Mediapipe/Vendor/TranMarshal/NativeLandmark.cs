using System.Runtime.InteropServices;
using Mediapipe.PInvoke;

namespace Mediapipe.TranMarshal;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeLandmark
{
    public readonly float x;
    public readonly float y;
    public readonly float z;

    [MarshalAs(UnmanagedType.I1)] public readonly bool hasVisibility;
    public readonly float visibility;

    [MarshalAs(UnmanagedType.I1)] public readonly bool hasPresence;
    public readonly float presence;

    private readonly nint _name;

    public string? Name => Marshal.PtrToStringAnsi(_name);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeNormalizedLandmark
{
    public readonly float x;
    public readonly float y;
    public readonly float z;

    [MarshalAs(UnmanagedType.I1)] public readonly bool hasVisibility;
    public readonly float visibility;

    [MarshalAs(UnmanagedType.I1)] public readonly bool hasPresence;
    public readonly float presence;

    private readonly nint _name;

    public string? Name => Marshal.PtrToStringAnsi(_name);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeLandmarks
{
    private readonly nint _landmarks;
    public readonly uint landmarksCount;

    public ReadOnlySpan<NativeLandmark> AsReadOnlySpan()
    {
        unsafe
        {
            return new ReadOnlySpan<NativeLandmark>((NativeLandmark*)_landmarks, (int)landmarksCount);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeNormalizedLandmarks
{
    private readonly nint _landmarks;
    public readonly uint landmarksCount;

    public ReadOnlySpan<NativeNormalizedLandmark> AsReadOnlySpan()
    {
        unsafe
        {
            return new ReadOnlySpan<NativeNormalizedLandmark>((NativeNormalizedLandmark*)_landmarks,
                (int)landmarksCount);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeLandmarksArray
{
    private readonly nint _data;
    public readonly int size;

    public void Dispose()
    {
        UnsafeNativeMethods.mp_api_LandmarksArray__delete(this);
    }

    public ReadOnlySpan<NativeLandmarks> AsReadOnlySpan()
    {
        unsafe
        {
            return new ReadOnlySpan<NativeLandmarks>((NativeLandmarks*)_data, size);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeNormalizedLandmarksArray
{
    private readonly nint _data;
    public readonly int size;

    public void Dispose()
    {
        UnsafeNativeMethods.mp_api_NormalizedLandmarksArray__delete(this);
    }

    public ReadOnlySpan<NativeNormalizedLandmarks> AsReadOnlySpan()
    {
        unsafe
        {
            return new ReadOnlySpan<NativeNormalizedLandmarks>((NativeNormalizedLandmarks*)_data, size);
        }
    }
}