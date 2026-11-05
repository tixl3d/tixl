using System.Runtime.InteropServices;

namespace T3.Core.DataTypes;

[StructLayout(LayoutKind.Explicit, Size = StructSize)]
public struct Sprite
{
    [FieldOffset(0 * 4)]
    public float Width;

    [FieldOffset(1 * 4)]
    public float Height;

    [FieldOffset(2 * 4)]
    public Vector4 Color;

    [FieldOffset(6 * 4)]
    public Vector2 UvMin;

    [FieldOffset(8 * 4)]
    public Vector2 UvMax;

    [FieldOffset(10 * 4)]
    public Vector2 Pivot;

    [FieldOffset(12 * 4)]
    public uint CharIndex;

    [FieldOffset(13 * 4)]
    public uint CharIndexInLine;

    [FieldOffset(14 * 4)]
    public uint LineIndex;

    [FieldOffset(15 * 4)]
    public uint GroupIndex;

    /// <summary>
    /// Bounds of the sprite's group, relative to its point position and before the draw op's size is applied.
    /// </summary>
    [FieldOffset(16 * 4)]
    public Vector2 GroupBoundsMin;

    [FieldOffset(18 * 4)]
    public Vector2 GroupBoundsMax;

    private const int StructSize = 20 * 4;
}