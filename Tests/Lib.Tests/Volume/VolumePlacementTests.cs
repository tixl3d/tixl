using System.Numerics;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using Xunit;

namespace Lib.Tests.Volume;

public class VolumePlacementTests
{
    [Fact]
    public void ZUpGridStandsUpAndScales()
    {
        // A capsule lying along Z, as a Z-up file stores an upright one. The interior is solid, so pruning has left
        // uniform tiles that have to be turned along with the leaves.
        var lying = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(0.6f, 0.4f, 2.4f), 0.05f, new Vector3(0.3f, -0.2f, 1.2f), 3);
        var placed = VolumePlacement.Apply(lying, 0.5f, true, new Vector3(4f, 0f, 0f));

        var source = new VdbSampler(lying);
        var target = new VdbSampler(placed);
        var random = new Random(1);
        for (var index = 0; index < 400; index++)
        {
            var p = new Vector3(random.NextSingle() * 1.6f - 0.5f, random.NextSingle() * 1.2f - 0.8f, random.NextSingle() * 3.2f - 0.4f);
            var turned = VolumePlacement.ZUpToYUp(p) * 0.5f + new Vector3(4f, 0f, 0f);
            Assert.Equal(source.SampleWorld(p) * 0.5f, target.SampleWorld(turned), 3);
        }

        var bounds = placed.ActiveWorldBBox();
        Assert.True(bounds.Extents.Y > bounds.Extents.X * 2.5f, $"the long axis is now up: {bounds.Min} .. {bounds.Max}");
    }

    [Fact]
    public void UniformTilesAreTurnedToo()
    {
        var fog = new VdbGrid<float>(0f, 1f, "density", VdbGridClass.FogVolume);
        var leaf = fog.Tree.AddLeaf(new VdbCoord(16, 8, 24));
        Array.Fill(leaf.Values, 0.75f);
        leaf.ValueMask.SetAll();
        fog.Tree.Prune();
        Assert.Equal(0, fog.Tree.LeafCount);
        Assert.Equal(0.75f, fog.Tree.GetValue(new VdbCoord(20, 10, 30)));

        var placed = VolumePlacement.Apply(fog, 1f, true, Vector3.Zero);
        var world = VolumePlacement.ZUpToYUp(fog.Transform.IndexToWorld(new VdbCoord(20, 10, 30)));
        var index = placed.Transform.WorldToIndexf(world);
        var coord = new VdbCoord((int)MathF.Round(index.X), (int)MathF.Round(index.Y), (int)MathF.Round(index.Z));
        Assert.Equal(0.75f, placed.Tree.GetValue(coord));
        Assert.True(placed.Tree.IsValueOn(coord));
        Assert.Equal(512, placed.ActiveVoxelCount);
    }

    [Fact]
    public void FogDensityKeepsItsValuesAndVectorsTurn()
    {
        var fog = PointsToVolume.Splat([new Vector3(1f, 2f, 3f)], null, 0.1f, 0.5f, 1f, PointsToVolume.Falloff.Gaussian, 0);
        var placed = VolumePlacement.Apply(fog, 2f, true, Vector3.Zero);
        Assert.Equal(new VdbSampler(fog).SampleWorld(new Vector3(1f, 2f, 3f)),
                     new VdbSampler(placed).SampleWorld(VolumePlacement.ZUpToYUp(new Vector3(1f, 2f, 3f)) * 2f), 4);

        var velocity = new VdbGrid<Vector3>(Vector3.Zero, 0.1f);
        velocity.Tree.SetValueOn(new VdbCoord(1, 2, 3), new Vector3(0f, 0f, 1f));
        var turnedVelocity = VolumePlacement.Apply(velocity, 2f, true, Vector3.Zero);
        Assert.Equal(new Vector3(0f, 2f, 0f), turnedVelocity.Tree.GetValue(new VdbCoord(1, 3, -3)));
    }
}
