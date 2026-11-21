#nullable enable
using System;
using System.IO;
using System.Numerics;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.IO;
using Xunit;

namespace Core.Tests.Volume;

/// <summary>OpenVDB maps index (i,j,k) to world translation + (i,j,k) * voxelSize; the voxel center sits on that point.</summary>
public class VdbTransformTests
{
    [Fact]
    public void VoxelCenterIsTranslationPlusIndexTimesSize()
    {
        var transform = new VdbTransform(0.25f, new Vector3(1f, -2f, 3f));

        Assert.Equal(new Vector3(1f, -2f, 3f), transform.IndexToWorld(new VdbCoord(0, 0, 0)));
        Assert.Equal(new Vector3(2f, -1.5f, 3.5f), transform.IndexToWorld(new VdbCoord(4, 2, 2)));
    }

    [Fact]
    public void WorldToIndexInvertsIndexToWorld()
    {
        var transform = new VdbTransform(0.1f, new Vector3(0.3f, 0.4f, -0.5f));
        var index = new Vector3(7f, -3f, 12f);

        var back = transform.WorldToIndexf(transform.IndexToWorld(index));

        Assert.True(Vector3.Distance(index, back) < 1e-3f);
    }

    [Fact]
    public void CellCornerIsHalfAVoxelBelowTheCenter()
    {
        var transform = new VdbTransform(0.5f, Vector3.Zero);

        Assert.Equal(new Vector3(-0.25f), transform.IndexCornerToWorld(Vector3.Zero));
        Assert.Equal(new Vector3(0.25f), transform.IndexCornerToWorld(Vector3.One));
    }

    [Fact]
    public void FileRoundTripKeepsVoxelWorldPositions()
    {
        var grid = new VdbGrid<float>(1f, 0.2f) { Transform = new VdbTransform(0.2f, new Vector3(0.5f, 1f, -1.5f)) };
        grid.Tree.SetValueOn(new VdbCoord(3, -2, 5), 0.25f);
        using var stream = new MemoryStream();
        VdbFile.Save(new VdbVolume(grid), stream);
        stream.Position = 0;

        var loaded = VdbFile.Load(stream).GetFloatGrid()!;

        Assert.Equal(grid.Transform.IndexToWorld(new VdbCoord(3, -2, 5)), loaded.Transform.IndexToWorld(new VdbCoord(3, -2, 5)));
        Assert.Equal(0.25f, loaded.Tree.GetValue(new VdbCoord(3, -2, 5)));
    }

    [Fact]
    public void NonFiniteVoxelSizeFallsBackToDefault()
    {
        var transform = new VdbTransform(float.NaN, new Vector3(1f, 2f, 3f));

        Assert.Equal(0.1f, transform.VoxelSize);
        Assert.Equal(new Vector3(1f, 2f, 3f), transform.Translation);
    }

    [Fact]
    public void AnisotropicMapsAreRejectedInsteadOfAveraged()
    {
        Assert.Throws<NotSupportedException>(() => VdbTransform.FromOpenVdbMap("ScaleTranslateMap", [0, 0, 0, 0.02, 0.05, 0.02, 0, 0, 0]));
        Assert.Throws<NotSupportedException>(() => VdbTransform.FromOpenVdbMap("ScaleMap", [0.02, 0.05, 0.02]));
    }

    [Fact]
    public void UniformPerAxisScalesStillLoad()
    {
        var transform = VdbTransform.FromOpenVdbMap("ScaleTranslateMap", [0, 0, 0, 0.02, 0.02, 0.02, 0, 0, 0]);

        Assert.Equal(0.02f, transform.VoxelSize);
        Assert.Equal(0.02f, VdbTransform.FromOpenVdbMap("ScaleMap", [0.02, 0.02, 0.02]).VoxelSize);
    }
}
