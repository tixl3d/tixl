#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace T3.Core.DataTypes.Volume.Tools;

/// <summary>
/// Splatting point clouds into density volumes (OpenVDB's ParticlesToLevelSet/PointAdvect
/// territory): each point contributes a normalized falloff kernel (gaussian or smoothstep) over
/// its radius. Points are grouped per leaf so parallel workers never share mask words.
/// </summary>
public static class PointsToVolume
{
    public enum Falloff
    {
        Gaussian,
        Smoothstep,
        Constant,
    }

    /// <summary>
    /// Radius in voxels is bounded: the splat cost grows with its cube and touches every leaf in
    /// the kernel box, so a large world radius on a fine grid would allocate millions of leaves.
    /// </summary>
    public const int MaxRadiusVoxels = 64;

    public static VdbGrid<float> Splat(Vector3[] positions, float[]? weights,
                                       float voxelSize, float radius, float density,
                                       Falloff falloff, int seed)
    {
        radius = MathF.Max(radius, 1e-5f);
        voxelSize = MathF.Max(voxelSize, 1e-5f);
        var radiusVoxels = (int)MathF.Ceiling(radius / voxelSize);
        if (radiusVoxels > MaxRadiusVoxels)
        {
            throw new ArgumentException($"Splat radius is {radiusVoxels} voxels (limit {MaxRadiusVoxels}); "
                                        + "increase VoxelSize or reduce Radius.");
        }
        var transform = new VdbTransform(voxelSize, Vector3.Zero);
        var radiusSq = radius * radius;

        var grid = new VdbGrid<float>(0f, voxelSize, "density", VdbGridClass.FogVolume);

        // Group point indices by every leaf their splat touches (deterministic + race-free parallelism).
        var leafGroups = new Dictionary<VdbCoord, List<int>>();
        for (var i = 0; i < positions.Length; i++)
        {
            // A NaN position (a body that blew up) would floor to int.MinValue and loop over the whole index range.
            if (!float.IsFinite(positions[i].X + positions[i].Y + positions[i].Z))
                continue;

            var center = transform.WorldToIndexf(positions[i]);
            var minCoord = new VdbCoord((int)MathF.Floor(center.X - radiusVoxels), (int)MathF.Floor(center.Y - radiusVoxels), (int)MathF.Floor(center.Z - radiusVoxels));
            var maxCoord = new VdbCoord((int)MathF.Floor(center.X + radiusVoxels), (int)MathF.Floor(center.Y + radiusVoxels), (int)MathF.Floor(center.Z + radiusVoxels));
            var leafMin = new VdbCoord(minCoord.X & ~7, minCoord.Y & ~7, minCoord.Z & ~7);
            var leafMax = new VdbCoord(maxCoord.X & ~7, maxCoord.Y & ~7, maxCoord.Z & ~7);
            for (var lz = leafMin.Z; lz <= leafMax.Z; lz += 8)
            {
                for (var ly = leafMin.Y; ly <= leafMax.Y; ly += 8)
                {
                    for (var lx = leafMin.X; lx <= leafMax.X; lx += 8)
                    {
                        var leafOrigin = new VdbCoord(lx, ly, lz);
                        if (!leafGroups.TryGetValue(leafOrigin, out var list))
                            leafGroups[leafOrigin] = list = new List<int>();
                        list.Add(i);
                    }
                }
            }
        }

        // Allocate leaves serially (tree is not thread-safe), then splat in parallel.
        var leafList = new List<VdbCoord>(leafGroups.Keys);
        var leafNodes = new List<VdbLeafNode<float>>(leafList.Count);
        foreach (var origin in leafList)
            leafNodes.Add(grid.Tree.AddLeaf(origin));

        Parallel.For(0, leafList.Count, VolumeParallelism.Options, leafIndex =>
        {
            var group = new KeyValuePair<VdbCoord, List<int>>(leafList[leafIndex], leafGroups[leafList[leafIndex]]);
            var values = new float[VdbLeafNode<float>.Size];
            var active = new bool[VdbLeafNode<float>.Size];
            var any = false;

            foreach (var pointIndex in group.Value)
            {
                var center = transform.WorldToIndexf(positions[pointIndex]);
                var weight = weights != null ? weights[pointIndex] : 1f;

                // Only the part of the kernel box inside this leaf: the full box is up to (2r+1)^3 voxels,
                // a leaf holds 512, and every point is visited once per leaf its kernel touches.
                var minX = Math.Max((int)MathF.Floor(center.X - radiusVoxels), group.Key.X);
                var minY = Math.Max((int)MathF.Floor(center.Y - radiusVoxels), group.Key.Y);
                var minZ = Math.Max((int)MathF.Floor(center.Z - radiusVoxels), group.Key.Z);
                var maxX = Math.Min((int)MathF.Floor(center.X + radiusVoxels), group.Key.X + 7);
                var maxY = Math.Min((int)MathF.Floor(center.Y + radiusVoxels), group.Key.Y + 7);
                var maxZ = Math.Min((int)MathF.Floor(center.Z + radiusVoxels), group.Key.Z + 7);
                var position = positions[pointIndex];
                if (!float.IsFinite(position.X + position.Y + position.Z))
                    continue;

                for (var z = minZ; z <= maxZ; z++)
                {
                    for (var y = minY; y <= maxY; y++)
                    {
                        for (var x = minX; x <= maxX; x++)
                        {
                            var coord = new VdbCoord(x, y, z);
                            var world = transform.IndexToWorld(coord);
                            var distSq = Vector3.DistanceSquared(world, position);
                            if (distSq > radiusSq)
                                continue;

                            var dist = MathF.Sqrt(distSq);
                            var t = 1f - dist / radius;
                            var contribution = falloff switch
                            {
                                Falloff.Gaussian => MathF.Exp(-6f * distSq / radiusSq),
                                Falloff.Smoothstep => t * t * (3f - 2f * t),
                                Falloff.Constant => 1f,
                                _ => t,
                            };

                            var offset = coord.LeafOffset();
                            values[offset] += contribution * weight * density;
                            active[offset] = true;
                            any = true;
                        }
                    }
                }
            }

            if (!any)
                return;

            var leaf = leafNodes[leafIndex];
            for (var i = 0; i < VdbLeafNode<float>.Size; i++)
            {
                leaf.Values[i] = values[i];
                if (active[i])
                    leaf.ValueMask.SetOn(i);
            }
        });

        grid.Tree.Prune();
        return grid;
    }
}

