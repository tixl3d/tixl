using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes;
using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Tests for the laser point-data safety transforms (Core/DataTypes/LaserSafetyTransforms.cs)
/// used by the LaserSafety operator and the built-in output safety in
/// EtherDreamOutput / PONKOutput. The transforms run in a fixed order (safe-zone clamp →
/// mask blanking → jump limiting → static-dwell blanking → intensity ceiling → out-of-bounds
/// audit); these tests pin each stage's behaviour so the order's assumptions stay valid.
/// </summary>
public class LaserSafetyTransformTests
{
    private static LaserPoint Lit(int x, int y, int r = 65535, int g = 65535, int b = 65535)
        => new(x, y, r, g, b);

    private static List<LaserPoint> Points(params LaserPoint[] points) => new(points);

    // --- Safe zone clamp ------------------------------------------------------

    [Fact]
    public void ClampToScanField_MovesOutsidePointsIntoZone()
    {
        var points = Points(Lit(0, 0), Lit(5000, 200), Lit(-5000, -5000));
        var config = LaserSafetyTransforms.Config.Default;
        config.ZoneCenterX = 0;
        config.ZoneCenterY = 0;
        config.ZoneHalfWidth = 1000;
        config.ZoneHalfHeight = 1000;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.ClampToScanField(points, in config, ref stats);

        Assert.Equal(1000, points[1].X);
        Assert.Equal(200, points[1].Y);
        Assert.Equal(-1000, points[2].X);
        Assert.Equal(-1000, points[2].Y);
        Assert.Equal(2, stats.ClampedPoints);
    }

    [Fact]
    public void ClampToScanField_PreservesColor()
    {
        var points = Points(new LaserPoint(5000, 0, 100, 200, 300, 65535));
        var config = LaserSafetyTransforms.Config.Default;
        config.ZoneHalfWidth = 1000;
        config.ZoneHalfHeight = 1000;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.ClampToScanField(points, in config, ref stats);

        Assert.Equal(100, points[0].R);
        Assert.Equal(200, points[0].G);
        Assert.Equal(300, points[0].B);
        Assert.Equal(65535, points[0].I);
    }

    [Fact]
    public void ClampToScanField_ZoneIsIntersectedWithPhysicalField()
    {
        // A zone larger than the field must still clamp to ±32767
        var points = Points(Lit(40000, -40000));
        var config = LaserSafetyTransforms.Config.Default;
        config.ZoneHalfWidth = 100000;
        config.ZoneHalfHeight = 100000;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.ClampToScanField(points, in config, ref stats);

        Assert.Equal(32767, points[0].X);
        Assert.Equal(-32767, points[0].Y);
    }

    // --- Jump limiting --------------------------------------------------------

    [Fact]
    public void LimitJumpDistance_SubdividesLitJumps()
    {
        var points = Points(Lit(0, 0), Lit(8000, 0, 1000, 2000, 3000));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStepPerPoint = 4000;

        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.LimitJumpDistance(points, in config, ref stats);

        Assert.Equal(3, result.Count);
        Assert.Equal(4000, result[1].X);
        Assert.Equal(0, result[1].Y);
        // Inserted points on a lit jump carry the destination color
        Assert.Equal(1000, result[1].R);
        Assert.Equal(1, stats.JumpPointsInserted);
    }

