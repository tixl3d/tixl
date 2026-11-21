#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using Xunit;

namespace Core.Tests.Volume;

public class MeshToVolumeTests
{
    private const float Radius = 1.0f;
    private const float VoxelSize = 0.1f;

    private static VdbGrid<float> CreateSphereSdf()
    {
        var (positions, triangles) = TestMeshes.UvSphere(Radius, 32, 16);
        return MeshToVolume.ConvertCorners(positions, triangles, VoxelSize, 3, VdbVolumeType.Sdf, Vector3.Zero);
    }

    [Fact]
    public void SphereSdfHasCorrectSignAndMagnitude()
    {
        var grid = CreateSphereSdf();

        // Center reads the inactive interior fill (-background) in a narrow-band SDF.
        var centerValue = grid.Tree.GetValue(grid.Transform.WorldToIndex(Vector3.Zero));
        Assert.True(centerValue < 0, $"center value {centerValue} should be inside");

        // Just outside the surface (2 voxels): small positive.
        var outsidePoint = new Vector3(Radius + 2 * VoxelSize, 0, 0);
        var outsideValue = grid.Tree.GetValue(grid.Transform.WorldToIndex(outsidePoint));
        Assert.True(outsideValue > 0, $"outside value {outsideValue} should be positive");
        Assert.True(outsideValue < 4 * VoxelSize, $"outside value {outsideValue} too far from surface");

        // Just inside the surface: small negative.
        var insidePoint = new Vector3(Radius - 2 * VoxelSize, 0, 0);
        var insideValue = grid.Tree.GetValue(grid.Transform.WorldToIndex(insidePoint));
        Assert.True(insideValue < 0, $"inside value {insideValue} should be negative");

        // Zero crossing between them.
        Assert.True(MathF.Abs(grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(Radius, 0, 0)))) <= 1.5f * VoxelSize);
    }

    [Fact]
    public void SphereSdfIsClassifiedAsLevelSet()
    {
        var grid = CreateSphereSdf();
        Assert.Equal(VdbGridClass.LevelSet, grid.GridClass);
        Assert.True(grid.ActiveVoxelCount > 0);
    }

    [Fact]
    public void FogVolumeIsDensestAtSurface()
    {
        var (positions, triangles) = TestMeshes.UvSphere(Radius, 32, 16);
        var grid = MeshToVolume.ConvertCorners(positions, triangles, VoxelSize, 3, VdbVolumeType.Fog, Vector3.Zero);

        var atSurface = grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(Radius, 0, 0)));
        var nearBandEdge = grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(Radius + 2.5f * VoxelSize, 0, 0)));

        Assert.InRange(atSurface, 0.5f, 1.01f);
        Assert.True(nearBandEdge < atSurface, "fog should fall off with distance");
        Assert.Equal(VdbGridClass.FogVolume, grid.GridClass);
    }
}

public class VolumeToMeshTests
{
    [Fact]
    public void SphereMeshIsWatertightAndSized()
    {
        var (positions, triangles) = TestMeshes.UvSphere(1f, 48, 24);
        var grid = MeshToVolume.ConvertCorners(positions, triangles, 0.08f, 3, VdbVolumeType.Sdf, Vector3.Zero);
        var mesh = VolumeToMesh.Extract(grid, 0f);

        Assert.True(mesh.FaceCount > 100, $"expected a real surface, got {mesh.FaceCount} faces");
        Assert.All(mesh.FaceCornerOffsets, offset => Assert.True(offset >= 0));

        // Watertight: every edge shared by exactly two triangles.
        var edgeCounts = new Dictionary<(int, int), int>();
        for (var f = 0; f < mesh.FaceCount; f++)
        {
            for (var c = 0; c < 3; c++)
            {
                var a = mesh.CornerPointIndices[mesh.FaceCornerOffsets[f] + c];
                var b = mesh.CornerPointIndices[mesh.FaceCornerOffsets[f] + (c + 1) % 3];
                var key = (Math.Min(a, b), Math.Max(a, b));
                edgeCounts[key] = edgeCounts.GetValueOrDefault(key) + 1;
            }
        }

        foreach (var (edge, count) in edgeCounts)
        {
            Assert.True(count == 2, $"edge {edge} used {count} times - mesh is not watertight");
        }

        // Vertices near the unit sphere.
        //
        // Narrow-band level sets store +/-background outside the band instead of the exact
        // distance, so cells straddling the band edge interpolate across truncated values.
        // The mesh stays watertight, but a few vertices can sit up to roughly one band
        // width away from the true surface. Widen BandWidth for a more exact surface.
        var maxError = 0f;
        foreach (var p in mesh.Positions)
        {
            maxError = MathF.Max(maxError, MathF.Abs(p.Length() - 1f));
        }

        Assert.True(maxError < 0.35f, $"vertex radius error {maxError} too large");

        // The bulk of the surface must still be tight: the median vertex is on the sphere.
        var errors = mesh.Positions.Select(p => MathF.Abs(p.Length() - 1f)).OrderBy(e => e).ToArray();
        var median = errors[errors.Length / 2];
        Assert.True(median < 0.05f, $"median vertex radius error {median} too large");

        // Corner normals present and roughly radial.
        var normals = mesh.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var normalAttr);
        Assert.True(normals);
        Assert.Equal(mesh.CornerCount, normalAttr.Values.Length);
    }
}

