#nullable enable
using System;
using System.Numerics;

namespace T3.Core.DataTypes.Volume.Tools;

/// <summary>
/// Trilinear sampler over a float grid in world space - the bridge that lets TiXL's existing
/// <c>ScalarField</c>/<c>VectorField</c> consumers (geometry displacement, scatter density,
/// custom fields) read volumes without knowing about the tree.
/// </summary>
public sealed class VdbSampler
{
    private readonly VdbGrid<float> _grid;

    public VdbSampler(VdbGrid<float> grid) => _grid = grid;

    public float SampleWorld(Vector3 world)
    {
        var index = _grid.Transform.WorldToIndexf(world);
        return SampleIndex(index);
    }

    public float SampleIndex(Vector3 index)
    {
        var x0 = (int)MathF.Floor(index.X);
        var y0 = (int)MathF.Floor(index.Y);
        var z0 = (int)MathF.Floor(index.Z);
        var tx = index.X - x0;
        var ty = index.Y - y0;
        var tz = index.Z - z0;

        var c000 = _grid.Tree.GetValue(new VdbCoord(x0, y0, z0));
        var c100 = _grid.Tree.GetValue(new VdbCoord(x0 + 1, y0, z0));
        var c010 = _grid.Tree.GetValue(new VdbCoord(x0, y0 + 1, z0));
        var c110 = _grid.Tree.GetValue(new VdbCoord(x0 + 1, y0 + 1, z0));
        var c001 = _grid.Tree.GetValue(new VdbCoord(x0, y0, z0 + 1));
        var c101 = _grid.Tree.GetValue(new VdbCoord(x0 + 1, y0, z0 + 1));
        var c011 = _grid.Tree.GetValue(new VdbCoord(x0, y0 + 1, z0 + 1));
        var c111 = _grid.Tree.GetValue(new VdbCoord(x0 + 1, y0 + 1, z0 + 1));

        var c00 = c000 + (c100 - c000) * tx;
        var c10 = c010 + (c110 - c010) * tx;
        var c01 = c001 + (c101 - c001) * tx;
        var c11 = c011 + (c111 - c011) * tx;

        var c0 = c00 + (c10 - c00) * ty;
        var c1 = c01 + (c11 - c01) * ty;

        return c0 + (c1 - c0) * tz;
    }
}

/// <summary>
/// Vectors from a volume in world space, for <c>VectorField</c> consumers such as forces. A vector grid
/// (velocity) is sampled trilinearly; a float grid gives its gradient instead, which for a level set is the
/// direction away from the surface.
/// </summary>
public sealed class VdbVectorSampler
{
    private readonly VdbGrid<Vector3>? _vectors;
    private readonly VdbSampler? _scalars;
    private readonly float _step;

    public VdbVectorSampler(VdbGrid<Vector3> grid) => _vectors = grid;

    public VdbVectorSampler(VdbGrid<float> grid)
    {
        _scalars = new VdbSampler(grid);

        // Half a voxel either side: the finest difference the trilinear samples can resolve.
        _step = grid.Transform.VoxelSize * 0.5f;
    }

    public Vector3 SampleWorld(Vector3 world)
    {
        if (_vectors != null)
            return SampleIndex(_vectors, _vectors.Transform.WorldToIndexf(world));

        var scalars = _scalars!;
        var dx = scalars.SampleWorld(world + new Vector3(_step, 0, 0)) - scalars.SampleWorld(world - new Vector3(_step, 0, 0));
        var dy = scalars.SampleWorld(world + new Vector3(0, _step, 0)) - scalars.SampleWorld(world - new Vector3(0, _step, 0));
        var dz = scalars.SampleWorld(world + new Vector3(0, 0, _step)) - scalars.SampleWorld(world - new Vector3(0, 0, _step));
        return new Vector3(dx, dy, dz) / (2f * _step);
    }