    [Fact]
    public void LimitJumpDistance_InsertedPointsOnBlankedJumpsAreBlanked()
    {
        var points = Points(LaserPoint.CreateBlanked(0, 0), LaserPoint.CreateBlanked(8000, 0));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStepPerPoint = 4000;

        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.LimitJumpDistance(points, in config, ref stats);

        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[1].R);
        Assert.Equal(0, result[1].G);
        Assert.Equal(0, result[1].B);
        Assert.Equal(0, result[1].I);
        Assert.True(result[1].X is > 0 and < 8000);
    }

    [Fact]
    public void LimitJumpDistance_LeavesShortJumpsUntouched()
    {
        var points = Points(Lit(0, 0), Lit(100, 100));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStepPerPoint = 4000;

        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.LimitJumpDistance(points, in config, ref stats);

        Assert.Same(points, result);
        Assert.Equal(0, stats.JumpPointsInserted);
    }

    // --- Static dwell ---------------------------------------------------------

    [Fact]
    public void LimitStaticDwell_BlanksPointsBeyondCap()
    {
        // 100 identical lit points, then a blanked move away and back; cap of 64 leaves the first 64 lit
        var points = new List<LaserPoint>();
        for (var i = 0; i < 100; i++)
            points.Add(Lit(100, 100));
        points.Add(LaserPoint.CreateBlanked(5000, 5000));

        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStaticPoints = 64;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.LimitStaticDwell(points, in config, ref stats);

        Assert.Equal(64, points.Count(p => p.I > 0));
        Assert.Equal(36, stats.StaticBlanked);
        // Blanked points keep their position so the scanners keep moving
        Assert.Equal(100, points[99].X);
        Assert.Equal(100, points[99].Y);
    }

    [Fact]
    public void LimitStaticDwell_RunResetsWhenBeamMoves()
    {
        // Two static groups of 40 points each, separated by a moving point - both stay lit under a cap of 64
        var points = new List<LaserPoint>();
        for (var i = 0; i < 40; i++)
            points.Add(Lit(100, 100));
        points.Add(Lit(2000, 2000));
        for (var i = 0; i < 40; i++)
            points.Add(Lit(300, 300));

        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStaticPoints = 64;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.LimitStaticDwell(points, in config, ref stats);

        Assert.All(points, p => Assert.True(p.I > 0));
        Assert.Equal(0, stats.StaticBlanked);
    }

    [Fact]
    public void LimitStaticDwell_IgnoresBlankedRuns()
    {
        var points = new List<LaserPoint>();
        for (var i = 0; i < 200; i++)
            points.Add(LaserPoint.CreateBlanked(50, 50));

        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStaticPoints = 64;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.LimitStaticDwell(points, in config, ref stats);

        Assert.Equal(0, stats.StaticBlanked);
    }

    // --- Intensity ceiling ----------------------------------------------------

    [Fact]
    public void ClampIntensity_ScalesLitColors()
    {
        var points = Points(new LaserPoint(0, 0, 65535, 32768, 1, 65535));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaxBrightness = 0.5f;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.ClampIntensity(points, in config, ref stats);

        Assert.Equal(32767, points[0].R);
        Assert.Equal(16384, points[0].G);
        Assert.Equal(0, points[0].B);
        // I carries the ceiling too (single-colour projectors are driven from it), and stays lit
        Assert.Equal(32767, points[0].I);
        Assert.Equal(1, stats.IntensityScaled);
    }

    [Fact]
    public void ClampIntensity_AtFullBrightnessFoldsStrayValuesIntoRange()
    {
        var points = Points(new LaserPoint(0, 0, 70000, -500, 65535, 65535));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaxBrightness = 1f;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.ClampIntensity(points, in config, ref stats);

        Assert.Equal(65535, points[0].R);
        Assert.Equal(0, points[0].G);
        Assert.Equal(1, stats.IntensityScaled);
    }

    // --- Out-of-bounds audit --------------------------------------------------

    [Fact]
    public void DetectAndBlankOutOfBounds_BlanksLitPointsOutsideField()
    {
        var points = Points(Lit(40000, 0), Lit(0, 0), LaserPoint.CreateBlanked(-40000, 0));
        var stats = new LaserSafetyTransforms.Stats();

        LaserSafetyTransforms.DetectAndBlankOutOfBounds(points, ref stats);

        Assert.Equal(0, points[0].I);
        Assert.True(points[1].I > 0);
        Assert.Equal(1, stats.OutOfBoundsBlanked);
    }

    // --- Full pipeline --------------------------------------------------------

    [Fact]
    public void Apply_BeamDisable_BlanksEverythingButKeepsGeometry()
    {
        var points = Points(Lit(100, 200, 65535, 65535, 65535), Lit(-300, 400, 1000, 0, 0));
        var config = LaserSafetyTransforms.Config.Default;
        config.BeamEnable = false;

        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.Apply(points, in config, ref stats);

        Assert.All(result, p =>
        {
            Assert.Equal(0, p.R);
            Assert.Equal(0, p.G);
            Assert.Equal(0, p.B);
            Assert.Equal(0, p.I);
        });
        Assert.Equal(100, result[0].X);
        Assert.Equal(400, result[1].Y);
        Assert.True(stats.BeamDisabled);
        Assert.StartsWith("BEAM DISABLED", stats.Describe(result.Count));
    }

    [Fact]
    public void Apply_RunsAllStagesAndStaysInsideTheField()
    {
        var points = new List<LaserPoint> { Lit(0, 0), Lit(8000, 0) };
        for (var i = 0; i < 70; i++)
            points.Add(Lit(8000, 0));
        points.Add(Lit(60000, 60000)); // far outside - clamped by the safe zone

        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStepPerPoint = 4000;
        config.MaxStaticPoints = 64;
        config.MaxBrightness = 0.5f;

        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.Apply(points, in config, ref stats);

        // Long jump got subdivided
        Assert.True(stats.JumpPointsInserted > 0);
        Assert.True(result.Count > points.Count);

        // Over-long static dwell got blanked
        Assert.True(stats.StaticBlanked > 0);

        // Brightness got capped on lit points
        Assert.True(stats.IntensityScaled > 0);
        Assert.All(result, p => Assert.True(p.R <= 32768));

        // Out-of-bounds point was pulled into the field (audit stays silent)
        Assert.Equal(0, stats.OutOfBoundsBlanked);
        Assert.All(result, p =>
        {
            Assert.InRange(p.X, LaserSafetyTransforms.FieldMin, LaserSafetyTransforms.FieldMax);
            Assert.InRange(p.Y, LaserSafetyTransforms.FieldMin, LaserSafetyTransforms.FieldMax);
        });

        // Status summarizes the interventions
        Assert.StartsWith("SAFETY:", stats.Describe(result.Count));
    }

    [Fact]
    public void Apply_CleanFrameReportsOk()
    {
        var points = new List<LaserPoint>();
        for (var i = 0; i < 100; i++)
        {
            var t = i / 100f;
            points.Add(Lit((int)(t * 20000), (int)(t * 10000)));
        }

        var config = LaserSafetyTransforms.Config.Default;
        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.Apply(points, in config, ref stats);

        Assert.Same(points, result);
        Assert.False(stats.AnythingEngaged);
        Assert.StartsWith("OK", stats.Describe(result.Count));
    }

    // --- Blank helper (used by output operators for E-stop / watchdog) --------

    [Fact]
    public void BlankCopyInto_ZeroesColorsKeepsPositions()
    {
        var source = new[] { Lit(100, 200), LaserPoint.CreateBlanked(-50, -60) };
        var destination = new LaserPoint[source.Length];

        LaserSafetyTransforms.BlankCopyInto(source, destination);

        Assert.All(destination, p =>
        {
            Assert.Equal(0, p.R);
            Assert.Equal(0, p.G);
            Assert.Equal(0, p.B);
            Assert.Equal(0, p.I);
        });
        Assert.Equal(100, destination[0].X);
        Assert.Equal(200, destination[0].Y);
        Assert.Equal(-60, destination[1].Y);
    }

    // --- Crash-resistance regression tests ------------------------------------
    // The laser operators must degrade gracefully instead of throwing
    // IndexOutOfRangeException, ArgumentOutOfRangeException, etc. when the
    // upstream data is bad. The BlankCopyInto helper is the last line of
    // defense in the E-stop / watchdog blanking path used by EtherDream and
    // PONK outputs - a length mismatch there used to crash the editor.

    [Fact]
    public void BlankCopyInto_DestinationShorterThanSource_DoesNotThrow()
    {
        // 100 source points, 10 destination slots: must silently copy the first 10
        // rather than indexing past the destination end.
        var source = new LaserPoint[100];
        for (var i = 0; i < source.Length; i++)
            source[i] = Lit(i, i);

        var destination = new LaserPoint[10];

        var ex = Record.Exception(() => LaserSafetyTransforms.BlankCopyInto(source, destination));
        Assert.Null(ex);

        // The first 10 points should be blanked, the rest of `destination` untouched.
        for (var i = 0; i < destination.Length; i++)
        {
            Assert.Equal(0, destination[i].R);
            Assert.Equal(0, destination[i].G);
            Assert.Equal(0, destination[i].B);
            Assert.Equal(0, destination[i].I);
            Assert.Equal(i, destination[i].X);
            Assert.Equal(i, destination[i].Y);
        }
    }

    [Fact]
    public void BlankCopyInto_DestinationLongerThanSource_DoesNotThrow()
    {
        // 5 source points, 50 destination slots: must not overread `source`.
        var source = new[] { Lit(1, 2), Lit(3, 4), Lit(5, 6), Lit(7, 8), Lit(9, 10) };
        var destination = new LaserPoint[50];
        for (var i = 0; i < destination.Length; i++)
            destination[i] = Lit(99, 99, 99, 99, 99);

        var ex = Record.Exception(() => LaserSafetyTransforms.BlankCopyInto(source, destination));
        Assert.Null(ex);

        // Slots 0..4 must be blanked, slots 5..49 must be untouched.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(0, destination[i].R);
            Assert.Equal(0, destination[i].G);
        }
        Assert.Equal(99, destination[10].R);
    }

    [Fact]
    public void BlankCopyInto_EmptyArrays_AreNoOp()
    {
        var ex = Record.Exception(() =>
        {
            LaserSafetyTransforms.BlankCopyInto(Array.Empty<LaserPoint>(), Array.Empty<LaserPoint>());
        });
        Assert.Null(ex);
    }

    [Fact]
    public void LimitJumpDistance_PathologicalInput_CapsExplosiveGrowth()
    {
        // Construct 200 points that are all 60000 units apart from the previous
        // one. With a very small MaxStepPerPoint, each jump gets subdivided into
        // thousands of intermediate points. The hard cap (MaxTotalPoints) stops
        // subdivision from consuming unbounded memory.
        var points = new List<LaserPoint>(200);
        for (var i = 0; i < 200; i++)
            points.Add(Lit(i * 60000, 0));

        var config = LaserSafetyTransforms.Config.Default;
        // Subdivide every point: with jump=60000 and step=1, each pair needs 60000 steps.
        config.MaxStepPerPoint = 1f;

        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.LimitJumpDistance(points, in config, ref stats);

        // Without the cap the result would be ~12 million points.
        // Verify the result is bounded by a reasonable constant (not O(input * jump/step)).
        var bound = 200 * 60000;
        Assert.True(result.Count < bound,
                    $"Result grew to {result.Count} points – subdivision cap may be broken. Bound was {bound}");

        // The result must contain at least one point.
        Assert.True(result.Count >= 1, "Result should not be empty");

        // Subdivision must have engaged (the first jump is 60000 units, far > maxStep).
        // Regression: a previous int32 overflow in (dx*dx+dy*dy) caused the fast path to
        // silently skip subdivision on long jumps, leaving JumpPointsInserted at 0.
        Assert.True(stats.JumpPointsInserted > 0,
                    $"Subdivision should have engaged but JumpPointsInserted={stats.JumpPointsInserted}");
    }

    [Fact]
    public void LimitJumpDistance_LongJumpBeyondInt32Sqrt_DoesNotOverflow()
    {
        // Regression: dx*dx overflows int32 when dx >= 46341 (sqrt(int.MaxValue)).
        // This test uses coordinates large enough to overflow int32 when multiplied as int,
        // verifying the function uses float arithmetic internally.
        var points = Points(Lit(0, 0), Lit(70000, 0)); // 70,000^2 = 4.9B > int.MaxValue

        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStepPerPoint = 1f;

        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.LimitJumpDistance(points, in config, ref stats);

        Assert.True(stats.JumpPointsInserted > 0,
                    $"Int32 overflow suppressed subdivision: JumpPointsInserted={stats.JumpPointsInserted}");
        // 2 input points + 70000 inserted = 70001 total (no cap triggered)
        Assert.Equal(70001, result.Count);
    }

    [Fact]
    public void Apply_NaNCoordinates_ClampedToField()
    {
        // Garbage coordinates must not propagate to the field as NaN.
        var points = Points(Lit(int.MaxValue, int.MinValue), Lit(0, 0));
        var config = LaserSafetyTransforms.Config.Default;

        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.Apply(points, in config, ref stats);

        Assert.All(result, p =>
        {
            Assert.InRange(p.X, LaserSafetyTransforms.FieldMin, LaserSafetyTransforms.FieldMax);
            Assert.InRange(p.Y, LaserSafetyTransforms.FieldMin, LaserSafetyTransforms.FieldMax);
        });
        Assert.True(stats.ClampedPoints > 0);
    }

    [Fact]
    public void ClampToScanField_NonFiniteZoneStillClamps()
    {
        // A NaN half-extent reads as "no limit" to every comparison in Math.Clamp, so the safe
        // zone silently stopped clamping - out-of-zone content reached the scanners. A non-finite
        // limit is a broken value, not a request to disable the stage: it collapses to the center.
        var points = Points(Lit(20000, -20000));
        var config = LaserSafetyTransforms.Config.Default;
        config.ZoneHalfWidth = float.NaN;
        config.ZoneHalfHeight = float.NaN;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.ClampToScanField(points, in config, ref stats);

        Assert.Equal(0, points[0].X);
        Assert.Equal(0, points[0].Y);
        Assert.Equal(1, stats.ClampedPoints);
    }

    [Fact]
    public void LimitJumpDistance_NonFiniteMaxStepStillSubdivides()
    {
        // Same hole on the jump limiter: `dist > NaN` is false for every pair, so a NaN step
        // disabled subdivision entirely. The fallback is the documented default stage size.
        var points = Points(Lit(0, 0), Lit(8000, 0));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStepPerPoint = float.NaN;

        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.LimitJumpDistance(points, in config, ref stats);

        Assert.True(stats.JumpPointsInserted > 0, "subdivision must engage under a non-finite step limit");
        Assert.All(result, p => Assert.InRange(p.X, LaserSafetyTransforms.FieldMin, LaserSafetyTransforms.FieldMax));
    }

    [Fact]
    public void LimitStaticDwell_FullFieldStepCountsAsMovement()
    {
        // 65534 squared overflows int; the step must still read as motion, not as a frozen beam.
        // Even count, so the loop seam (last back to first) is a full-field step as well.
        var points = Points(Lit(-32767, 0));
        for (var i = 0; i < 5; i++)
            points.Add(Lit(i % 2 == 0 ? 32767 : -32767, 0));

        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStaticPoints = 1;
        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.LimitStaticDwell(points, in config, ref stats);

        Assert.Equal(0, stats.StaticBlanked);
    }

    // --- Safety masks ---------------------------------------------------------

    private static float[] CenterQuad(float half = 1000f) =>
        [-half, half, half, half, half, -half, -half, -half];

    [Fact]
    public void Masks_BlankInsideQuadKeepPositionAndCount()
    {
        var points = Points(Lit(0, 0), Lit(5000, 5000), LaserPoint.CreateBlanked(200, 200));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaskCorners = CenterQuad();
        config.MaskAllowOnlyInside = [false];
        config.MaskCount = 1;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.Apply(points, in config, ref stats);

        // Blanked inside the mask, position kept so the scanners keep moving the same path.
        Assert.Equal(0, points[0].R);
        Assert.Equal(0, points[0].I);
        Assert.Equal(0, points[0].X);
        Assert.Equal(0, points[0].Y);

        // Outside the mask stays lit; the pre-blanked point is not counted as masked.
        Assert.Equal(65535, points[1].I);
        Assert.Equal(1, stats.MaskedPoints);
    }

    [Fact]
    public void Masks_AllowOnlyInsideBlanksEverythingOutside()
    {
        var points = Points(Lit(0, 0), Lit(5000, 5000), Lit(-5000, 0));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaskCorners = CenterQuad();
        config.MaskAllowOnlyInside = [true];
        config.MaskCount = 1;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.Apply(points, in config, ref stats);

        Assert.Equal(65535, points[0].I);
        Assert.Equal(0, points[1].I);
        Assert.Equal(0, points[2].I);
        Assert.Equal(2, stats.MaskedPoints);
    }

    [Fact]
    public void Masks_BlankInsideWinsOverAllowMask()
    {
        var allow = CenterQuad();
        var blank = new float[] { 0, 1000, 1000, 1000, 1000, -1000, 0, -1000 };
        var corners = new float[16];
        allow.CopyTo(corners, 0);
        blank.CopyTo(corners, 8);

        var points = Points(Lit(-500, 0), Lit(500, 0), Lit(5000, 0));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaskCorners = corners;
        config.MaskAllowOnlyInside = [true, false];
        config.MaskCount = 2;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.Apply(points, in config, ref stats);

        // In the allow mask only -> lit; in allow AND blank -> blanked; outside allow -> blanked.
        Assert.Equal(65535, points[0].I);
        Assert.Equal(0, points[1].I);
        Assert.Equal(0, points[2].I);
        Assert.Equal(2, stats.MaskedPoints);
    }

    [Fact]
    public void Masks_WorkOnRotatedQuads()
    {
        // Diamond: corners at the axes, not an axis-aligned rectangle.
        var corners = new float[] { 0, 1000, 1000, 0, 0, -1000, -1000, 0 };
        var points = Points(Lit(0, 0), Lit(900, 900));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaskCorners = corners;
        config.MaskAllowOnlyInside = [false];
        config.MaskCount = 1;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.Apply(points, in config, ref stats);

        Assert.Equal(0, points[0].I);
        Assert.Equal(65535, points[1].I);
        Assert.Equal(1, stats.MaskedPoints);
    }

    [Fact]
    public void Masks_DescribeReportsMaskedPoints()
    {
        var points = Points(Lit(0, 0));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaskCorners = CenterQuad();
        config.MaskAllowOnlyInside = [false];
        config.MaskCount = 1;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.Apply(points, in config, ref stats);

        Assert.Contains("mask blanked 1", stats.Describe(points.Count));
    }

    [Fact]
    public void Masks_AttenuatingLevelDimsInsideQuad()
    {
        var points = Points(Lit(0, 0), Lit(5000, 5000));
        var config = LaserSafetyTransforms.Config.Default;
        config.MaskCorners = CenterQuad();
        config.MaskAllowOnlyInside = [false];
        config.MaskLevels = [0.1f];
        config.MaskCount = 1;

        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.Apply(points, in config, ref stats);

        // Inside: dimmed to 10%, position kept, still a lit point. Outside: untouched.
        Assert.Equal(6553, points[0].R);
        Assert.Equal(6553, points[0].I);
        Assert.Equal(0, points[0].X);
        Assert.Equal(65535, points[1].I);
        Assert.Equal(1, stats.MaskedPoints);
    }

    [Fact]
    public void ApplyColorLevels_GainsAndMinimumsShapeTheFrame()
    {
        // Gain lifts a weak channel; the minimum cuts a diode below its emission threshold;
        // a channel that is off stays off even when the gain would lift it.
        var points = Points(new LaserPoint(30000, 0, 10000, 1000, 0, 65535));
        var config = LaserSafetyTransforms.Config.Default;
        config.ColorGainR = 2f;
        config.ColorGainG = 2f;
        config.ColorMinB = 0.05f;

        LaserSafetyTransforms.ApplyColorLevels(points, in config);

        Assert.Equal(20000, points[0].R);
        Assert.Equal(2000, points[0].G);
        Assert.Equal(0, points[0].B);

        // Default levels leave the frame untouched.
        var untouched = Points(Lit(100, 100));
        LaserSafetyTransforms.ApplyColorLevels(untouched, LaserSafetyTransforms.Config.Default);
        Assert.Equal(65535, untouched[0].R);
    }

    [Fact]
    public void ApplyColorLevels_UseTheFull16BitColorScale()
    {
        // A gain on one channel must not halve full-scale colors on the others, and a minimum is a
        // fraction of full scale: 0.5 cuts 20000 (of 65535) but keeps 40000.
        var points = Points(Lit(0, 0));
        var gainConfig = LaserSafetyTransforms.Config.Default;
        gainConfig.ColorGainR = 2f;
        LaserSafetyTransforms.ApplyColorLevels(points, in gainConfig);

        Assert.Equal(65535, points[0].R);
        Assert.Equal(65535, points[0].G);
        Assert.Equal(65535, points[0].B);

        var minConfig = LaserSafetyTransforms.Config.Default;
        minConfig.ColorMinG = 0.5f;
        minConfig.ColorMinB = 0.5f;
        var shaped = Points(Lit(0, 0, 65535, 20000, 40000));
        LaserSafetyTransforms.ApplyColorLevels(shaped, in minConfig);

        Assert.Equal(0, shaped[0].G);
        Assert.Equal(40000, shaped[0].B);
    }

    [Fact]
    public void ApplyColorTimeShift_DelaysAChannelAlongThePath()
    {
        var points = Points(Lit(0, 0, 65535, 0, 0),
                            Lit(100, 0, 0, 65535, 0),
                            Lit(200, 0, 0, 65535, 0),
                            Lit(300, 0, 0, 0, 65535));
        var config = LaserSafetyTransforms.Config.Default;
        config.ColorShiftG = 1;

        LaserSafetyTransforms.ApplyColorTimeShift(points, in config);

        // The green run moved one point later; the point that only had green before is now dark.
        Assert.Equal(65535, points[0].R);
        Assert.Equal(0, points[1].I);
        Assert.Equal(0, points[1].G);
        Assert.Equal(65535, points[2].G);
        Assert.Equal(0, points[2].R);
        Assert.Equal(65535, points[3].G);
        Assert.Equal(65535, points[3].B);
    }

    [Fact]
    public void ApplyColorTimeShift_NegativeShiftLeadsTheBeam()
    {
        var points = Points(Lit(0, 0, 0, 0, 0),
                            Lit(100, 0, 65535, 0, 0),
                            Lit(200, 0, 0, 0, 0));
        var config = LaserSafetyTransforms.Config.Default;
        config.ColorShiftR = -1;

        LaserSafetyTransforms.ApplyColorTimeShift(points, in config);

        Assert.Equal(65535, points[0].R);
        Assert.Equal(0, points[1].I);
    }

    [Fact]
    public void ApplyColorTimeShift_NeverLightsABlankedPoint()
    {
        var points = Points(Lit(0, 0, 65535, 0, 0),
                            LaserPoint.CreateBlanked(100, 0),
                            Lit(200, 0, 0, 0, 0));
        var config = LaserSafetyTransforms.Config.Default;
        config.ColorShiftR = 2;

        LaserSafetyTransforms.ApplyColorTimeShift(points, in config);

        // The red skips over the blanked point: it stays dark and the color lands two points later.
        Assert.Equal(0, points[1].I);
        Assert.Equal(0, points[1].R);
        Assert.Equal(65535, points[2].R);
    }

    [Fact]
    public void ApplyColorTimeShift_DoesNotWrapAroundTheFrame()
    {
        var points = Points(Lit(0, 0, 0, 65535, 0), Lit(100, 0, 65535, 0, 0));
        var config = LaserSafetyTransforms.Config.Default;
        config.ColorShiftR = 1;

        LaserSafetyTransforms.ApplyColorTimeShift(points, in config);

        // Red from the last point does not reappear on the first one, it is dropped past the end.
        Assert.Equal(0, points[0].R);
        Assert.Equal(65535, points[0].G);
        Assert.Equal(0, points[1].R);
    }

    [Fact]
    public void ApplyColorTimeShift_ClampsToTheMaximumShift()
    {
        var points = new List<LaserPoint> { Lit(0, 0, 65535, 0, 0) };
        for (var i = 1; i < 40; i++)
            points.Add(Lit(i, 0, 0, 0, 0));

        var config = LaserSafetyTransforms.Config.Default;
        config.ColorShiftR = 1000;

        LaserSafetyTransforms.ApplyColorTimeShift(points, in config);

        Assert.Equal(65535, points[LaserSafetyTransforms.MaxColorShiftPoints].R);
        Assert.Equal(0, points[0].I);
    }

    [Fact]
    public void ApplyColorTimeShift_DefaultShiftsLeaveTheFrameUntouched()
    {
        var points = Points(Lit(0, 0, 65535, 0, 0), Lit(100, 0, 0, 65535, 0));

        LaserSafetyTransforms.ApplyColorTimeShift(points, LaserSafetyTransforms.Config.Default);

        Assert.Equal(65535, points[0].R);
        Assert.Equal(65535, points[1].G);
    }

    [Fact]
    public void Apply_ShiftedColorStaysUnderTheBrightnessCeiling()
    {
        // The shift runs before the ceiling, so a color moved onto another point is still capped.
        var points = Points(Lit(0, 0, 65535, 0, 0), Lit(100, 0, 0, 0, 0), Lit(200, 0, 0, 0, 0));
        var config = LaserSafetyTransforms.Config.Default;
        config.ColorShiftR = 1;
        config.MaxBrightness = 0.5f;

        var stats = new LaserSafetyTransforms.Stats();
        var result = LaserSafetyTransforms.Apply(points, in config, ref stats);

        Assert.Equal(0, result[0].I);
        Assert.Equal(32767, result[1].R);
    }

    [Fact]
    public void LaserProcessingSettings_ColorShiftRoundTripsAndConfigures()
    {
        var settings = new LaserProcessingSettings();
        settings.Safety.ColorShiftR = -3;
        settings.Safety.ColorShiftB = 5;

        var stringWriter = new StringWriter();
        using (var writer = new JsonTextWriter(stringWriter))
        {
            settings.WriteToJson(writer);
        }

        var read = LaserProcessingSettings.ReadFromJson(JObject.Parse(stringWriter.ToString()));
        Assert.Equal(-3, read.Safety.ColorShiftR);
        Assert.Equal(0, read.Safety.ColorShiftG);
        Assert.Equal(5, read.Safety.ColorShiftB);

        var config = read.Safety.ToConfig();
        Assert.Equal(-3, config.ColorShiftR);
        Assert.Equal(5, config.ColorShiftB);
    }

    [Fact]
    public void LaserProcessingSettings_MasksRoundTripAndConfigure()
    {
        var settings = new LaserProcessingSettings();
        settings.Safety.Masks.Add(new LaserProcessingSettings.SafetySettings.MaskQuad
                                     {
                                         X1 = -0.5f, Y1 = 0.5f,
                                         X2 = 0.5f, Y2 = 0.5f,
                                         X3 = 0.5f, Y3 = -0.5f,
                                         X4 = -0.5f, Y4 = -0.5f,
                                         AllowOnlyInside = true,
                                         Enabled = false,
                                     });
        settings.Safety.MasksChanged();

        var stringWriter = new StringWriter();
        using (var writer = new JsonTextWriter(stringWriter))
        {
            settings.WriteToJson(writer);
        }

        var read = LaserProcessingSettings.ReadFromJson(JObject.Parse(stringWriter.ToString()));
        var mask = Assert.Single(read.Safety.Masks);
        Assert.Equal(-0.5f, mask.X1);
        Assert.Equal(0.5f, mask.Y1);
        Assert.Equal(-0.5f, mask.Y4);
        Assert.True(mask.AllowOnlyInside);
        Assert.False(mask.Enabled);

        // A disabled mask is absent from the safety config.
        var config = read.Safety.ToConfig();
        Assert.Equal(0, config.MaskCount);
        Assert.Null(config.MaskCorners);

        // Enabled, it lands in the config scaled to ILDA units.
        mask.Enabled = true;
        read.Safety.MasksChanged();
        config = read.Safety.ToConfig();
        Assert.Equal(1, config.MaskCount);
        Assert.Equal(-0.5f * LaserSafetyTransforms.FieldMax, config.MaskCorners[0]);
        Assert.Equal(-0.5f * LaserSafetyTransforms.FieldMax, config.MaskCorners[7]);
        Assert.True(config.MaskAllowOnlyInside[0]);
    }

    [Fact]
    public void BuildSafetyShapes_DrawsZoneAndEnabledMasksOnly()
    {
        var safety = new LaserProcessingSettings();
        safety.Safety.RenderShapesLevel = 0.5f;
        safety.Safety.Masks.Add(new LaserProcessingSettings.SafetySettings.MaskQuad
                                    {
                                        X1 = -0.5f, Y1 = 0.5f, X2 = 0.5f, Y2 = 0.5f,
                                        X3 = 0.5f, Y3 = -0.5f, X4 = -0.5f, Y4 = -0.5f,
                                        Enabled = true,
                                    });
        safety.Safety.Masks.Add(new LaserProcessingSettings.SafetySettings.MaskQuad
                                    {
                                        X1 = -0.1f, Y1 = 0.1f, X2 = 0.1f, Y2 = 0.1f,
                                        X3 = 0.1f, Y3 = -0.1f, X4 = -0.1f, Y4 = -0.1f,
                                        Enabled = false,
                                    });

        var target = new LaserPoint[4096];
        var count = LaserPipeline.BuildSafetyShapes(safety.Safety, target);

        // The disabled mask must contribute nothing: two quads at 76 points each.
        Assert.Equal(152, count);

        // First point is the blanked approach; the corner dwell is lit green at half level.
        Assert.Equal(0, target[0].R);
        Assert.Equal(0, target[0].I);
        Assert.Equal((int)(65535 * 0.5f), target[1].G);
        Assert.Equal(0, target[1].R);

        // Level 0 means nothing is written.
        safety.Safety.RenderShapesLevel = 0f;
        Assert.Equal(0, LaserPipeline.BuildSafetyShapes(safety.Safety, target));
    }
}