/// <summary>
/// Analytic level-set primitives (sphere, box, torus, ...) rasterized straight into the tree,
/// the counterparts of OpenVDB's tools/LevelSetSphere etc.
/// </summary>
public static class VolumePrimitives
{
    public enum Shape
    {
        Sphere,
        Box,
        RoundedBox,
        Torus,
        Capsule,
        Cylinder,
        Cone,
        Octahedron,
        Plane,
    }

    public delegate float SdfFn(Vector3 localPoint);

    public static VdbGrid<float> Create(Shape shape, Vector3 size,
                                        float voxelSize, Vector3 translation, int halfWidthVoxels = 3)
    {
        var sdf = LocalSdf(shape, size);
        var extent = BoundingBoxOf(shape, size);
        // LocalSdf is centred on the origin, so the translation has to move the *sample point*:
        // evaluating the unshifted SDF and only shifting the index transform leaves the geometry
        // at the origin while the voxel grid moves away from it (Center would silently do nothing).
        return CreateSdf(p => sdf(p - translation), extent.Min + translation, extent.Max + translation,
                         voxelSize, translation, halfWidthVoxels);
    }

    /// <summary>World-space bounds of a primitive (pre-translation), for band rasterization. Each
    /// case must match the extent its <see cref="LocalSdf"/> actually evaluates — the box is the
    /// only region that gets rasterized, so a smaller box clips the shape.</summary>
    public static (Vector3 Min, Vector3 Max) BoundingBoxOf(Shape shape, Vector3 size)
    {
        var half = size * 0.5f;
        return shape switch
        {
            Shape.Sphere => new(new Vector3(-half.X), new Vector3(half.X)),
            Shape.Box => new(-half, half),
            Shape.RoundedBox => new(-half - new Vector3(0.1f * MathF.Min(size.X, MathF.Min(size.Y, size.Z))),
                                    half + new Vector3(0.1f * MathF.Min(size.X, MathF.Min(size.Y, size.Z)))),
            // The torus ring lies in the XY plane (see LocalSdf): the ring reaches ±(R+r) in X and
            // Y, the tube only ±r in Z. The old box swapped those, clipping the ring top/bottom.
            Shape.Torus => new(new Vector3(-(half.X + half.Y), -(half.X + half.Y), -half.Y),
                               new Vector3(half.X + half.Y, half.X + half.Y, half.Y)),
            // A capsule with a Y size below its diameter degenerates into a sphere of the tube
            // radius (LocalSdf clamps the segment to zero length), so Y must reach the tube radius.
            Shape.Capsule => new(new Vector3(-half.X, -MathF.Max(half.X, half.Y), -half.X),
                                 new Vector3(half.X, MathF.Max(half.X, half.Y), half.X)),
            Shape.Cylinder => new(new Vector3(-half.X, -half.Y, -half.X), new Vector3(half.X, half.Y, half.X)),
            Shape.Cone => new(new Vector3(-half.X, -half.Y, -half.X), new Vector3(half.X, half.Y, half.X)),
            // The octahedron SDF scales all axes by size.X (see LocalSdf); per-axis halves clip it.
            Shape.Octahedron => new(new Vector3(-size.X * 0.5f), new Vector3(size.X * 0.5f)),
            Shape.Plane => new(new Vector3(-half.X, -half.Y, -half.Z), new Vector3(half.X, half.Y, half.Z)),
            _ => new(-half, half),
        };
    }

