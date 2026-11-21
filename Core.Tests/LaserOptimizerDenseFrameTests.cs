using System;
using System.Diagnostics;
using Xunit;

namespace Core.Tests;

/// <summary>
/// The greedy tour answers "nearest unvisited segment" through a uniform grid once a frame has
/// more than a few hundred lit segments, because the exhaustive scan is quadratic and a mesh
/// wireframe frame has thousands of them (seconds per frame, which the stream watchdog then
/// punishes by blanking the beam). These pin the properties that must survive that switch.
/// </summary>
public class LaserOptimizerDenseFrameTests
{
    /// <summary>Dense, clustered strokes: what a [LaserCamera] mesh capture produces.</summary>
    private static T3.Core.DataTypes.LaserPoint[] BuildDenseFrame(int segmentCount, int pointsPerSegment)
    {
        var points = new System.Collections.Generic.List<T3.Core.DataTypes.LaserPoint>(segmentCount * (pointsPerSegment + 1));
        for (var s = 0; s < segmentCount; s++)
        {
            var x0 = (s % 60) * 500 - 15000;
            var y0 = (s / 60) * 500 - 15000;
            for (var i = 0; i < pointsPerSegment; i++)
                points.Add(new T3.Core.DataTypes.LaserPoint(x0 + i * 7, y0 + i * 3, 65535, 0, 0, 65535));

            points.Add(T3.Core.DataTypes.LaserPoint.CreateBlanked(x0, y0));
        }

        return points.ToArray();
    }

    [Fact]
    public void DenseFrameToursInMilliseconds()
    {
        var frame = BuildDenseFrame(4000, 5);
        var config = T3.Core.DataTypes.LaserOptimizerTransforms.Config.Default;
        var scratch = new T3.Core.DataTypes.LaserOptimizerTransforms.Scratch();

        // Warm up both the grid build and the JIT, then measure a steady-state frame.
        T3.Core.DataTypes.LaserOptimizerTransforms.Apply(frame, frame.Length, config, scratch);

        var sw = Stopwatch.StartNew();
        var optimized = T3.Core.DataTypes.LaserOptimizerTransforms.Apply(frame, frame.Length, config, scratch);
        sw.Stop();

        Assert.NotEmpty(optimized);
        // The exhaustive scan needed ~350 ms for this shape and grew quadratically from there;
        // the budget is deliberately loose so slow CI machines do not make this flaky.
        Assert.True(sw.Elapsed.TotalMilliseconds < 150,
                    $"optimizing a {frame.Length}-point / 4000-segment frame took {sw.Elapsed.TotalMilliseconds:0.0} ms");

        // Same scratch again: the tour unlinks consumed segments from the grid, and the next
        // frame's rebuild must restore it exactly - no shape may go missing frame to frame.
        var third = T3.Core.DataTypes.LaserOptimizerTransforms.Apply(frame, frame.Length, config, scratch);
        Assert.Equal(optimized.Length, third.Length);
        for (var i = 0; i < optimized.Length; i++)
            Assert.Equal(optimized[i], third[i]);
    }

    [Fact]
    public void DenseTourIsDeterministic()
    {
        var frame = BuildDenseFrame(800, 4);
        var config = T3.Core.DataTypes.LaserOptimizerTransforms.Config.Default;

        var first = T3.Core.DataTypes.LaserOptimizerTransforms.Apply(frame, frame.Length, config,
                                                                     new T3.Core.DataTypes.LaserOptimizerTransforms.Scratch());
        var second = T3.Core.DataTypes.LaserOptimizerTransforms.Apply(frame, frame.Length, config,
                                                                      new T3.Core.DataTypes.LaserOptimizerTransforms.Scratch());

        Assert.Equal(first.Length, second.Length);
        for (var i = 0; i < first.Length; i++)
            Assert.Equal(first[i], second[i]);
    }

    /// <summary>Sparse frames: dots and wireframe edges spread over the whole field, the way a
    /// LaserCamera capture of a real scene looks. Ring expansion of the tour grid degenerates
    /// here - the nearest unvisited segment is far away, so every query sweeps thousands of
    /// empty cells before it finds a hit.</summary>
    private static T3.Core.DataTypes.LaserPoint[] BuildSparseFrame(int segmentCount, int pointsPerSegment, int seed)
    {
        var random = new Random(seed);
        var points = new System.Collections.Generic.List<T3.Core.DataTypes.LaserPoint>(segmentCount * (pointsPerSegment + 1));
        for (var s = 0; s < segmentCount; s++)
        {
            var x0 = random.Next(-32000, 32000);
            var y0 = random.Next(-32000, 32000);
            var dx = (short)random.Next(-3000, 3000);
            var dy = (short)random.Next(-3000, 3000);
            for (var i = 0; i < pointsPerSegment; i++)
                points.Add(new T3.Core.DataTypes.LaserPoint((short)(x0 + dx * i / pointsPerSegment), (short)(y0 + dy * i / pointsPerSegment), 65535, 65535, 65535, 65535));

            points.Add(T3.Core.DataTypes.LaserPoint.CreateBlanked(x0 + dx, y0 + dy));
        }

        return points.ToArray();
    }

