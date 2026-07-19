using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

/// <summary>
/// Validates the DLT solve against a virtual projector: synthesize pixels from a hidden
/// Pose + Projection, solve from the correspondences alone, and require sub-pixel residuals.
/// </summary>
public class ProjectorSolverTests
{
    [Fact]
    public void VirtualProjector_SolvesWithSubPixelResidual()
    {
        var (pose, lens, canvas) = CreateVirtualProjector();
        var points = ProjectStagePoints(pose, lens, canvas, TwoPlanePoints());

        Assert.True(ProjectorSolver.TrySolve(points, canvas, out var result));
        Assert.True(result.MaxResidualPx < 1.0f, $"max residual {result.MaxResidualPx} px");
    }

    [Fact]
    public void VirtualProjector_RecoversPoseAndLens()
    {
        var (pose, lens, canvas) = CreateVirtualProjector();
        var points = ProjectStagePoints(pose, lens, canvas, TwoPlanePoints());

        Assert.True(ProjectorSolver.TrySolve(points, canvas, out var result));

        Assert.True((result.Pose.Position - pose.Position).Length() < 0.01f,
                    $"position {result.Pose.Position} vs {pose.Position}");
        Assert.True(Math.Abs(result.Lens.FieldOfViewY - lens.FieldOfViewY) < 0.005f,
                    $"fov {result.Lens.FieldOfViewY} vs {lens.FieldOfViewY}");
        Assert.True((result.Lens.LensShift - lens.LensShift).Length() < 0.01f,
                    $"shift {result.Lens.LensShift} vs {lens.LensShift}");

        // Same rotation up to quaternion double-cover
        var dot = Math.Abs(Quaternion.Dot(result.Pose.Orientation, pose.Orientation));
        Assert.True(dot > 0.9999f, $"orientation dot {dot}");
    }

    [Fact]
    public void SolvedCamera_PredictsUnseenPoints()
    {
        var (pose, lens, canvas) = CreateVirtualProjector();
        var points = ProjectStagePoints(pose, lens, canvas, TwoPlanePoints());

        Assert.True(ProjectorSolver.TrySolve(points, canvas, out var result));

        // A point that was NOT part of the solve (e.g. the floor, never hand-pinned)
        var unseen = new Vector3(1.1f, 0.0f, 0.8f);
        Assert.True(ProjectorSolver.TryProjectToPixel(pose, lens, canvas, unseen, out var expected));
        Assert.True(ProjectorSolver.TryProjectToPixel(result.Pose, result.Lens, canvas, unseen, out var predicted));
        Assert.True((predicted - expected).Length() < 1.0f);
    }

    [Fact]
    public void CoplanarPoints_AreRejected()
    {
        var (pose, lens, canvas) = CreateVirtualProjector();

        // All points on the z=0 wall: fov and standoff cannot be separated
        var coplanar = new List<Vector3>();
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 3; j++)
                coplanar.Add(new Vector3(i * 0.8f, 0.5f + j * 0.7f, 0));
        }

        var points = ProjectStagePoints(pose, lens, canvas, coplanar);
        Assert.False(ProjectorSolver.TrySolve(points, canvas, out _));
    }

    [Fact]
    public void TooFewPoints_AreRejected()
    {
        var (pose, lens, canvas) = CreateVirtualProjector();
        var points = ProjectStagePoints(pose, lens, canvas, TwoPlanePoints());
        points.RemoveRange(ProjectorSolver.MinPointCount - 1, points.Count - (ProjectorSolver.MinPointCount - 1));

        Assert.False(ProjectorSolver.TrySolve(points, canvas, out _));
    }

    [Fact]
    public void TryProjectToPixel_CenterAndShiftBehave()
    {
        // Camera at +2m on Z looking at origin, no shift: the origin lands at the canvas center
        var canvas = new Int2(1920, 1200);
        var pose = new Pose(new Vector3(0, 0, 2), Quaternion.Identity);
        var lens = Projection.CreatePerspective(60 * MathF.PI / 180f, Vector2.Zero);

        Assert.True(ProjectorSolver.TryProjectToPixel(pose, lens, canvas, Vector3.Zero, out var center));
        Assert.True((center - new Vector2(960, 600)).Length() < 0.01f);

        // Positive Y lens shift moves the image up on the canvas => the projected point's
        // pixel v decreases (pixel space is y-down)
        var shifted = Projection.CreatePerspective(60 * MathF.PI / 180f, new Vector2(0, 0.5f));
        Assert.True(ProjectorSolver.TryProjectToPixel(pose, shifted, canvas, Vector3.Zero, out var shiftedPixel));
        Assert.True(shiftedPixel.Y < center.Y);
        Assert.Equal(center.X, shiftedPixel.X, 2);

        // Behind the camera fails instead of exploding
        Assert.False(ProjectorSolver.TryProjectToPixel(pose, lens, canvas, new Vector3(0, 0, 5), out _));
    }

    /// <summary>A projector mounted at head height, off to the side, tilted toward the corner — with lens shift.</summary>
    private static (Pose Pose, Projection Lens, Int2 Canvas) CreateVirtualProjector()
    {
        var position = new Vector3(1.8f, 1.6f, 3.5f);
        var orientation = Quaternion.CreateFromYawPitchRoll(0.35f, -0.12f, 0.03f);
        var lens = Projection.CreatePerspective(38 * MathF.PI / 180f, new Vector2(0.05f, 0.42f));
        return (new Pose(position, orientation), lens, new Int2(1920, 1200));
    }

    /// <summary>Stage points spanning two walls and the floor (the "spread across two planes" requirement).</summary>
    private static List<Vector3> TwoPlanePoints()
    {
        return
        [
            // Wall z = 0
            new Vector3(0.2f, 0.4f, 0),
            new Vector3(2.6f, 0.5f, 0),
            new Vector3(2.4f, 2.2f, 0),
            new Vector3(0.4f, 2.4f, 0),
            // Wall x = 0 (perpendicular)
            new Vector3(0, 0.6f, 0.9f),
            new Vector3(0, 2.1f, 1.6f),
            new Vector3(0, 1.2f, 2.3f),
            // Floor y = 0
            new Vector3(1.2f, 0, 1.1f),
            new Vector3(2.2f, 0, 1.9f),
        ];
    }

    private static List<CalibrationPoint> ProjectStagePoints(in Pose pose, in Projection lens, Int2 canvas, List<Vector3> stagePoints)
    {
        var points = new List<CalibrationPoint>(stagePoints.Count);
        foreach (var stagePoint in stagePoints)
        {
            Assert.True(ProjectorSolver.TryProjectToPixel(pose, lens, canvas, stagePoint, out var pixel),
                        $"virtual projector does not see {stagePoint}");
            points.Add(new CalibrationPoint { StagePosition = stagePoint, OutputPixel = pixel });
        }

        return points;
    }
}
