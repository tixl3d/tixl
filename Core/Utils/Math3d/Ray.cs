using System;
using System.Numerics;

namespace T3.Core.Utils.Math3d;

public struct Ray
{
    public Vector3 Origin;

    private Vector3 _direction;
    public Vector3 Direction
    {
        get => _direction;
        set => _direction = Vector3.Normalize(value);
    }

    public Ray(in Vector3 origin, in Vector3 direction)
    {
        Origin = origin;
        _direction = Vector3.Normalize(direction);
    }

    public bool Intersects(in Plane plane, out float distance)
    {
        // Get the dot product of the direction vector of the ray and the normal of the plane
        float denominator = Vector3.Dot(Direction, plane.Normal);

        // If the dot product is close to zero, the ray is parallel to the plane (no intersection)
        if (Math.Abs(denominator) < float.Epsilon)
        {
            distance = 0;
            return false;
        }

        // Calculate the distance from the ray origin to the plane
        float nominator = Vector3.Dot(Origin - plane.Normal * plane.D, -plane.Normal);
        distance = nominator / denominator;

        // If the distance is less than zero, the plane is behind the ray origin (we consider this as no intersection)
        if (distance < 0)
        {
            distance = 0;
            return false;
        }

        return true;
    }
}