    private static Vector3 SampleIndex(VdbGrid<Vector3> grid, Vector3 index)
    {
        var x0 = (int)MathF.Floor(index.X);
        var y0 = (int)MathF.Floor(index.Y);
        var z0 = (int)MathF.Floor(index.Z);
        var tx = index.X - x0;
        var ty = index.Y - y0;
        var tz = index.Z - z0;

        var c00 = Vector3.Lerp(grid.Tree.GetValue(new VdbCoord(x0, y0, z0)), grid.Tree.GetValue(new VdbCoord(x0 + 1, y0, z0)), tx);
        var c10 = Vector3.Lerp(grid.Tree.GetValue(new VdbCoord(x0, y0 + 1, z0)), grid.Tree.GetValue(new VdbCoord(x0 + 1, y0 + 1, z0)), tx);
        var c01 = Vector3.Lerp(grid.Tree.GetValue(new VdbCoord(x0, y0, z0 + 1)), grid.Tree.GetValue(new VdbCoord(x0 + 1, y0, z0 + 1)), tx);
        var c11 = Vector3.Lerp(grid.Tree.GetValue(new VdbCoord(x0, y0 + 1, z0 + 1)), grid.Tree.GetValue(new VdbCoord(x0 + 1, y0 + 1, z0 + 1)), tx);
        return Vector3.Lerp(Vector3.Lerp(c00, c10, ty), Vector3.Lerp(c01, c11, ty), tz);
    }
}

/// <summary>
/// Dense extraction and resampling. <see cref="ExtractDense"/> fills an RGBA-normalized
/// [x + y*sizeX + z*sizeX*sizeY] float buffer for Texture3D upload; <see cref="Resample"/>
/// changes voxel size via trilinear sampling.
/// </summary>
public static class VolumeDense
{
    /// <summary>
    /// Upper bound for one dense bake: 64M floats = 256 MB on the CPU and on the GPU. A larger
    /// request is shrunk proportionally instead of overflowing the array length or exhausting memory.
    /// </summary>
    public const long MaxDenseVoxels = 64L << 20;

    /// <summary>
    /// Upper bound for a resample result (~512 MB of leaf data). Beyond it the operator reports the
    /// requested voxel size instead of exhausting memory.
    /// </summary>
    public const long MaxResampleLeaves = 262_144;

    /// <summary>
    /// Samples the grid's active index bbox into a dense float buffer (linear, clamped outside).
    /// Returns null when the grid has no active voxels.
    /// </summary>
    public static DenseVolume ExtractDense(VdbGrid<float> grid, int maxSize = 512)
    {
        var bbox = grid.ActiveIndexBBox;
        if (bbox.IsEmpty)
            return null!;

        var srcExtentX = bbox.Max.X - bbox.Min.X + 2;
        var srcExtentY = bbox.Max.Y - bbox.Min.Y + 2;
        var srcExtentZ = bbox.Max.Z - bbox.Min.Z + 2;

        // An axis beyond maxSize is downsampled proportionally instead of being cropped to the
        // lower corner: the bake must cover the whole volume or the texture path renders a
        // fragment of it with bounds that claim otherwise.
        var sizeX = Math.Min(srcExtentX, maxSize);
        var sizeY = Math.Min(srcExtentY, maxSize);
        var sizeZ = Math.Min(srcExtentZ, maxSize);
        var stepX = (float)srcExtentX / sizeX;
        var stepY = (float)srcExtentY / sizeY;
        var stepZ = (float)srcExtentZ / sizeZ;

        // Three individually clamped axes can still multiply past int.MaxValue (2048^3 is 8.6e9),
        // which would overflow the array length. Shrink all axes proportionally to the budget.
        var totalVoxels = (long)sizeX * sizeY * sizeZ;
        if (totalVoxels > MaxDenseVoxels)
        {
            var scale = MathF.Cbrt(MaxDenseVoxels / (float)totalVoxels);
            sizeX = Math.Max(1, (int)(sizeX * scale));
            sizeY = Math.Max(1, (int)(sizeY * scale));
            sizeZ = Math.Max(1, (int)(sizeZ * scale));
        }

        var data = new float[sizeX * sizeY * sizeZ];
        for (var z = 0; z < sizeZ; z++)
        {
            var srcZ = bbox.Min.Z + (int)(z * stepZ);
            for (var y = 0; y < sizeY; y++)
            {
                var srcY = bbox.Min.Y + (int)(y * stepY);
                for (var x = 0; x < sizeX; x++)
                {
                    var srcX = bbox.Min.X + (int)(x * stepX);
                    data[x + y * sizeX + z * sizeX * sizeY] = grid.Tree.GetValue(new VdbCoord(srcX, srcY, srcZ));
                }
            }
        }

        return new DenseVolume(data, sizeX, sizeY, sizeZ, bbox.Min, grid.Transform.VoxelSize, grid.Background,
                               new Vector3(stepX, stepY, stepZ));
    }

