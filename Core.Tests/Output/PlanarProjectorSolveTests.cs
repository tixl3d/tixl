using System;
using System.Numerics;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

/// <summary>
/// A hidden projector throws a placed surface onto the canvas through the forward model; the planar solve gets only
/// the surface's corner pin, its placement and the lens shift, and must give the projector back.
/// </summary>
public class PlanarProjectorSolveTests
{
    [Fact]
    public void TurnedWall_RecoversPoseAndZoom()
    {
        var placement = new Pose(new Vector3(0.4f, 0.2f, -0.3f), Quaternion.CreateFromYawPitchRoll(0.25f, 0, 0));
        AssertRecovers(placement, new Vector2(2.4f, 1.6f), new Vector2(0.3f, -0.8f));
    }

    [Fact]
    public void Floor_RecoversPoseAndZoom()
    {
        var placement = new Pose(new Vector3(-0.5f, 0, 0.6f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2));
        AssertRecovers(placement, new Vector2(2f, 1.5f), new Vector2(-0.2f, 0.4f));
    }

    [Fact]
    public void SquarelySeenWall_IsDeclined()
    {
        // Looking straight at the wall: no keystone, so near-wide and far-tele look the same.
        var canvas = new Int2(1920, 1080);
        var projector = new Pose(new Vector3(0, 0, 3), Quaternion.Identity);
        var lens = Projection.CreatePerspective(40 * MathF.PI / 180, Vector2.Zero);
        Assert.True(TryPin(projector, lens, canvas, Pose.Identity, new Vector2(2, 1), out var pin));

        Assert.False(ProjectorSolver.TrySolveFromPlane(pin, Pose.Identity, Vector2.Zero, canvas, 0, out _, out _, out _));

        // With the zoom given, the pose follows from the pin alone.
        Assert.True(ProjectorSolver.TrySolveFromPlane(pin, Pose.Identity, Vector2.Zero, canvas, lens.FieldOfViewY,
                                                      out var solvedPose, out var solvedLens, out var usedFallbackZoom));
        Assert.True(usedFallbackZoom);
        Assert.True((solvedPose.Position - projector.Position).Length() < 0.01f, $"position {solvedPose.Position}");
        Assert.Equal(lens.FieldOfViewY, solvedLens.FieldOfViewY, 4);
    }

    [Fact]
    public void AngledWall_WithKnownZoom_RecoversPose()
    {
        // Following a pin while it is aimed: the zoom stays what it was solved as, only the pose moves.
        var canvas = new Int2(1920, 1080);
        var projector = new Pose(new Vector3(1.3f, 1.9f, 3.2f), Quaternion.CreateFromYawPitchRoll(0.3f, -0.35f, 0.05f));
        var lens = Projection.CreatePerspective(33 * MathF.PI / 180, new Vector2(0.1f, -0.6f));
        Assert.True(TryPin(projector, lens, canvas, Pose.Identity, new Vector2(2, 1.5f), out var pin));

        Assert.True(ProjectorSolver.TrySolvePoseFromPlane(pin, Pose.Identity, lens.LensShift, canvas, lens.FieldOfViewY,
                                                          out var solvedPose, out var solvedLens));
        Assert.Equal(lens.FieldOfViewY, solvedLens.FieldOfViewY, 5);
        Assert.True((solvedPose.Position - projector.Position).Length() < 0.01f, $"position {solvedPose.Position}");
        Assert.True(Math.Abs(Quaternion.Dot(solvedPose.Orientation, projector.Orientation)) > 0.9999f, "orientation");
    }

    private static void AssertRecovers(Pose placement, Vector2 surfaceSize, Vector2 rectMin)
    {
        var canvas = new Int2(1920, 1080);
        var projector = new Pose(new Vector3(1.3f, 1.9f, 3.2f), Quaternion.CreateFromYawPitchRoll(0.3f, -0.35f, 0.05f));
        var lens = Projection.CreatePerspective(33 * MathF.PI / 180, new Vector2(0.1f, -0.6f));
        Assert.True(TryPin(projector, lens, canvas, placement, surfaceSize, out var pin, rectMin));

        Assert.True(ProjectorSolver.TrySolveFromPlane(pin, placement, lens.LensShift, canvas, 0, out var solvedPose, out var solvedLens, out var usedFallbackZoom));
        Assert.False(usedFallbackZoom);

        Assert.True((solvedPose.Position - projector.Position).Length() < 0.01f, $"position {solvedPose.Position} vs {projector.Position}");
        Assert.True(Math.Abs(solvedLens.FieldOfViewY - lens.FieldOfViewY) < 0.001f, $"fov {solvedLens.FieldOfViewY} vs {lens.FieldOfViewY}");
        Assert.True(Math.Abs(Quaternion.Dot(solvedPose.Orientation, projector.Orientation)) > 0.9999f, "orientation");

        // And it predicts a point off the plane, which a corner pin can't.
        var offPlane = Vector3.Transform(new Vector3(rectMin.X + 0.5f, rectMin.Y + 0.5f, 0.3f), placement.ToWorldMatrix());
        Assert.True(ProjectorSolver.TryProjectToPixel(projector, lens, canvas, offPlane, out var expected));
        Assert.True(ProjectorSolver.TryProjectToPixel(solvedPose, solvedLens, canvas, offPlane, out var predicted));
        Assert.True((predicted - expected).Length() < 1f, $"off-plane point {predicted} vs {expected}");
    }

    /** The corner pin the projector throws the surface's rectangle with: surface metres → canvas pixels. */
    private static bool TryPin(Pose projector, Projection lens, Int2 canvas, Pose placement, Vector2 size, out Homography pin, Vector2 rectMin = default)
    {
        pin = default;
        Span<Vector2> rect = [rectMin, rectMin + new Vector2(size.X, 0), rectMin + size, rectMin + new Vector2(0, size.Y)];
        Span<Vector2> pixels = stackalloc Vector2[4];
        var surfaceToStage = placement.ToWorldMatrix();
        for (var i = 0; i < 4; i++)
        {
            var stage = Vector3.Transform(new Vector3(rect[i], 0), surfaceToStage);
            if (!ProjectorSolver.TryProjectToPixel(projector, lens, canvas, stage, out pixels[i]))
                return false;
        }

        return Homography.TryComputeQuadToQuad(rect, pixels, out pin);
    }
}