public class VolumeCsgTests
{
    private static VdbGrid<float> SphereAt(Vector3 center, float radius, float voxelSize = 0.1f)
    {
        var (positions, triangles) = TestMeshes.UvSphere(radius, 32, 16, center);
        return MeshToVolume.ConvertCorners(positions, triangles, voxelSize, 3, VdbVolumeType.Sdf, Vector3.Zero);
    }

    [Fact]
    public void UnionContainsBothSpheres()
    {
        var a = SphereAt(new Vector3(-0.6f, 0, 0), 1f);
        var b = SphereAt(new Vector3(0.6f, 0, 0), 1f);
        var union = VolumeCsg.Combine(a, b, VolumeCsg.Operation.Union);

        // Points inside either sphere must read negative.
        var insideA = union.Tree.GetValue(union.Transform.WorldToIndex(new Vector3(-1.2f, 0, 0)));
        var insideB = union.Tree.GetValue(union.Transform.WorldToIndex(new Vector3(1.2f, 0, 0)));
        var outside = union.Tree.GetValue(union.Transform.WorldToIndex(new Vector3(0, 1.2f, 0)));

        Assert.True(insideA < 0, $"inside A reads {insideA}");
        Assert.True(insideB < 0, $"inside B reads {insideB}");
        Assert.True(outside > 0, $"outside reads {outside}");
    }

    [Fact]
    public void IntersectionKeepsOnlyOverlap()
    {
        var a = SphereAt(new Vector3(-0.75f, 0, 0), 1f);
        var b = SphereAt(new Vector3(0.75f, 0, 0), 1f);
        var intersection = VolumeCsg.Combine(a, b, VolumeCsg.Operation.Intersection);

        // Overlap region around the origin must be inside.
        var inOverlap = intersection.Tree.GetValue(intersection.Transform.WorldToIndex(Vector3.Zero));
        Assert.True(inOverlap < 0, $"overlap center reads {inOverlap}");

        // A-only region must be outside.
        var aOnly = intersection.Tree.GetValue(intersection.Transform.WorldToIndex(new Vector3(-1.4f, 0, 0)));
        Assert.True(aOnly > 0, $"A-only region reads {aOnly}");
    }

    [Fact]
    public void DifferenceCarvesOut()
    {
        var a = SphereAt(Vector3.Zero, 1f);
        var b = SphereAt(Vector3.Zero, 0.5f);
        var difference = VolumeCsg.Combine(a, b, VolumeCsg.Operation.Difference);

        var inShell = difference.Tree.GetValue(difference.Transform.WorldToIndex(new Vector3(0.75f, 0, 0)));
        var inHole = difference.Tree.GetValue(difference.Transform.WorldToIndex(Vector3.Zero));

        Assert.True(inShell < 0, $"shell reads {inShell}");
        Assert.True(inHole > 0, $"carved center reads {inHole}");
    }
}

public class VolumeMorphologyTests
{
    private static VdbGrid<float> SphereAt(Vector3 center, float radius, float voxelSize = 0.1f)
    {
        var (positions, triangles) = TestMeshes.UvSphere(radius, 32, 16, center);
        return MeshToVolume.ConvertCorners(positions, triangles, voxelSize, 4, VdbVolumeType.Sdf, Vector3.Zero);
    }