    /// <summary>
    /// Rasterizes a world-space SDF into a narrow-band level set over the given world bounds.
    /// Voxels within the band around the zero crossing get exact values; the rest stays inactive
    /// background (the same topology OpenVDB's LevelSetSphere & co produce).
    /// </summary>
    public static VdbGrid<float> CreateSdf(SdfFn worldSdf, Vector3 worldMin, Vector3 worldMax,
                                           float voxelSize, Vector3 translation, int halfWidthVoxels = 3)
    {
        var t = new VdbTransform(voxelSize, translation);
        var band = Math.Clamp(halfWidthVoxels, 1, 64);
        var bandWorld = band * t.VoxelSize;
        var background = bandWorld;
        var grid = new VdbGrid<float>(background, t.VoxelSize, "LS", VdbGridClass.LevelSet)
        {
            Transform = t,
        };

        var indexMin = t.WorldToIndex(worldMin - new Vector3(bandWorld));
        var indexMax = t.WorldToIndex(worldMax + new Vector3(bandWorld));
        indexMin -= new VdbCoord(1);
        indexMax += new VdbCoord(1);

        var leafMin = new VdbCoord(indexMin.X & ~7, indexMin.Y & ~7, indexMin.Z & ~7);
        var leafMax = new VdbCoord(indexMax.X & ~7, indexMax.Y & ~7, indexMax.Z & ~7);

        var leafCountX = (leafMax.X - leafMin.X >> 3) + 1;
        var leafCountY = (leafMax.Y - leafMin.Y >> 3) + 1;
        var leafCountZ = (leafMax.Z - leafMin.Z >> 3) + 1;
        var leafCount = (long)leafCountX * leafCountY * leafCountZ;
        // 262k leaves is about 512 MB of voxel data; beyond that a typo in VoxelSize turns into an
        // out-of-memory crash instead of a warning.
        if (leafCount > 262_144)
            throw new ArgumentException($"Primitive too large for voxel size {voxelSize} ({leafCount} leaves, limit 262144)");

        // Leaves are rasterized in parallel into plain arrays (the SDF is a pure function), then added to the tree
        // serially in a fixed order because the tree is not thread-safe.
        var count = (int)leafCount;
        var leafValues = new float[count][];
        var leafActive = new ulong[count][];
        // Every SDF here is at most 1-Lipschitz, so a leaf whose centre is further outside than the band plus the
        // leaf's half diagonal has no voxel in the band and is skipped without evaluating its 512 voxels.
        var skipDistance = bandWorld + 0.5f * MathF.Sqrt(3f) * VdbLeafNode<float>.Dim * t.VoxelSize;
        Parallel.For(0, count, VolumeParallelism.Options, leafIndex =>
        {
            var lx = leafIndex % leafCountX;
            var ly = leafIndex / leafCountX % leafCountY;
            var lz = leafIndex / (leafCountX * leafCountY);
            var leafOrigin = new VdbCoord(leafMin.X + (lx << 3), leafMin.Y + (ly << 3), leafMin.Z + (lz << 3));
            var centre = t.IndexToWorld(leafOrigin.ToVector3() + new Vector3(3.5f));
            if (worldSdf(centre) > skipDistance)
                return;

            RasterizeLeaf(worldSdf, leafOrigin, t, bandWorld, background, out leafValues[leafIndex], out leafActive[leafIndex]);
        });

        for (var leafIndex = 0; leafIndex < count; leafIndex++)
        {
            if (leafActive[leafIndex] is not { } activeWords)
                continue;

            var lx = leafIndex % leafCountX;
            var ly = leafIndex / leafCountX % leafCountY;
            var lz = leafIndex / (leafCountX * leafCountY);
            var leaf = grid.Tree.AddLeaf(new VdbCoord(leafMin.X + (lx << 3), leafMin.Y + (ly << 3), leafMin.Z + (lz << 3)));
            Array.Copy(leafValues[leafIndex], leaf.Values, VdbLeafNode<float>.Size);
            activeWords.AsSpan().CopyTo(leaf.ValueMask.Words);
        }

        grid.Tree.Prune();
        return grid;
    }

