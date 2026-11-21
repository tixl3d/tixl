using T3.Core.DataTypes;
using Xunit;
using Xunit.Abstractions;

namespace Core.Tests;

/// <summary>
/// Regression for the oscilloscope report: the corner-dwelling pass probed an unwritten
/// scratch slot (always 0,0) for its angle, injecting two zero samples per shape - the beam
/// walked to the middle between every dot. Pins the fix against the real camera shape:
/// dots encoded as 12-segment circles with genuinely sharp corners.
/// </summary>
public class LaserCornerDwellZeroVisitTests
{
    private readonly ITestOutputHelper _log;
    public LaserCornerDwellZeroVisitTests(ITestOutputHelper log) => _log = log;

    private static LaserPoint[] BuildCircleDotsFrame(int dots, int dotDwellUnused = 0)
    {
        // Camera encoding for radius >= 2 dots: 1 blanked at the dot + a closed 12-segment
        // lit circle around it (AppendDots' isCircle path).
        const int segments = 12;
        var pts = new System.Collections.Generic.List<LaserPoint>(dots * (segments + 3));
        for (var i = 0; i < dots; i++)
        {
            var a = i / (float)dots * 2f * System.MathF.PI;
            var cx = (int)(System.MathF.Cos(a) * 20000f);
            var cy = (int)(System.MathF.Sin(a) * 12000f);
            pts.Add(LaserPoint.CreateBlanked(cx, cy));
            for (var k = 0; k <= segments; k++)
            {
                var t = k * (System.MathF.PI * 2f) / segments;
                pts.Add(new LaserPoint((int)(cx + System.MathF.Cos(t) * 100f),
                                       (int)(cy + System.MathF.Sin(t) * 100f), 0, 65535, 0, 65535));
            }
        }
        return pts.ToArray();
    }

    private static int CountNearNull(LaserPoint[] frame)
    {
        var c = 0;
        foreach (var p in frame)
        {
            if (System.Math.Abs(p.X) < 1500 && System.Math.Abs(p.Y) < 1500)
                c++;
        }
        return c;
    }

    [Fact]
    public void CircleDots_WithCornerAnchors_NeverVisitNull()
    {
        var input = BuildCircleDotsFrame(80);
        var config = new LaserOptimizerTransforms.Config
                         {
                             EnableOptimization = true, MaxJumpDistance = 5000f, BlankingDelayPoints = 5,
                             CornerAngleThreshold = 30f, CornerAnchorCount = 3,
                         };
        var result = LaserOptimizerTransforms.Apply(input, input.Length, config, new LaserOptimizerTransforms.Scratch());
        Assert.Equal(0, CountNearNull(result));
    }

    [Fact]
    public void CircleDots_CornerAnchorsStillWork()
    {
        // The fix must not disable corner dwelling: a sharp triangle corner gets its anchors.
        var input = new[]
                        {
                            LaserPoint.CreateBlanked(0, 10000),
                            new LaserPoint(0, 10000, 0, 65535, 0, 65535),
                            new LaserPoint(3000, 13000, 0, 65535, 0, 65535),
                            new LaserPoint(6000, 10000, 0, 65535, 0, 65535),
                            new LaserPoint(3000, 7000, 0, 65535, 0, 65535),
                            new LaserPoint(0, 10000, 0, 65535, 0, 65535),
                        };
        var config = new LaserOptimizerTransforms.Config
                         {
                             EnableOptimization = true, MaxJumpDistance = 5000f, BlankingDelayPoints = 5,
                             CornerAngleThreshold = 170f, CornerAnchorCount = 3,
                         };
        var result = LaserOptimizerTransforms.Apply(input, input.Length, config, new LaserOptimizerTransforms.Scratch());
        // 6 input points; with near-180° threshold every interior point is a corner and gets
        // 2 anchors: strictly more points than input, and none of them at null.
        Assert.True(result.Length > input.Length, $"anchors missing: {result.Length}");
        Assert.Equal(0, CountNearNull(result));
    }
}
