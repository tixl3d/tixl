using System;
using System.Numerics;

namespace T3.Core.Utils.Math3d;

public static class PlaneExtensions
{
    public static bool Intersects(this Plane plane, in Ray ray, out Vector3 point)
    {
        float direction = Vector3.Dot(plane.Normal, ray.Direction);

        if (Math.Abs(direction) < 1e-6f)
        {
            point = default;
            return false;
        }

        float distance = (plane.D - Vector3.Dot(plane.Normal, ray.Origin)) / direction;

        if (distance < 0)
        {
            point = default;
            return false;
        }

        point = ray.Origin + distance * ray.Direction;
        return true;
    }

    /// <summary>
    /// Creates a plane from a point that lies on the plane and the normal vector.
    /// Implementation basically copied from SharpDX con.
    /// </summary>
    /// <param name="point">Any point that lies along the plane.</param>
    /// <param name="normal">The normal vector to the plane.</param>
    public static Plane CreateFromPointAndNormal(in Vector3 point, in Vector3 normal)
    {
        var d = -Vector3.Dot(normal, point);
        return new Plane(normal, d);
    }
}