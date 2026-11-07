using System;
using System.Numerics;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

public class StageRectTests
{
    [Fact]
    public void Root_IsItsOwnRectangle_InTheFrameTheProjectorSolveUses()
    {
        var setup = new Setup();
        var wall = new Surface { SizeInMeters = new Vector2(2, 1), Anchor = new Vector2(-1, -1) };
        var pose = new Pose(new Vector3(0.5f, 0, -3), Quaternion.CreateFromYawPitchRoll(0.4f, 0, 0));
        wall.Placement = new Surface.StagePlacement { Pose = pose };
        setup.Surfaces.Add(wall);

        SurfaceGeometry.GetStageRect(setup, wall, out var world, out var min, out var max, out var lift);
        SurfaceGeometry.LocalBounds(wall, out var localMin, out var localMax);

        // The solve maps the surface's own metres through its placement, so the quad must too.
        Assert.Equal(localMin, min);
        Assert.Equal(localMax, max);
        Assert.Equal(0, lift);
        Assert.Equal(pose.ToWorldMatrix(), world);
    }

    [Fact]
    public void Region_SitsAtItsPlaceOnTheParent_LiftedOffIt()
    {
        var setup = new Setup();
        var wall = new Surface { SizeInMeters = new Vector2(4, 2), Anchor = new Vector2(0, 0) };
        var label = new Surface
                        {
                            Kind = Surface.Kinds.Layout,
                            ParentId = wall.Id,
                            SizeInMeters = new Vector2(0.5f, 0.25f),
                            LocalPosition = new Vector2(0.3f, -0.2f),
                        };
        setup.Surfaces.Add(wall);
        setup.Surfaces.Add(label);

        SurfaceGeometry.GetStageRect(setup, label, out var world, out var min, out var max, out var lift);

        Assert.Equal(new Vector2(0.3f, -0.2f), min);
        Assert.True((max - new Vector2(0.8f, 0.05f)).Length() < 1e-6f, $"max {max}");
        Assert.Equal(SurfaceGeometry.RegionLiftInMeters, lift);
        Assert.Equal(Matrix4x4.Identity, world);
    }
}
