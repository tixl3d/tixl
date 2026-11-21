using System.Collections.Generic;
using T3.Core.DataTypes;
using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Regression tests for the hardening pass: counters describe one Apply, public transforms clamp
/// a caller count to its array, and a malformed quad blanks the frame instead of falling back to the full field.
/// </summary>
public class LaserHardeningTests
{
    [Fact]
    public void SafetyApply_ReusedStats_AreResetEachCall()
    {
        var config = LaserSafetyTransforms.Config.Default;
        config.ZoneHalfWidth = 1000;
        config.ZoneHalfHeight = 1000;

        var stats = new LaserSafetyTransforms.Stats();
        var outside = new List<LaserPoint> { new(20000, 0, 65535, 65535, 65535, 65535) };
        LaserSafetyTransforms.Apply(outside, in config, ref stats);
        Assert.True(stats.ClampedPoints > 0);

        var clean = new List<LaserPoint>
                        {
                            new(0, 0, 1000, 1000, 1000, 65535),
                            new(500, 500, 1000, 1000, 1000, 65535),
                        };
        LaserSafetyTransforms.Apply(clean, in config, ref stats);

        Assert.False(stats.AnythingEngaged);
        Assert.Equal(0, stats.ClampedPoints);
    }

    [Fact]
    public void Optimizer_CountBeyondArray_IsClampedInsteadOfThrowing()
    {
        var input = new[]
                        {
                            new LaserPoint(0, 0, 65535, 65535, 65535, 65535),
                            new LaserPoint(1000, 0, 65535, 65535, 65535, 65535),
                        };

        var result = LaserOptimizerTransforms.Apply(input, input.Length + 100, LaserOptimizerTransforms.Config.Default,
                                                    new LaserOptimizerTransforms.Scratch());

        Assert.NotEmpty(result);
        Assert.All(result, p => Assert.InRange(p.X, -32767, 32767));
    }

    [Fact]
    public void FrameMapping_CountBeyondArray_IsClampedInsteadOfThrowing()
    {
        var input = new[] { new LaserPoint(1000, 1000, 65535, 65535, 65535, 65535) };
        var mapped = LaserPipeline.MapFrameIntoZone(input, input.Length + 50, new LaserProcessingSettings.SafetySettings(),
                                                    LaserFrameMappingMode.Stretch, null);

        Assert.NotNull(mapped);
        Assert.Single(mapped!);
    }

    [Fact]
    public void Warp_WithBowTieQuad_FailsClosed()
    {
        // Corners in a crossed (self-intersecting) order: a homography would fold the frame.
        var safety = new LaserProcessingSettings.SafetySettings
                         {
                             WarpCorner1X = -0.5f, WarpCorner1Y = -0.5f,
                             WarpCorner2X = 0.5f, WarpCorner2Y = 0.5f,
                             WarpCorner3X = 0.5f, WarpCorner3Y = -0.5f,
                             WarpCorner4X = -0.5f, WarpCorner4Y = 0.5f,
                         };

        var input = new[] { new LaserPoint(100, 200, 65535, 0, 0, 65535) };
        var mapped = LaserPipeline.WarpFrameIntoQuad(input, input.Length, safety);

        Assert.Null(mapped);
    }

    [Fact]
    public void SafetyApply_ZeroIntensityWithColour_IsDarkAfterwards()
    {
        // I = 0 is the blanking gate, but PONK/CITP carry no I and many projectors ignore it:
        // the colour must go too, or the point bypasses every safety stage and is drawn lit.
        var config = LaserSafetyTransforms.Config.Default;
        var stats = new LaserSafetyTransforms.Stats();
        var points = new List<LaserPoint>
                         {
                             new(0, 0, 65535, 65535, 65535, 65535),
                             new(1000, 0, 65535, 65535, 65535, 0),
                             new(2000, 0, 65535, 0, 0, -5),
                         };

        var result = LaserSafetyTransforms.Apply(points, in config, ref stats);

        foreach (var p in result)
        {
            if (p.I <= 0)
                Assert.True(p.R == 0 && p.G == 0 && p.B == 0 && p.I == 0);
        }
    }

    [Fact]
    public void StaticDwell_FrameThatNeverMoves_IsBlankedEntirely()
    {
        // A DAC replays the frame until the next one: 10 lit points at one spot is a static
        // beam for as long as it is shown, however far below the per-frame cap it is.
        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStaticPoints = 64;
        var stats = new LaserSafetyTransforms.Stats();
        var points = new List<LaserPoint>();
        for (var i = 0; i < 10; i++)
            points.Add(new LaserPoint(100, 100, 65535, 65535, 65535, 65535));

        LaserSafetyTransforms.LimitStaticDwell(points, in config, ref stats);

        Assert.All(points, p => Assert.Equal(0, p.I));
        Assert.Equal(10, stats.StaticBlanked);
    }