    [Fact]
    public void DilateGrowsAndErodeShrinks()
    {
        var grid = SphereAt(Vector3.Zero, 1f);
        var dilated = VolumeMorphology.Apply(grid, VolumeMorphology.Operation.Dilate, 0.2f);
        var eroded = VolumeMorphology.Apply(grid, VolumeMorphology.Operation.Erode, 0.2f);

        // Sample along +X: surface moves outward for dilate, inward for erode.
        Assert.True(SurfaceRadius(dilated) > SurfaceRadius(grid));
        Assert.True(SurfaceRadius(eroded) < SurfaceRadius(grid));
    }

    private static float SurfaceRadius(VdbGrid<float> grid)
    {
        // March along +X until the sign flips.
        var previous = 1f;
        for (var x = 0f; x < 3f; x += grid.Transform.VoxelSize)
        {
            var v = grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(x, 0, 0)));
            if (previous < 0 && v >= 0)
                return x;
            previous = v;
        }

        return float.MaxValue;
    }

    [Fact]
    public void OpenThenCloseRoughlyRestoresSurface()
    {
        var grid = SphereAt(Vector3.Zero, 1f);
        var opened = VolumeMorphology.Apply(grid, VolumeMorphology.Operation.Open, 0.1f);
        var radius = SurfaceRadius(opened);
        Assert.InRange(radius, 0.7f, 1.3f);
    }
}

public class VolumeFilterTests
{
    [Fact]
    public void GaussianPreservesValueRange()
    {
        var (positions, triangles) = TestMeshes.UvSphere(1f, 32, 16);
        var grid = MeshToVolume.ConvertCorners(positions, triangles, 0.1f, 3, VdbVolumeType.Sdf, Vector3.Zero);

        var smoothed = VolumeFilter.Gaussian(grid, 1, 2);

        foreach (var leaf in smoothed.Tree.Leaves)
        {
            foreach (var i in leaf.ValueMask.OnBits())
            {
                Assert.True(MathF.Abs(leaf.Values[i]) <= 0.35f + 1e-4f,
                            $"smoothed value {leaf.Values[i]} out of band");
            }
        }

        Assert.True(smoothed.ActiveVoxelCount > 0);
    }

    [Fact]
    public void OffsetMovesSurface()
    {
        var (positions, triangles) = TestMeshes.UvSphere(1f, 32, 16);
        var grid = MeshToVolume.ConvertCorners(positions, triangles, 0.1f, 3, VdbVolumeType.Sdf, Vector3.Zero);

        var offset = VolumeFilter.Apply(grid, VolumeFilter.FilterType.Offset, 1, offset: 0.1f);
        var atOldSurface = offset.Tree.GetValue(offset.Transform.WorldToIndex(new Vector3(1.05f, 0, 0)));
        Assert.True(atOldSurface > 0, $"surface should have moved outward, reads {atOldSurface}");
    }
}

public class VolumeNoiseTests
{
    [Fact]
    public void NoiseIsDeterministicPerSeed()
    {
        var a = new VdbNoise(42).Perlin(1.3f, 2.7f, -4.2f);
        var b = new VdbNoise(42).Perlin(1.3f, 2.7f, -4.2f);
        var c = new VdbNoise(43).Perlin(1.3f, 2.7f, -4.2f);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.True(MathF.Abs(a) <= 1.01f);
    }

    [Fact]
    public void DisplaceModeShiftsLevelSetValues()
    {
        var (positions, triangles) = TestMeshes.UvSphere(1f, 32, 16);
        var grid = MeshToVolume.ConvertCorners(positions, triangles, 0.1f, 3, VdbVolumeType.Sdf, Vector3.Zero);
        var noise = new VdbNoise(7);

        var displaced = VolumeNoise.Apply(grid, noise, frequency: 3f, amplitude: 0.5f, Vector3.Zero,
                                          octaves: 2, VdbNoiseType.Perlin, VdbNoiseFractalType.Fbm,
                                          VolumeNoise.Mode.Displace);

        Assert.NotEqual(0, grid.ActiveVoxelCount);
        Assert.True(displaced.ActiveVoxelCount > 0);
    }
}

