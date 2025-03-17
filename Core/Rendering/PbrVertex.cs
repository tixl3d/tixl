using System.Runtime.InteropServices;

namespace T3.Core.Rendering;

[StructLayout(LayoutKind.Explicit, Size = Stride)]
public struct PbrVertex
{
    [FieldOffset(0)]
    public Vector3 Position;

    [FieldOffset(3 * 4)]
    public Vector3 Normal;

    [FieldOffset(6 * 4)]
    public Vector3 Tangent;

    [FieldOffset(9 * 4)]
    public Vector3 Bitangent;

    [FieldOffset(12 * 4)]
    public Vector2 Texcoord;

    [FieldOffset(14 * 4)]
    public Vector2 Texcoord2; // 2 floats -> 8 bytes (newly added second UV set)

    [FieldOffset(16 * 4)]
    public float Selection;

    [FieldOffset(17 * 4)]
    private float __padding;

    public const int Stride = 18 * 4; // Total size: 18 floats -> 72 bytes
}