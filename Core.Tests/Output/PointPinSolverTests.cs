using System;
using System.Numerics;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

public class PointPinSolverTests
{
    private static readonly Vector2[] _rect = [new(100, 100), new(500, 100), new(500, 400), new(100, 400)];

    [Fact]
    public void OnePointShiftsTheWholePin()
    {
        var quad = (Vector2[])_rect.Clone();
        Assert.True(PointPinSolver.TrySolve([new Vector2(200, 200)], [new Vector2(230, 190)], quad, out var residual));
        Assert.Equal(0f, residual);
        for (var c = 0; c < 4; c++)
            AssertNear(_rect[c] + new Vector2(30, -10), quad[c]);
    }

    [Fact]
    public void TwoPointsTurnAndScale()
    {
        var quad = (Vector2[])_rect.Clone();
        // Rotate 90° about the first point and double the distance between them.
        Vector2[] from = [new(200, 200), new(300, 200)];
        Vector2[] to = [new(200, 200), new(200, 400)];
        Assert.True(PointPinSolver.TrySolve(from, to, quad, out _));
        AssertNear(new Vector2(400, 0), quad[0]);   // (100,100): dx=-100,dy=-100 → rotated (100,-100)*2 = (200,-200) + (200,200)
        AssertNear(new Vector2(400, 800), quad[1]);
    }

    [Fact]
    public void FourPointsAreHitExactly()
    {
        var quad = (Vector2[])_rect.Clone();
        Vector2[] from = [new(150, 150), new(450, 150), new(450, 350), new(150, 350)];
        Vector2[] to = [new(160, 140), new(470, 170), new(440, 360), new(130, 330)];
        Assert.True(PointPinSolver.TrySolve(from, to, quad, out var residual));
        Assert.True(residual < 0.01f);

        Assert.True(Homography.TryComputeQuadToQuad(_rect, quad, out var solved));
        for (var i = 0; i < 4; i++)
            AssertNear(to[i], solved.TransformPoint(from[i]));
    }

    [Fact]
    public void FivePointsReportTheMiss()
    {
        var quad = (Vector2[])_rect.Clone();
        Vector2[] from = [new(150, 150), new(450, 150), new(450, 350), new(150, 350), new(300, 250)];
        Vector2[] to = [new(150, 150), new(450, 150), new(450, 350), new(150, 350), new(300, 270)];
        Assert.True(PointPinSolver.TrySolve(from, to, quad, out var residual));
        Assert.True(residual > 5f && residual < 20f);
    }

    private static void AssertNear(Vector2 expected, Vector2 actual)
    {
        Assert.True((expected - actual).Length() < 0.01f, $"expected {expected}, got {actual}");
    }
}