public class VolumeCalculusTests
{
    private static VdbGrid<float> RadialField()
    {
        // field(p) = p.X: gradient is (1,0,0) everywhere, divergence of the vector version is 1.
        var grid = new VdbGrid<float>(0f, 0.1f, "x", VdbGridClass.Unknown);
        for (var z = 4; z < 12; z++)
        {
            for (var y = 4; y < 12; y++)
            {
                for (var x = 4; x < 12; x++)
                {
                    grid.Tree.SetValueOn(new VdbCoord(x, y, z), x * 0.1f);
                }
            }
        }

        return grid;
    }

    [Fact]
    public void GradientOfLinearFieldIsConstant()
    {
        var field = RadialField();
        var gradient = VolumeCalculus.Gradient(field, scale: 1f / field.Transform.VoxelSize);

        var center = new VdbCoord(8, 8, 8);
        var g = gradient.Tree.GetValue(center);
        Assert.Equal(1f, g.X, 2);
        Assert.Equal(0f, g.Y, 2);
        Assert.Equal(0f, g.Z, 2);
    }

    [Fact]
    public void LaplacianOfLinearFieldIsZero()
    {
        var field = RadialField();
        var laplacian = VolumeCalculus.Laplacian(field);
        Assert.Equal(0f, laplacian.Tree.GetValue(new VdbCoord(8, 8, 8)), 3);
    }

    [Fact]
    public void CurlOfLinearVectorFieldIsZero()
    {
        var vectorGrid = new VdbGrid<Vector3>(Vector3.Zero, 0.1f, "v", VdbGridClass.Unknown);
        for (var z = 4; z < 12; z++)
        {
            for (var y = 4; y < 12; y++)
            {
                for (var x = 4; x < 12; x++)
                {
                    var p = new Vector3(x, y, z) * 0.1f;
                    vectorGrid.Tree.SetValueOn(new VdbCoord(x, y, z), p); // v = p is curl-free
                }
            }
        }

        var curl = VolumeCalculus.Curl(vectorGrid);
        var c = curl.Tree.GetValue(new VdbCoord(8, 8, 8));
        Assert.True(c.Length() < 1e-3f, $"curl {c} should vanish for v = p");
    }
}

public class PointsToVolumeTests
{
    [Fact]
    public void SinglePointCreatesPeak()
    {
        var grid = PointsToVolume.Splat([new Vector3(0.5f, 0.5f, 0.5f)], null, 0.1f, 0.3f, 1f,
                                        PointsToVolume.Falloff.Gaussian, 0);

        var peak = grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(0.5f, 0.5f, 0.5f)));
        // The nearest voxel center is ~0.087 away from the point, so the gaussian reads below 1.
        Assert.InRange(peak, 0.55f, 1.0001f);

        var far = grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(2.5f, 2.5f, 2.5f)));
        Assert.Equal(0f, far);
        Assert.Equal(VdbGridClass.FogVolume, grid.GridClass);
    }

    [Fact]
    public void TwoPointsAccumulate()
    {
        var grid = PointsToVolume.Splat(
            [new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 0, 0)], null, 0.1f, 0.4f, 1f,
            PointsToVolume.Falloff.Constant, 0);

        var first = grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(0.5f, 0, 0)));
        var second = grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(-0.5f, 0, 0)));
        Assert.InRange(first, 0.99f, 1.5f);
        Assert.InRange(second, 0.99f, 1.5f);
    }
}

public class VolumePrimitivesTests
{
    [Theory]
    [InlineData(VolumePrimitives.Shape.Sphere)]
    [InlineData(VolumePrimitives.Shape.Box)]
    [InlineData(VolumePrimitives.Shape.Torus)]
    [InlineData(VolumePrimitives.Shape.Capsule)]
    [InlineData(VolumePrimitives.Shape.Cylinder)]
    [InlineData(VolumePrimitives.Shape.Cone)]
    [InlineData(VolumePrimitives.Shape.Octahedron)]
    public void PrimitivesHaveSurfaceAtExpectedDistance(VolumePrimitives.Shape shape)
    {
        var grid = VolumePrimitives.Create(shape, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);

        // The zero crossing exists somewhere: at least one voxel is negative.
        var hasNegative = false;
        foreach (var leaf in grid.Tree.Leaves)
        {
            foreach (var i in leaf.ValueMask.OnBits())
            {
                if (leaf.Values[i] < 0)
                {
                    hasNegative = true;
                    break;
                }
            }
        }

        Assert.True(hasNegative, $"{shape} has no interior voxels");

        // Background is positive band edge.
        Assert.True(grid.Background > 0);
        Assert.Equal(VdbGridClass.LevelSet, grid.GridClass);
    }

