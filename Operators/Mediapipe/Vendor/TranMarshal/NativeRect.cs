using System.Runtime.InteropServices;

namespace Mediapipe.TranMarshal;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRect
{
    public readonly int Left;
    public readonly int Top;
    public readonly int Bottom;
    public readonly int Right;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRectF
{
    public readonly float Left;
    public readonly float Top;
    public readonly float Bottom;
    public readonly float Right;
}