    [Fact]
    public void SparseFramesStayFast()
    {
        var config = T3.Core.DataTypes.LaserOptimizerTransforms.Config.Default;

        // A dot cloud: each dot is a blanked jump + a short dwell run, ~2400 segments in 12k points.
        var dots = BuildSparseFrame(2000, 4, 7);
        // A wireframe: ~3000 short edges in 12k points.
        var edges = BuildSparseFrame(3000, 3, 11);

        var scratch = new T3.Core.DataTypes.LaserOptimizerTransforms.Scratch();
        T3.Core.DataTypes.LaserOptimizerTransforms.Apply(dots, dots.Length, config, scratch); // warm the JIT

        // Best of several runs: a wall-clock bound has to ignore a scheduler hiccup on a loaded machine,
        // while a quadratic regression is slow on every run.
        var dotsMs = double.MaxValue;
        var edgesMs = double.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            var sw = Stopwatch.StartNew();
            T3.Core.DataTypes.LaserOptimizerTransforms.Apply(dots, dots.Length, config, scratch);
            dotsMs = Math.Min(dotsMs, sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            T3.Core.DataTypes.LaserOptimizerTransforms.Apply(edges, edges.Length, config, scratch);
            edgesMs = Math.Min(edgesMs, sw.Elapsed.TotalMilliseconds);
        }

        // Loose enough for slow CI, tight enough to catch a quadratic regression (which lands
        // in the hundreds of ms for these sizes and stalls the editor frame).
        Assert.True(dotsMs < 100, $"sparse dot frame ({dots.Length} points) took {dotsMs:0.0} ms");
        Assert.True(edgesMs < 100, $"sparse edge frame ({edges.Length} points) took {edgesMs:0.0} ms");
    }

    /// <summary>What a LaserCamera mesh capture really produces: thousands of wireframe edges
    /// projected into a SMALL screen region - nearly every segment lands in one or two grid
    /// cells. The grid's linked lists made every tour query sweep the whole remaining blob
    /// (~390 ms measured in the field); the compact remaining-list scan answers the same query
    /// sequentially.</summary>
    private static T3.Core.DataTypes.LaserPoint[] BuildConcentratedFrame(int segmentCount, int pointsPerSegment, int seed)
    {
        var random = new Random(seed);
        var points = new System.Collections.Generic.List<T3.Core.DataTypes.LaserPoint>(segmentCount * (pointsPerSegment + 1));
        for (var s = 0; s < segmentCount; s++)
        {
            var x0 = random.Next(-2000, 2000);
            var y0 = random.Next(-2000, 2000);
            var dx = (short)random.Next(-400, 400);
            var dy = (short)random.Next(-400, 400);
            for (var i = 0; i < pointsPerSegment; i++)
                points.Add(new T3.Core.DataTypes.LaserPoint((short)(x0 + dx * i / pointsPerSegment), (short)(y0 + dy * i / pointsPerSegment), 65535, 65535, 65535, 65535));

            points.Add(T3.Core.DataTypes.LaserPoint.CreateBlanked(x0 + dx, y0 + dy));
        }

        return points.ToArray();
    }

    [Fact]
    public void ConcentratedFramesStayFast()
    {
        var config = T3.Core.DataTypes.LaserOptimizerTransforms.Config.Default;
        var frame = BuildConcentratedFrame(3104, 2, 5); // the field-reproduced shape: 3104 tiny edges, 9312 points

        var scratch = new T3.Core.DataTypes.LaserOptimizerTransforms.Scratch();
        T3.Core.DataTypes.LaserOptimizerTransforms.Apply(frame, frame.Length, config, scratch); // warm the JIT

        var sw = Stopwatch.StartNew();
        var optimized = T3.Core.DataTypes.LaserOptimizerTransforms.Apply(frame, frame.Length, config, scratch);
        var ms = sw.Elapsed.TotalMilliseconds;

        Assert.NotEmpty(optimized);
        // The grid tour measured ~390 ms for exactly this shape; sequential answering brings the
        // bound to a fraction of the pump budget even on slow CI.
        Assert.True(ms < 60, $"concentrated frame ({frame.Length} points / 3104 segments) took {ms:0.0} ms");
    }
}
