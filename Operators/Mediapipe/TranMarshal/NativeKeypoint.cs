using System.Runtime.InteropServices;

namespace Mediapipe.TranMarshal;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeNormalizedKeypoint
{
    public readonly float x;
    public readonly float y;
    private readonly nint _label;
    public readonly float score;
    public readonly bool hasScore;

    public string? Label => Marshal.PtrToStringAnsi(_label);
}