    [Fact]
    public void SpherePrimitiveRadiusIsCorrect()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1.2f, 0, 0), 0.05f, Vector3.Zero, 3);

        Assert.InRange(grid.Tree.GetValue(grid.Transform.WorldToIndex(Vector3.Zero)), -0.7f, -0.5f);
        Assert.True(grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(0.7f, 0, 0))) < 0.1f);
        Assert.True(grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(1.0f, 0, 0))) > 0f);
    }
}

public class VolumeDenseTests
{
    [Fact]
    public void ExtractDenseMatchesTree()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var dense = VolumeDense.ExtractDense(grid, 128);

        Assert.NotNull(dense);
        Assert.True(dense.SizeX <= 128 && dense.SizeY <= 128 && dense.SizeZ <= 128);
        Assert.Equal(dense.SizeX * dense.SizeY * dense.SizeZ, dense.Data.Length);

        // Center voxel should match the tree.
        var coord = dense.Origin + new VdbCoord(dense.SizeX / 2, dense.SizeY / 2, dense.SizeZ / 2);
        Assert.Equal(grid.Tree.GetValue(coord), dense.GetValue(dense.SizeX / 2, dense.SizeY / 2, dense.SizeZ / 2));
    }

    [Fact]
    public void ResamplePreservesSurface()
    {
        var fine = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.05f, Vector3.Zero, 3);
        var coarse = VolumeDense.Resample(fine, 0.1f);

        Assert.Equal(0.1f, coarse.Transform.VoxelSize, 5);
        Assert.True(coarse.ActiveVoxelCount > 0);
        Assert.True(coarse.Tree.GetValue(coarse.Transform.WorldToIndex(Vector3.Zero)) < 0, "center should stay inside after resample");
    }

    [Fact]
    public void ResampleToFinerVoxelSizeKeepsFullExtent()
    {
        var source = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(2f, 2f, 2f), 0.2f, Vector3.Zero, 3);
        var fine = VolumeDense.Resample(source, 0.05f);

        var sourceBox = source.ActiveWorldBBox();
        var fineBox = fine.ActiveWorldBBox();
        Assert.True(fine.ActiveVoxelCount > 0, "resampling to a finer voxel size must produce voxels");

        // The source is active out to its band (radius 1 + 3 voxels of 0.2); the finer grid must cover
        // at least the same world region instead of being clipped to the source's index range.
        Assert.True(fineBox.Min.X <= sourceBox.Min.X + 0.15f, $"fine min {fineBox.Min.X} vs source {sourceBox.Min.X}");
        Assert.True(fineBox.Max.X >= sourceBox.Max.X - 0.15f, $"fine max {fineBox.Max.X} vs source {sourceBox.Max.X}");
        Assert.True(fineBox.Min.Z <= sourceBox.Min.Z + 0.15f, $"fine min {fineBox.Min.Z} vs source {sourceBox.Min.Z}");
        Assert.True(fineBox.Max.Z >= sourceBox.Max.Z - 0.15f, $"fine max {fineBox.Max.Z} vs source {sourceBox.Max.Z}");
    }

    [Fact]
    public void ResampleFogVolumeKeepsDensity()
    {
        var (positions, triangles) = TestMeshes.UvSphere(1f, 16, 8);
        var fog = MeshToVolume.ConvertCorners(positions, triangles, 0.05f, 3, VdbVolumeType.Fog, Vector3.Zero);
        Assert.True(fog.ActiveVoxelCount > 0);

        var resampled = VolumeDense.Resample(fog, 0.1f);
        Assert.True(resampled.ActiveVoxelCount > 0, "resampling a fog volume must not produce an empty grid");

        var surface = resampled.Tree.GetValue(resampled.Transform.WorldToIndex(new Vector3(1f, 0f, 0f)));
        Assert.True(surface > 0.1f, $"density at the surface should survive the resample, got {surface}");
    }
}