    private static void RasterizeLeaf(SdfFn worldSdf, VdbCoord leafOrigin, VdbTransform t, float bandWorld, float background,
                                      out float[] values, out ulong[] activeWords)
    {
        // Voxels outside the band keep the background value, so inactive reads stay consistent.
        values = null!;
        activeWords = null!;
        for (var i = 0; i < VdbLeafNode<float>.Size; i++)
        {
            var local = VdbCoord.LocalFromOffset(i);
            var world = t.IndexToWorld(leafOrigin + local);
            var d = worldSdf(world);
            if (d > bandWorld)
                continue;

            if (values == null)
            {
                values = new float[VdbLeafNode<float>.Size];
                Array.Fill(values, background);
                activeWords = new ulong[VdbLeafNode<float>.Size / 64];
            }

            values[i] = d;
            activeWords[i >> 6] |= 1ul << (i & 63);
        }
    }

    /// <summary>Local-space (pre-translation) SDF for each primitive shape.</summary>
    public static SdfFn LocalSdf(Shape shape, Vector3 size) => shape switch
    {
        Shape.Sphere => p => p.Length() - size.X * 0.5f,
        Shape.Box => p => SdBox(p, size * 0.5f),
        Shape.RoundedBox => p => SdBox(p, size * 0.5f) - MathF.Min(size.X, MathF.Min(size.Y, size.Z)) * 0.1f,
        Shape.Torus => p =>
        {
            var q = new Vector2(new Vector2(p.X, p.Y).Length() - size.X * 0.5f, p.Z);
            return q.Length() - size.Y * 0.5f;
        },
        Shape.Capsule => p =>
        {
            var half = MathF.Max(size.Y * 0.5f - size.X * 0.5f, 0f);
            p.Y -= Math.Clamp(p.Y, -half, half);
            return p.Length() - size.X * 0.5f;
        },
        Shape.Cylinder => p =>
        {
            var d = new Vector2(new Vector2(p.X, p.Z).Length() - size.X * 0.5f, MathF.Abs(p.Y) - size.Y * 0.5f);
            return MathF.Min(MathF.Max(d.X, d.Y), 0f) + new Vector2(MathF.Max(d.X, 0f), MathF.Max(d.Y, 0f)).Length();
        },
        Shape.Cone => p =>
        {
            // Capped cone centered on Y (Inigo Quilez): base radius at y=-h/2, apex at y=+h/2.
            var h = MathF.Max(size.Y * 0.5f, 1e-4f);
            var r1 = MathF.Max(size.X * 0.5f, 1e-4f);  // bottom radius
            var r2 = 0f;                                // top radius (apex)
            var q = new Vector2(new Vector2(p.X, p.Z).Length(), p.Y);
            var k1 = new Vector2(r2, h);
            var k2 = new Vector2(r2 - r1, 2f * h);
            var ca = new Vector2(q.X - MathF.Min(q.X, q.Y < 0f ? r1 : r2), MathF.Abs(q.Y) - h);
            var cb = q - k1 + k2 * Math.Clamp(Vector2.Dot(k1 - q, k2) / Vector2.Dot(k2, k2), 0f, 1f);
            var sign = cb.X < 0f && ca.Y < 0f ? -1f : 1f;
            return sign * MathF.Sqrt(MathF.Min(Vector2.Dot(ca, ca), Vector2.Dot(cb, cb)));
        },
        Shape.Octahedron => p =>
        {
            var s = size.X * 0.5f;
            var ap = Vector3.Abs(p);
            return (ap.X + ap.Y + ap.Z - s) * 0.57735027f;
        },
        Shape.Plane => p => p.Y + size.Y * 0.5f,
        _ => p => p.Length() - size.X * 0.5f,
    };

