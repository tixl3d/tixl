using System;

namespace T3.Core.Utils.Math3d;

public static class VectorT3
{
    public static readonly Vector3 Up = new(0, 1, 0);
    public static readonly Vector3 Down = new(0, -1, 0);
    public static readonly Vector3 Right = new(1, 0, 0);
    public static readonly Vector3 Left = new(-1, 0, 0);

    /// <summary>
    /// Represents a vector pointing forwards (0, 0, -1) in a right-handed coordinate system.
    /// </summary>
    public static readonly Vector3 Forward = new(0, 0, -1);

    public static readonly Vector3 ForwardRH = new(0, 0, -1);
    public static readonly Vector3 ForwardLH = new(0, 0, 1);

    /// <summary>
    /// Represents a vector pointing backwards (0, 0, 1) in a right-handed coordinate system.
    /// </summary>
    public static readonly Vector3 Backward = new(0, 0, 1);

    public static readonly Vector3 BackwardRH = new(0, 0, 1);
    public static readonly Vector3 BackwardLH = new(0, 0, -1);

    public static float GetValueUnsafe(this Vector3 vec, int index)
    {
        unsafe
        {
            return *(&vec.X + index);
        }
    }

    public static float GetValue(this Vector3 vec, int index)
    {
        switch (index)
        {
            case 0:
                return vec.X;
            case 1:
                return vec.Y;
            case 2:
                return vec.Z;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public static Vector4 ToVector4(this Vector3 vec, float w) => new(vec.X, vec.Y, vec.Z, w);

    public static float Axis(this Vector3 vec, int axis) => axis switch
                                                                {
                                                                    0 => vec.X,
                                                                    1 => vec.Y,
                                                                    2 => vec.Z,
                                                                    _ => throw new ArgumentOutOfRangeException(nameof(axis))
                                                                };
}