public class VolumeDiagnoseTests
{
    [Fact]
    public void DetectsNanValues()
    {
        var grid = new VdbGrid<float>(1f, 0.1f);
        grid.Tree.SetValueOn(new VdbCoord(1, 1, 1), float.NaN);
        grid.Tree.SetValueOn(new VdbCoord(2, 1, 1), float.PositiveInfinity);
        grid.Tree.SetValueOn(new VdbCoord(3, 1, 1), 0.5f);

        var report = VolumeDiagnose.Check(grid);
        Assert.Equal(1, report.NanCount);
        Assert.Equal(1, report.InfCount);
        Assert.True(report.HasErrors);
        Assert.Equal(0.5f, report.MaxValue);
    }

    [Fact]
    public void HealthyVolumePasses()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var report = VolumeDiagnose.Check(grid);
        Assert.False(report.HasErrors);
        Assert.True(report.ActiveVoxels > 0);
        Assert.True(report.MemoryBytes > 0);
    }
}

public class VdbSamplerTests
{
    [Fact]
    public void TrilinearMidpointInterpolates()
    {
        var grid = new VdbGrid<float>(0f, 1f);
        grid.Tree.SetValueOn(new VdbCoord(0, 0, 0), 0f);
        grid.Tree.SetValueOn(new VdbCoord(1, 0, 0), 2f);

        var sampler = new VdbSampler(grid);
        // Voxel centers are at 0 and 1 in world space (voxel size 1, no translation).
        var value = sampler.SampleWorld(new Vector3(0.5f, 0f, 0f));
        Assert.Equal(1f, value, 3);
    }
}

public class VolumeRegressionTests
{
    [Fact]
    public void ConstantMathAppliesToTilesCollapsedByPrune()
    {
        var grid = new VdbGrid<float>(0f, 0.1f);
        var leaf = grid.Tree.AddLeaf(new VdbCoord(0, 0, 0));
        leaf.Fill(2f);
        grid.Tree.Prune();
        Assert.Equal(0, grid.Tree.LeafCount); // collapsed into an internal 8^3 tile

        var result = T3.Core.DataTypes.Volume.Tools.VolumeMath.Constant(grid, 3f, T3.Core.DataTypes.Volume.Tools.VolumeMath.BinaryOp.Add);
        Assert.Equal(5f, result.Tree.GetValue(new VdbCoord(3, 3, 3)));
    }