    private static float SdBox(Vector3 p, Vector3 halfSize)
    {
        var q = Vector3.Abs(p) - halfSize;
        var outside = Vector3.Max(q, Vector3.Zero).Length();
        return outside + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0f);
    }
}

/// <summary>
/// Scalar grid arithmetic (OpenVDB tools::csg/comp decorators): two-grid operations, constant
/// operations, and unary function application, all producing fresh grids.
/// </summary>
public static class VolumeMath
{
    public enum BinaryOp
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Min,
        Max,
    }

    public static VdbGrid<float> Combine(VdbGrid<float> a, VdbGrid<float> b, BinaryOp op)
    {
        var result = new VdbGrid<float>(Apply(op, a.Background, b.Background), a.Transform.VoxelSize, a.Name, a.GridClass)
        {
            Transform = a.Transform,
        };

        var sameSpace = a.Transform.VoxelSize == b.Transform.VoxelSize
                        && a.Transform.Translation == b.Transform.Translation;

        var leafCoords = new SortedSet<VdbCoord>();
        foreach (var leaf in a.Tree.Leaves)
            leafCoords.Add(leaf.Origin);
        if (sameSpace)
        {
            foreach (var leaf in b.Tree.Leaves)
                leafCoords.Add(leaf.Origin);
        }
        else
        {
            VolumeCsg.CollectLeafCoordsMapped(b, a.Transform, leafCoords);
        }

        VolumeCsg.CombineInto(result, a, b, leafCoords, (va, vb) => Apply(op, va, vb));
        return result;
    }

    public static VdbGrid<float> Constant(VdbGrid<float> a, float constant, BinaryOp op)
    {
        var result = (VdbGrid<float>)a.CloneGrid();
        // Includes the tiles that pruning collapsed uniform leaves into.
        result.Tree.TransformValues(v => Apply(op, v, constant));
        return result;
    }

    public static VdbGrid<float> ApplyFunction(VdbGrid<float> a, Func<float, float> function, float background = float.NaN)
    {
        var result = (VdbGrid<float>)a.CloneGrid();
        var bg = float.IsNaN(background) ? function(a.Background) : background;
        result.Tree.Root.Background = bg;
        result.Tree.TransformValues(function);
        return result;
    }

    private static float Apply(BinaryOp op, float a, float b) => op switch
    {
        BinaryOp.Add => a + b,
        BinaryOp.Subtract => a - b,
        BinaryOp.Multiply => a * b,
        BinaryOp.Divide => MathF.Abs(b) < 1e-12f ? 0f : a / b,
        BinaryOp.Min => MathF.Min(a, b),
        BinaryOp.Max => MathF.Max(a, b),
        _ => a,
    };
}