    /// <summary>Changes voxel size by trilinear resampling of the source grid.</summary>
    public static VdbGrid<float> Resample(VdbGrid<float> source, float newVoxelSize)
    {
        newVoxelSize = Math.Clamp(newVoxelSize, 1e-5f, 1e4f);
        if (MathF.Abs(newVoxelSize - source.Transform.VoxelSize) < 1e-7f)
            return (VdbGrid<float>)source.CloneGrid();

        var worldBBox = source.ActiveWorldBBox();
        if (worldBBox.IsEmpty)
            return new VdbGrid<float>(source.Background, newVoxelSize, source.Name, source.GridClass);

        var sampler = new VdbSampler(source);
        var target = new VdbGrid<float>(source.Background, newVoxelSize, source.Name, source.GridClass)
        {
            Transform = source.Transform.WithVoxelSize(newVoxelSize),
        };

        // Level sets carry a band of exact distances around the surface; outside it only the sign
        // matters. Fog volumes hold density only near their active region and have no band (their
        // background is 0), so they just need a small margin.
        var isLevelSet = source.GridClass == VdbGridClass.LevelSet;
        var bandWorld = isLevelSet ? MathF.Abs(source.Background) : 0f;
        var marginWorld = isLevelSet ? bandWorld : source.Transform.VoxelSize * 2f;

        // A tiny voxel size over a large volume would allocate billions of voxels and freeze the
        // editor. Check the leaf count in double precision before converting to index space.
        var extent = (worldBBox.Max - worldBBox.Min) + new Vector3(2f * marginWorld);
        var leafCount = ((double)extent.X / 8 / newVoxelSize + 2)
                        * ((double)extent.Y / 8 / newVoxelSize + 2)
                        * ((double)extent.Z / 8 / newVoxelSize + 2);
        if (leafCount > MaxResampleLeaves)
        {
            throw new ArgumentException($"Resampling to voxel size {newVoxelSize} would need about {(long)leafCount} leaves "
                                        + $"(limit {MaxResampleLeaves}); use a larger voxel size.");
        }

        // Bounds must be expressed in the *target* index space. The source's indices describe a
        // different world region once the voxel size changes; reusing them clipped finer resamples
        // to a fraction of the volume.
        var min = target.Transform.WorldToIndex(worldBBox.Min - new Vector3(marginWorld));
        var max = target.Transform.WorldToIndex(worldBBox.Max + new Vector3(marginWorld));

        var leafMin = new VdbCoord(min.X & ~7, min.Y & ~7, min.Z & ~7);
        var leafMax = new VdbCoord(max.X & ~7, max.Y & ~7, max.Z & ~7);

        for (var lz = leafMin.Z; lz <= leafMax.Z; lz += 8)
        {
            for (var ly = leafMin.Y; ly <= leafMax.Y; ly += 8)
            {
                for (var lx = leafMin.X; lx <= leafMax.X; lx += 8)
                {
                    var leafOrigin = new VdbCoord(lx, ly, lz);
                    var values = new float[VdbLeafNode<float>.Size];
                    var active = new bool[VdbLeafNode<float>.Size];
                    var anyActive = false;
                    var anyInterior = false;

                    for (var i = 0; i < VdbLeafNode<float>.Size; i++)
                    {
                        var coord = leafOrigin + VdbCoord.LocalFromOffset(i);
                        var world = target.Transform.IndexToWorld(coord);

                        // Sample the source trilinearly at the world position, so the surface keeps
                        // its sub-voxel placement instead of moving to the nearest source voxel.
                        var value = sampler.SampleWorld(world);

                        if (isLevelSet)
                        {
                            if (MathF.Abs(value) > bandWorld)
                            {
                                // Beyond the band only the sign survives, as +-background, so a solid's
                                // interior stays inside.
                                values[i] = value < 0 ? -source.Background : source.Background;
                                anyInterior |= value < 0;
                                continue;
                            }
                        }
                        else if (value == source.Background)
                        {
                            // Fog: background density stays inactive instead of allocating empty leaves.
                            continue;
                        }

                        values[i] = value;
                        active[i] = true;
                        anyActive = true;
                    }

                    if (!anyActive)
                    {
                        // A fully-interior leaf of a level set reads as outside unless the tree keeps
                        // it: its samples are all the negative background, so add it inactive and let
                        // GetValue return the stored (negative) values. An all-outside leaf is dropped.
                        if (!anyInterior)
                            continue;

                        var solidLeaf = target.Tree.AddLeaf(leafOrigin);
                        Array.Copy(values, solidLeaf.Values, values.Length);
                        continue;
                    }

                    var leaf = target.Tree.AddLeaf(leafOrigin);
                    Array.Copy(values, leaf.Values, values.Length);
                    for (var i = 0; i < active.Length; i++)
                    {
                        if (active[i])
                            leaf.ValueMask.SetOn(i);
                    }
                }
            }
        }

        target.Tree.Prune();
        return target;
    }
}