    [Fact]
    public void PrimitiveVoxelsOutsideTheBandReadBackground()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(0.9f), 0.05f, new Vector3(0.5f, 0, 0), 3);

        // Just outside the narrow band of the box, but inside a leaf that holds band voxels.
        var value = grid.Tree.GetValue(grid.Transform.WorldToIndex(new Vector3(-0.1f, 0, 0)));
        Assert.Equal(grid.Background, value, 4);
    }

    [Fact]
    public void CsgDifferenceRemovesOnlyTheOverlap()
    {
        var sphere = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1.2f), 0.05f, Vector3.Zero, 3);
        var box = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(0.9f), 0.05f, new Vector3(0.5f, 0, 0), 3);
        var difference = VolumeCsg.Combine(sphere, box, VolumeCsg.Operation.Difference);

        // Inside the sphere and away from the box stays solid; inside both is carved out.
        Assert.True(difference.Tree.GetValue(difference.Transform.WorldToIndex(new Vector3(-0.3f, 0, 0))) < 0);
        Assert.True(difference.Tree.GetValue(difference.Transform.WorldToIndex(new Vector3(0.3f, 0, 0))) > 0);
    }

    [Fact]
    public void ExtractedSphereIsCenteredAndFacesOutward()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1.2f), 0.05f, Vector3.Zero, 3);
        var mesh = VolumeToMesh.Extract(grid, 0f);

        var volume = 0.0;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var position in mesh.Positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        for (var face = 0; face < mesh.FaceCount; face++)
        {
            var start = mesh.FaceCornerOffsets[face];
            var p0 = mesh.Positions[mesh.CornerPointIndices[start]];
            var p1 = mesh.Positions[mesh.CornerPointIndices[start + 1]];
            var p2 = mesh.Positions[mesh.CornerPointIndices[start + 2]];
            volume += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6.0;
        }

        // Outward counter-clockwise faces give a positive signed volume (4/3 pi 0.6^3 = 0.905).
        Assert.InRange(volume, 0.85, 0.95);
        Assert.InRange((min + max).Length() * 0.5f, 0f, 0.005f);

        mesh.Attributes.TryGet<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, out var normals);
        for (var corner = 0; corner < mesh.CornerCount; corner++)
        {
            var position = mesh.Positions[mesh.CornerPointIndices[corner]];
            Assert.True(Vector3.Dot(normals!.Values[corner], position) > 0f, "normals must point away from the center");
        }
    }

    private static VdbGrid<float> MeshSphereSdf()
    {
        var (positions, triangles) = TestMeshes.UvSphere(1f, 32, 16);
        return MeshToVolume.ConvertCorners(positions, triangles, 0.1f, 3, VdbVolumeType.Sdf, Vector3.Zero);
    }

    [Fact]
    public void NewLeafInactiveVoxelsReadBackground()
    {
        var tree = new VdbTree<float>(1f);
        tree.SetValueOn(new VdbCoord(0, 0, 0), -0.25f);

        Assert.Equal(1f, tree.GetValue(new VdbCoord(1, 0, 0)));
    }

    [Theory]
    [InlineData(VolumeFilter.FilterType.Gaussian)]
    [InlineData(VolumeFilter.FilterType.Box)]
    [InlineData(VolumeFilter.FilterType.Median)]
    public void FilterKeepsInteriorOfMeshSdf(VolumeFilter.FilterType type)
    {
        var grid = MeshSphereSdf();
        var filtered = VolumeFilter.Apply(grid, type, 2);

        Assert.True(filtered.Tree.GetValue(grid.Transform.WorldToIndex(Vector3.Zero)) < 0);
        Assert.Equal(grid.ActiveVoxelCount, filtered.ActiveVoxelCount);
    }

    [Fact]
    public void NoiseKeepsInteriorOfMeshSdf()
    {
        var grid = MeshSphereSdf();
        var noisy = VolumeNoise.Apply(grid, new VdbNoise(1), 1f, 0.2f, Vector3.Zero, 2,
                                      VdbNoiseType.Perlin, VdbNoiseFractalType.None, VolumeNoise.Mode.Displace);

        Assert.True(noisy.Tree.GetValue(grid.Transform.WorldToIndex(Vector3.Zero)) < 0);
    }

    [Fact]
    public void CsgOfDifferentVoxelSizesKeepsBothShapesInPlace()
    {
        // The coarser grid comes first: its leaf origins must be mapped into the finer result space.
        var coarse = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f), 0.1f, new Vector3(-0.6f, 0, 0), 3);
        var fine = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f), 0.05f, new Vector3(0.6f, 0, 0), 3);

        var union = VolumeCsg.Combine(coarse, fine, VolumeCsg.Operation.Union);

        Assert.True(union.Tree.GetValue(union.Transform.WorldToIndex(new Vector3(-0.6f, 0, 0))) < 0);
        Assert.True(union.Tree.GetValue(union.Transform.WorldToIndex(new Vector3(0.6f, 0, 0))) < 0);
        Assert.True(union.Tree.GetValue(union.Transform.WorldToIndex(new Vector3(0f, 0.9f, 0))) > 0);
    }
}

public class VdbVectorSamplerTests
{
    [Fact]
    public void VectorGridIsSampledTrilinearly()
    {
        var grid = new VdbGrid<Vector3>(Vector3.Zero, 1f);
        grid.Tree.SetValueOn(new VdbCoord(0, 0, 0), new Vector3(0, 2, 0));
        grid.Tree.SetValueOn(new VdbCoord(1, 0, 0), new Vector3(4, 2, 0));

        var sampler = new VdbVectorSampler(grid);
        var middle = sampler.SampleWorld(new Vector3(0.5f, 0, 0));
        Assert.Equal(2f, middle.X, 0.0001f);
        Assert.Equal(2f, middle.Y, 0.0001f);
        Assert.Equal(new Vector3(4, 2, 0), sampler.SampleWorld(new Vector3(1, 0, 0)));
    }

