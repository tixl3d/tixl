#nullable enable
using System;
using System.Numerics;

namespace T3.Core.DataTypes.Volume.Tools;

/// <summary>
/// Turns distance samples taken on a regular lattice of points into a level-set grid. This is the bridge from the
/// GPU field graph to the CPU: a grid of points goes through SampleFieldPoints, is read back, and the distances it
/// carries become a grid that can be meshed, saved or used as a collider.
/// </summary>
public static class SdfSamples
{
    /// <summary>
    /// Spacing of a point lattice: the smallest step along any axis between consecutive points. 0 when it cannot be told.
    /// </summary>
    public static float FindSpacing(ReadOnlySpan<Point> points)
    {
        var spacing = float.MaxValue;
        for (var index = 1; index < points.Length; index++)
        {
            var step = Vector3.Abs(points[index].Position - points[index - 1].Position);
            Consider(step.X, ref spacing);
            Consider(step.Y, ref spacing);
            Consider(step.Z, ref spacing);
        }

        return spacing == float.MaxValue ? 0f : spacing;

        static void Consider(float step, ref float best)
        {
            if (float.IsFinite(step) && step > 1e-5f && step < best)
                best = step;
        }
    }

    /// <summary>
    /// One voxel per point, holding the point's F1 (or F2). Points are placed by position, so their order does not
    /// matter; samples that are not finite are left out, which leaves a hole instead of a spike in the surface.
    /// </summary>
    public static VdbGrid<float>? ToGrid(ReadOnlySpan<Point> points, float voxelSize, bool useF2 = false)
    {
        if (points.Length < 8)
            return null;

        if (!(voxelSize > 1e-5f))
            voxelSize = FindSpacing(points);

        if (!(voxelSize > 1e-5f))
            return null;

        // The first point sits on a voxel centre, so every other lattice point does too.
        var origin = Vector3.Zero;
        var hasOrigin = false;
        foreach (ref readonly var point in points)
        {
            if (!float.IsFinite(point.Position.X + point.Position.Y + point.Position.Z))
                continue;

            origin = point.Position;
            hasOrigin = true;
            break;
        }

        if (!hasOrigin)
            return null;

        // Far outside the lattice counts as outside the shape.
        var grid = new VdbGrid<float>(new VdbTree<float>(voxelSize * 4f), new VdbTransform(voxelSize, origin), "LS", VdbGridClass.LevelSet);
        foreach (ref readonly var point in points)
        {
            var value = useF2 ? point.F2 : point.F1;
            var offset = (point.Position - origin) / voxelSize;
            if (!float.IsFinite(value) || !float.IsFinite(offset.X + offset.Y + offset.Z) || MathF.Abs(offset.X) > 1e6f || MathF.Abs(offset.Y) > 1e6f || MathF.Abs(offset.Z) > 1e6f)
                continue;

            grid.Tree.SetValueOn(new VdbCoord((int)MathF.Round(offset.X), (int)MathF.Round(offset.Y), (int)MathF.Round(offset.Z)), value);
        }

        return grid;
    }
}