/// <summary>
/// Fractal noise application to a float grid: add, multiply, or displace (level-set-safe
/// displacement) using <see cref="VdbNoise"/>.
/// </summary>
public static class VolumeNoise
{
    public enum Mode
    {
        Add,
        Multiply,
        Displace,
        Replace,
    }

    public static VdbGrid<float> Apply(VdbGrid<float> input, VdbNoise noise,
                                       float frequency, float amplitude, Vector3 offset,
                                       int octaves, VdbNoiseType noiseType, VdbNoiseFractalType fractal,
                                       Mode mode)
    {
        var result = new VdbGrid<float>(mode == Mode.Replace ? 0f : input.Background, input.Transform.VoxelSize, input.Name, input.GridClass)
        {
            Transform = input.Transform,
        };

        // A voxel's world position is translation + index * voxelSize, so the noise input
        // p = world * (frequency / voxelSize) + offset is affine in the index:
        // p = translation * (frequency / voxelSize) + offset + index * frequency.
        // Stepping p by frequency per voxel instead of transforming each voxel removes a matrix
        // multiply per sample - the bulk of the cost when an animated noise rebuilds a 24k-voxel
        // band every frame.
        var voxelSize = input.Transform.VoxelSize;
        var baseP = input.Transform.Translation * (frequency / voxelSize) + offset;
        var displacementScale = mode == Mode.Displace ? amplitude * voxelSize : amplitude;
        var isReplace = mode == Mode.Replace;

        // Leaves are allocated serially (the tree is not thread-safe), then filled in parallel: the noise is a pure
        // function of the position, so the result is the same on any number of threads.
        var sources = new List<VdbLeafNode<float>>(input.Tree.Leaves);
        var targets = new VdbLeafNode<float>[sources.Count];
        for (var leafIndex = 0; leafIndex < sources.Count; leafIndex++)
            targets[leafIndex] = result.Tree.AddLeaf(sources[leafIndex].Origin);

        Parallel.For(0, sources.Count, VolumeParallelism.Options, leafIndex =>
        {
            var leaf = sources[leafIndex];
            var origin = leaf.Origin;
            var newLeaf = targets[leafIndex];
            var values = newLeaf.Values;

            // Inactive voxels keep their values: for a level set they carry the interior fill.
            if (!isReplace)
                Array.Copy(leaf.Values, values, VdbLeafNode<float>.Size);

            var words = leaf.ValueMask.Words;
            for (var x = 0; x < 8; x++)
            {
                var word = isReplace ? ulong.MaxValue : words[x];
                if (word == 0)
                    continue;

                var px = baseP.X + (origin.X + x) * frequency;
                for (var y = 0; y < 8; y++)
                {
                    // Leaf offsets are x * 64 + y * 8 + z, so one mask word holds the
                    // 8 z-voxels of an (x, y) row.
                    var bits = (uint)((word >> (y * 8)) & 0xFF);
                    if (bits == 0)
                        continue;

                    var py = baseP.Y + (origin.Y + y) * frequency;
                    var rowOffset = (x << 6) + (y << 3);
                    for (var z = 0; z < 8; z++)
                    {
                        if ((bits & (1u << z)) == 0)
                            continue;

                        var pz = baseP.Z + (origin.Z + z) * frequency;
                        var n = noise.Fractal(noiseType, fractal, px, py, pz, octaves);
                        var i = rowOffset + z;
                        var v = leaf.Values[i];
                        values[i] = mode switch
                        {
                            Mode.Add => v + n * amplitude,
                            Mode.Multiply => v * (1f + n * amplitude),
                            Mode.Displace => v + n * displacementScale,
                            Mode.Replace => n * amplitude,
                            _ => v,
                        };
                    }
                }
            }

            if (isReplace)
                newLeaf.ValueMask.SetAll();
            else
                newLeaf.ValueMask.Words.AsSpan().CopyFrom(leaf.ValueMask.Words);
        });

        result.Tree.Prune();
        return result;
    }
}
