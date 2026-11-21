#nullable enable
using System;
using System.Numerics;

namespace T3.Core.DataTypes.Volume;

/// <summary>
/// Integer lattice coordinate of a voxel, node or tile, matching OpenVDB's openvdb::Coord.
/// Axis layout matches OpenVDB: +X right, +Y up, +Z forward.
/// </summary>
public readonly struct VdbCoord : IEquatable<VdbCoord>, IComparable<VdbCoord>
{
    public const int Log2Leaf = 3;

    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public VdbCoord(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Splat constructor: all components equal.</summary>
    public VdbCoord(int splat)
    {
        X = splat;
        Y = splat;
        Z = splat;
    }

    /// <summary>Coordinate of the voxel within its 8³ leaf node (each component 0..7).</summary>
    public static VdbCoord LocalFromOffset(int offset) => new((offset >> 6) & 7, (offset >> 3) & 7, offset & 7);

    /// <summary>Linear voxel offset within an 8³ leaf node, matching OpenVDB's bit layout (x*64 + y*8 + z, so z is the fastest axis).</summary>
    public static int LeafOffset(int localX, int localY, int localZ) => (localX << 6) + (localY << 3) + localZ;

    public int LeafOffset() => LeafOffset(X & 7, Y & 7, Z & 7);

    public static VdbCoord operator +(VdbCoord a, VdbCoord b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static VdbCoord operator -(VdbCoord a, VdbCoord b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static VdbCoord operator *(VdbCoord a, int s) => new(a.X * s, a.Y * s, a.Z * s);

    /// <summary>Floor division, so negative coordinates snap to the node containing them.</summary>
    public static VdbCoord FloorDiv(VdbCoord c, int n) => new(FloorDiv(c.X, n), FloorDiv(c.Y, n), FloorDiv(c.Z, n));

    public static int FloorDiv(int i, int n)
    {
        var q = i / n;
        if ((i % n != 0) && ((i < 0) != (n < 0)))
            --q;
        return q;
    }

    /// <summary>Floored modulo, always in [0, n).</summary>
    public static int FloorMod(int i, int n) => i - n * FloorDiv(i, n);

    public static VdbCoord FloorDivLeaf(VdbCoord c) => FloorDiv(c, 8);

    public VdbCoord RoundDownLeaf() => new(X & ~7, Y & ~7, Z & ~7);

    public static VdbCoord Min(VdbCoord a, VdbCoord b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static VdbCoord Max(VdbCoord a, VdbCoord b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    public Vector3 ToVector3() => new(X, Y, Z);

    public bool Equals(VdbCoord other) => X == other.X && Y == other.Y && Z == other.Z;

    /// <summary>Z-major ordering, matching the tree iteration conventions.</summary>
    public int CompareTo(VdbCoord other)
        => Z != other.Z ? Z.CompareTo(other.Z) : Y != other.Y ? Y.CompareTo(other.Y) : X.CompareTo(other.X);
    public override bool Equals(object? obj) => obj is VdbCoord other && Equals(other);
    public override int GetHashCode() => (X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791);
    public override string ToString() => $"({X},{Y},{Z})";
    public static bool operator ==(VdbCoord a, VdbCoord b) => a.Equals(b);
    public static bool operator !=(VdbCoord a, VdbCoord b) => !a.Equals(b);
}

/// <summary>Axis-aligned integer bounding box over voxel coordinates.</summary>
public struct VdbCoordBBox
{
    public VdbCoord Min;
    public VdbCoord Max;

    public static readonly VdbCoordBBox Empty = new() { Min = new VdbCoord(int.MaxValue), Max = new VdbCoord(int.MinValue) };

    public VdbCoordBBox(VdbCoord min, VdbCoord max)
    {
        Min = min;
        Max = max;
    }

    public readonly bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    public readonly long Volume => IsEmpty ? 0 : (long)(Max.X - Min.X + 1) * (Max.Y - Min.Y + 1) * (Max.Z - Min.Z + 1);

    public void Expand(VdbCoord c)
    {
        Min = VdbCoord.Min(Min, c);
        Max = VdbCoord.Max(Max, c);
    }

    public void Expand(VdbCoordBBox other)
    {
        if (other.IsEmpty)
            return;
        Min = IsEmpty ? other.Min : VdbCoord.Min(Min, other.Min);
        Max = IsEmpty ? other.Max : VdbCoord.Max(Max, other.Max);
    }

    public readonly bool Contains(VdbCoord c)
        => c.X >= Min.X && c.X <= Max.X && c.Y >= Min.Y && c.Y <= Max.Y && c.Z >= Min.Z && c.Z <= Max.Z;

    public readonly bool Intersects(VdbCoordBBox other)
        => !(IsEmpty || other.IsEmpty)
           && other.Min.X <= Max.X && other.Max.X >= Min.X
           && other.Min.Y <= Max.Y && other.Max.Y >= Min.Y
           && other.Min.Z <= Max.Z && other.Max.Z >= Min.Z;

    public static VdbCoordBBox Intersect(VdbCoordBBox a, VdbCoordBBox b)
    {
        if (!a.Intersects(b))
            return Empty;
        return new VdbCoordBBox(VdbCoord.Max(a.Min, b.Min), VdbCoord.Min(a.Max, b.Max));
    }
}