    [Fact]
    public void StaticDwell_RunContinuesOverTheLoopSeam()
    {
        // 40 static points at the end and 40 at the start of the same spot form one run of 80
        // when looped; a moving segment in the middle keeps the frame from being fully static.
        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStaticPoints = 64;
        var stats = new LaserSafetyTransforms.Stats();
        var points = new List<LaserPoint>();
        for (var i = 0; i < 40; i++)
            points.Add(new LaserPoint(100, 100, 65535, 65535, 65535, 65535));
        for (var i = 0; i < 10; i++)
            points.Add(LaserPoint.CreateBlanked(1000 * (i + 1), 0));
        for (var i = 0; i < 40; i++)
            points.Add(new LaserPoint(100, 100, 65535, 65535, 65535, 65535));

        LaserSafetyTransforms.LimitStaticDwell(points, in config, ref stats);

        Assert.Equal(16, stats.StaticBlanked);
    }

    [Fact]
    public void StaticDwell_BlankingInPlaceDoesNotResetTheRun()
    {
        // Blinking at one spot is still dwelling there: only movement ends a run.
        var config = LaserSafetyTransforms.Config.Default;
        config.MaxStaticPoints = 10;
        var stats = new LaserSafetyTransforms.Stats();
        var points = new List<LaserPoint> { LaserPoint.CreateBlanked(20000, 20000) };
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 5; j++)
                points.Add(new LaserPoint(100, 100, 65535, 65535, 65535, 65535));
            points.Add(LaserPoint.CreateBlanked(100, 100));
        }

        LaserSafetyTransforms.LimitStaticDwell(points, in config, ref stats);

        Assert.Equal(10, stats.StaticBlanked);
    }

    [Fact]
    public void BrightnessCeiling_ScalesIntensityChannel()
    {
        var config = LaserSafetyTransforms.Config.Default;
        config.MaxBrightness = 0.25f;
        var stats = new LaserSafetyTransforms.Stats();
        var points = new List<LaserPoint> { new(0, 0, 0, 0, 0, 65535), new(10, 0, 65535, 0, 0, 2) };

        LaserSafetyTransforms.ClampIntensity(points, in config, ref stats);

        Assert.Equal(16383, points[0].I);
        Assert.Equal(LaserPoint.CreateBlanked(10, 0), points[1]); // rounds to dark: canonical blank
    }

    [Fact]
    public void PipelineStatus_IsBuiltOnRequestOnly()
    {
        var settings = new LaserProcessingSettings();
        settings.Safety.Calibrated = true;
        var session = new LaserPipeline.Session();
        var input = new[] { new LaserPoint(0, 0, 65535, 0, 0, 65535), new LaserPoint(1000, 0, 65535, 0, 0, 65535) };

        session.Apply(settings, input, input.Length, out int frameCount);

        Assert.True(frameCount > 0);
        Assert.Contains("points", session.DescribeLastRun());
        session.Apply(settings, null, 0, out frameCount);
        Assert.Equal("No input points", session.DescribeLastRun());
    }

    private static LaserPoint[] StraightLine(int count)
    {
        var line = new LaserPoint[count];
        for (var i = 0; i < count; i++)
            line[i] = new LaserPoint(i * 200, 0, 65535, 65535, 65535, 65535);
        return line;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Optimizer_StraightLine_IsNeitherDweltNorDotted(bool reorder)
    {
        // 180 degrees is straight: no corner anchors, and no blanked samples between lit points.
        var config = LaserOptimizerTransforms.Config.Default;
        config.EnableOptimization = reorder;
        var line = StraightLine(20);

        var result = LaserOptimizerTransforms.Apply(line, line.Length, config, new LaserOptimizerTransforms.Scratch());

        Assert.Equal(20, result.Length);
        Assert.All(result, p => Assert.True(p.I > 0));
    }

    [Fact]
    public void Optimizer_SpikeBelowThreshold_GetsCornerAnchors()
    {
        // Interior angle at the tip of this spike is ~11 degrees, below the default 30.
        var spike = new[]
                        {
                            new LaserPoint(0, 0, 65535, 65535, 65535, 65535),
                            new LaserPoint(10000, 1000, 65535, 65535, 65535, 65535),
                            new LaserPoint(0, 2000, 65535, 65535, 65535, 65535),
                        };
        var config = LaserOptimizerTransforms.Config.Default;

        var result = LaserOptimizerTransforms.Apply(spike, spike.Length, config, new LaserOptimizerTransforms.Scratch());

        var tipCount = 0;
        foreach (var p in result)
        {
            if (p.X == 10000 && p.Y == 1000)
                tipCount++;
        }

        Assert.Equal(config.CornerAnchorCount, tipCount);
    }

    [Fact]
    public void Optimizer_HugeConcentratedFrame_KeepsEveryLitPoint()
    {
        // Thousands of one-point shapes piled on a few spots: too dense for the tour grid and too
        // many for the quadratic sequential tour - the frame keeps its order but loses nothing.
        const int shapes = 6000;
        var input = new LaserPoint[shapes * 2];
        for (var i = 0; i < shapes; i++)
        {
            input[2 * i] = new LaserPoint(i % 3, 0, 65535, 0, 0, 65535);
            input[2 * i + 1] = LaserPoint.CreateBlanked(i % 3, 0);
        }

        var result = LaserOptimizerTransforms.Apply(input, input.Length, LaserOptimizerTransforms.Config.Default,
                                                    new LaserOptimizerTransforms.Scratch());

        var lit = 0;
        foreach (var p in result)
        {
            if (p.I > 0)
                lit++;
        }

        Assert.True(lit >= shapes);
    }
}