/// <summary>A dense (x-fastest) float block extracted from a grid, with its index-space origin.</summary>
public sealed class DenseVolume
{
    public DenseVolume(float[] data, int sizeX, int sizeY, int sizeZ, VdbCoord origin, float voxelSize, float background,
                       Vector3? indexStep = null)
    {
        Data = data;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        Origin = origin;
        VoxelSize = voxelSize;
        Background = background;
        IndexStep = indexStep ?? Vector3.One;
    }

    public float[] Data { get; }
    public int SizeX { get; }
    public int SizeY { get; }
    public int SizeZ { get; }
    public VdbCoord Origin { get; }

    /// <summary>The per-voxel spacing of the bake in SOURCE index units: 1 when the bake sampled
    /// one-to-one, larger when an axis was downsampled to fit MaxDenseVoxels/a resolution cap.</summary>
    public Vector3 IndexStep { get; }

    public float VoxelSize { get; }
    public float Background { get; }

    public float GetValue(int x, int y, int z) => Data[x + y * SizeX + z * SizeX * SizeY];

    public long VoxelCount => (long)SizeX * SizeY * SizeZ;
}

/// <summary>
/// Integrity diagnostics for a volume (OpenVDB tools/Diagnostics): NaN/Inf counting, active
/// voxel stats and memory usage - what [VolumeInfo] reports into the graph.
/// </summary>
public static class VolumeDiagnose
{
    public sealed class Report
    {
        public long ActiveVoxels;
        public long AllocatedVoxels;
        public int LeafCount;
        public int NanCount;
        public int InfCount;
        public float MinValue = float.MaxValue;
        public float MaxValue = float.MinValue;
        public double Average;
        public long MemoryBytes;
        public VdbCoordBBox ActiveBBox = VdbCoordBBox.Empty;
        public BBox WorldBBox = new(Vector3.Zero, Vector3.Zero);
        public bool HasErrors => NanCount > 0 || InfCount > 0;
    }

    public static Report Check(VdbGrid<float> grid)
    {
        var report = new Report
        {
            ActiveVoxels = grid.ActiveVoxelCount,
            LeafCount = grid.Tree.LeafCount,
            MemoryBytes = grid.AllocatedMemoryBytes,
            ActiveBBox = grid.ActiveIndexBBox,
            WorldBBox = grid.ActiveWorldBBox(),
        };

        var sum = 0.0;
        var count = 0L;
        foreach (var leaf in grid.Tree.Leaves)
        {
            report.AllocatedVoxels += VdbLeafNode<float>.Size;
            foreach (var i in leaf.ValueMask.OnBits())
            {
                var v = leaf.Values[i];
                if (float.IsNaN(v))
                {
                    report.NanCount++;
                    continue;
                }

                if (float.IsInfinity(v))
                {
                    report.InfCount++;
                    continue;
                }

                if (v < report.MinValue)
                    report.MinValue = v;
                if (v > report.MaxValue)
                    report.MaxValue = v;
                sum += v;
                count++;
            }
        }

        report.Average = count > 0 ? sum / count : 0;
        if (count == 0)
        {
            report.MinValue = 0;
            report.MaxValue = 0;
        }

        return report;
    }
}