    [Fact]
    public void FloatGridGivesItsGradient()
    {
        // value = 3 x, so the slope is 3 along X whatever the voxel size.
        var grid = new VdbGrid<float>(0f, 0.25f);
        for (var x = -4; x <= 4; x++)
        for (var y = -2; y <= 2; y++)
        for (var z = -2; z <= 2; z++)
            grid.Tree.SetValueOn(new VdbCoord(x, y, z), 3f * x * 0.25f);

        var gradient = new VdbVectorSampler(grid).SampleWorld(new Vector3(0.1f, 0.05f, -0.1f));
        Assert.Equal(3f, gradient.X, 0.001f);
        Assert.Equal(0f, gradient.Y, 0.001f);
        Assert.Equal(0f, gradient.Z, 0.001f);
    }

    [Fact]
    public void LevelSetGradientPointsAwayFromTheSurface()
    {
        var sphere = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.05f, Vector3.Zero, 3);
        var gradient = new VdbVectorSampler(sphere).SampleWorld(new Vector3(0, 0.5f, 0));

        Assert.True(gradient.Y > 0.8f, $"expected the outward normal at the top of the sphere, got {gradient}");
        Assert.InRange(MathF.Abs(gradient.X), 0f, 0.1f);
    }
}

public class SdfSamplesTests
{
    /// <summary>A lattice of points carrying the distance to a sphere, as SampleFieldPoints would write it.</summary>
    private static Point[] SphereSamples(int count, float spacing, Vector3 center, float radius, Vector3 origin)
    {
        var points = new Point[count * count * count];
        for (var index = 0; index < points.Length; index++)
        {
            var position = origin + new Vector3(index % count, index / count % count, index / (count * count)) * spacing;
            points[index] = new Point { Position = position, F1 = Vector3.Distance(position, center) - radius, F2 = 99f };
        }

        return points;
    }

    [Fact]
    public void SpacingIsFoundFromTheLattice()
    {
        var points = SphereSamples(6, 0.25f, Vector3.Zero, 0.5f, new Vector3(-3.1f, 2f, 7f));
        Assert.Equal(0.25f, SdfSamples.FindSpacing(points), 0.0001f);
    }

    [Fact]
    public void SampledSphereMeshesToASphereWhereverTheLatticeSits()
    {
        var center = new Vector3(4.3f, -2.1f, 9.7f);
        var points = SphereSamples(24, 0.125f, center, 1f, center - new Vector3(1.44f));
        var grid = SdfSamples.ToGrid(points, 0f);
        Assert.NotNull(grid);

        var mesh = VolumeToMesh.Extract(grid!, 0f);
        Assert.True(mesh.FaceCount > 500, $"{mesh.FaceCount} faces");
        foreach (var position in mesh.Positions)
            Assert.InRange(Vector3.Distance(position, center), 0.97f, 1.03f);
    }

    [Fact]
    public void ShuffledSamplesGiveTheSameSurface()
    {
        var points = SphereSamples(16, 0.2f, Vector3.Zero, 1f, new Vector3(-1.5f));
        var ordered = VolumeToMesh.Extract(SdfSamples.ToGrid(points, 0.2f)!, 0f).FaceCount;
        new Random(3).Shuffle(points);
        Assert.Equal(ordered, VolumeToMesh.Extract(SdfSamples.ToGrid(points, 0.2f)!, 0f).FaceCount);
    }

    [Fact]
    public void BadSamplesAreLeftOutAndTooFewGiveNothing()
    {
        var points = SphereSamples(16, 0.2f, Vector3.Zero, 1f, new Vector3(-1.5f));
        points[100].F1 = float.NaN;
        points[200].Position = new Vector3(float.PositiveInfinity, 0, 0);
        points[300].Position = new Vector3(1e30f, 0, 0);
        Assert.True(VolumeToMesh.Extract(SdfSamples.ToGrid(points, 0.2f)!, 0f).FaceCount > 100);

        Assert.Null(SdfSamples.ToGrid(points.AsSpan(0, 4), 0.2f));
        Assert.Null(SdfSamples.ToGrid(new Point[64], 0f));
        Assert.Equal(99f, SdfSamples.ToGrid(points, 0.2f, useF2: true)!.Tree.GetValue(new VdbCoord(0, 0, 0)));
    }
}
