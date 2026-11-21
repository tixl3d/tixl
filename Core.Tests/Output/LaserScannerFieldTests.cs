using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.Output;
using Xunit;
using Xunit.Abstractions;

namespace Core.Tests.Output;

/// <summary>
/// The laser side of the calibration solve: a laser output has no pixel canvas, so correspondences
/// live in a square field image (see <see cref="LaserScannerField"/>). These pin the convention and
/// the round trip the Scanner Camera card relies on - place points, solve, project through the
/// solved camera, land where the beam actually was.
/// </summary>
public class LaserScannerFieldTests
{
    private readonly ITestOutputHelper _log;

    public LaserScannerFieldTests(ITestOutputHelper log) => _log = log;

    [Fact]
    public void FieldPixelsAreSquareAndYUp()
    {
        Assert.Equal(LaserScannerField.Resolution, LaserScannerField.PixelResolution.Width);
        Assert.Equal(LaserScannerField.Resolution, LaserScannerField.PixelResolution.Height);

        // Centre, right/top edge, left/bottom edge
        Assert.Equal(new Vector2(LaserScannerField.Resolution * 0.5f, LaserScannerField.Resolution * 0.5f),
                     LaserScannerField.PixelFromField(0f, 0f));

        var topRight = LaserScannerField.PixelFromField(1f, 1f);
        Assert.Equal(LaserScannerField.Resolution, topRight.X);
        Assert.Equal(0f, topRight.Y);

        var bottomLeft = LaserScannerField.PixelFromField(-1f, -1f);
        Assert.Equal(0f, bottomLeft.X);
        Assert.Equal(LaserScannerField.Resolution, bottomLeft.Y);

        // Round trip
        var back = LaserScannerField.FieldFromPixel(LaserScannerField.PixelFromField(0.25f, -0.6f));
        Assert.Equal(0.25f, back.X, 4);
        Assert.Equal(-0.6f, back.Y, 4);
    }

    [Fact]
    public void StageAboveTheAxisLandsAboveTheFieldCentre()
    {
        // A scanner on the floor looking level along -Z: a point higher up must land higher in the
        // field (y up), i.e. above the field centre in the image.
        var pose = new Pose(new Vector3(0f, 0.5f, 4f), Quaternion.Identity);
        var lens = Projection.CreatePerspective(40 * MathF.PI / 180f, Vector2.Zero);
        var field = LaserScannerField.PixelResolution;

        Assert.True(ProjectorSolver.TryProjectToPixel(pose, lens, field, new Vector3(0f, 2f, 0f), out var pixel));
        var normalised = LaserScannerField.FieldFromPixel(pixel);
        Assert.True(normalised.Y > 0f, $"expected above centre, got {normalised.Y}");

        Assert.True(ProjectorSolver.TryProjectToPixel(pose, lens, field, new Vector3(1f, 0.5f, 0f), out var right));
        Assert.True(LaserScannerField.FieldFromPixel(right).X > 0f);
    }

    [Fact]
    public void SolvedLaserCameraReproducesTheBeamPositions()
    {
        var pose = new Pose(new Vector3(1.2f, 3.2f, 5.5f), Quaternion.CreateFromYawPitchRoll(0.22f, -0.30f, 0f));
        var lens = Projection.CreatePerspective(32 * MathF.PI / 180f, new Vector2(0.02f, 0.04f));
        var field = LaserScannerField.PixelResolution;

        var stagePoints = new List<Vector3>
                              {
                                  new(0.3f, 0.2f, 0f), new(2.8f, 0.3f, 0f), new(2.6f, 2.4f, 0f),
                                  new(0.5f, 2.1f, 0f), new(0f, 0.7f, 1.4f), new(0f, 2.0f, 2.2f),
                                  new(1.4f, 0f, 1.2f), new(2.4f, 0f, 2.6f),
                              };

        var points = new List<CalibrationPoint>();
        foreach (var stage in stagePoints)
        {
            Assert.True(ProjectorSolver.TryProjectToPixel(pose, lens, field, stage, out var pixel));
            points.Add(new CalibrationPoint { StagePosition = stage, OutputPixel = pixel });
        }

        Assert.True(ProjectorSolver.TrySolve(points, field, out var solved));
        Assert.True(solved.MaxResidualPx < 1f, $"max residual {solved.MaxResidualPx} px");

        foreach (var stage in stagePoints)
        {
            Assert.True(ProjectorSolver.TryProjectToPixel(solved.Pose, solved.Lens, field, stage, out var reprojected));
            Assert.True(ProjectorSolver.TryProjectToPixel(pose, lens, field, stage, out var expected));
            Assert.True((reprojected - expected).Length() < 1f,
                        $"{stage}: solved {LaserScannerField.FieldFromPixel(reprojected)} vs field {LaserScannerField.FieldFromPixel(expected)}");
        }

        _log.WriteLine($"solved pose position={solved.Pose.Position} orientation={solved.Pose.Orientation} "
                       + $"fovY={solved.Lens.FieldOfViewY} shift={solved.Lens.LensShift} residual={solved.MeanResidualPx:0.###}px");
    }
}
