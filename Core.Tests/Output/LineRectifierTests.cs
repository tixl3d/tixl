using System;
using System.Numerics;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

/// <summary>
/// Validates the rectifier against a synthetic keystone: take lines that are axis-aligned in the true metric
/// space, push them through a known distortion to get what the user would have traced in a mis-pinned
/// surface, solve from those traces alone, and require the result to bring them back to axis-aligned.
/// </summary>
public class LineRectifierTests
{
    [Fact]
    public void Keystone_RecoversAxisAlignedLines()
    {
        var size = new Vector2(4, 3);
        var distortion = Keystone(size);

        var horizontal = new[]
                             {
                                 Distort(distortion, 0.4f, 0.6f, 3.6f, 0.6f),
                                 Distort(distortion, 0.4f, 2.4f, 3.6f, 2.4f),
                             };
        var vertical = new[]
                           {
                               Distort(distortion, 0.9f, 0.3f, 0.9f, 2.7f),
                               Distort(distortion, 3.1f, 0.3f, 3.1f, 2.7f),
                           };

        Assert.True(LineRectifier.TrySolve(horizontal, vertical, size, out var rectify));

        foreach (var line in horizontal)
        {
            var a = rectify.TransformPoint(new Vector2(line.X, line.Y));
            var b = rectify.TransformPoint(new Vector2(line.Z, line.W));
            Assert.True(Math.Abs(a.Y - b.Y) < 0.001f, $"horizontal line not level: {a} .. {b}");
        }

        foreach (var line in vertical)
        {
            var a = rectify.TransformPoint(new Vector2(line.X, line.Y));
            var b = rectify.TransformPoint(new Vector2(line.Z, line.W));
            Assert.True(Math.Abs(a.X - b.X) < 0.001f, $"vertical line not plumb: {a} .. {b}");
        }
    }

    /// <summary>The surface keeps its extent — aspect and scale are the measured lengths' job, not this one's.</summary>
    [Fact]
    public void Keystone_PreservesSurfaceExtent()
    {
        var size = new Vector2(4, 3);
        var distortion = Keystone(size);

        var horizontal = new[]
                             {
                                 Distort(distortion, 0.4f, 0.6f, 3.6f, 0.6f),
                                 Distort(distortion, 0.4f, 2.4f, 3.6f, 2.4f),
                             };
        var vertical = new[]
                           {
                               Distort(distortion, 0.9f, 0.3f, 0.9f, 2.7f),
                               Distort(distortion, 3.1f, 0.3f, 3.1f, 2.7f),
                           };

        Assert.True(LineRectifier.TrySolve(horizontal, vertical, size, out var rectify));

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var corner in new[] { Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y) })
        {
            var p = rectify.TransformPoint(corner);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        Assert.True((min - Vector2.Zero).Length() < 0.001f, $"origin {min}");
        Assert.True((max - size).Length() < 0.001f, $"extent {max} vs {size}");
    }

    [Fact]
    public void TooFewLines_Fails()
    {
        var size = new Vector2(4, 3);
        var one = new[] { new Vector4(0, 1, 4, 1) };
        var two = new[] { new Vector4(1, 0, 1, 3), new Vector4(3, 0, 3, 3) };

        Assert.False(LineRectifier.TrySolve(one, two, size, out _));
        Assert.False(LineRectifier.TrySolve(two, one, size, out _));
    }

    [Fact]
    public void AlreadyStraight_IsANoOp()
    {
        var size = new Vector2(4, 3);
        var horizontal = new[] { new Vector4(0.4f, 0.6f, 3.6f, 0.6f), new Vector4(0.4f, 2.4f, 3.6f, 2.4f) };
        var vertical = new[] { new Vector4(0.9f, 0.3f, 0.9f, 2.7f), new Vector4(3.1f, 0.3f, 3.1f, 2.7f) };

        Assert.True(LineRectifier.TrySolve(horizontal, vertical, size, out var rectify));

        var probe = new Vector2(1.7f, 2.1f);
        Assert.True((rectify.TransformPoint(probe) - probe).Length() < 0.001f);
    }

    /// <summary>
    /// The case the closed-form solve can't take: three lines on one axis and only one on the other, which
    /// leaves the horizon under-determined. The regularization toward the starting quad is what resolves it.
    /// </summary>
    [Fact]
    public void Refine_RecoversQuadFromLopsidedLines()
    {
        var size = new Vector2(4, 3);

        // The true pin, and the mis-aligned one the user starts from.
        Vector2[] trueQuad = [new(120, 90), new(1800, 60), new(1840, 1150), new(80, 1120)];
        Vector2[] startQuad = [new(120, 90), new(1800, 60), new(1790, 1120), new(80, 1120)];

        Assert.True(Homography.TryComputeQuadToQuad(RectOf(size), trueQuad, out var surfaceToOutput));

        // What the user aimed at real features: axis-aligned in the *true* space, recorded in output pixels.
        var lines = new[]
                        {
                            ToOutput(surfaceToOutput, 0.4f, 0.6f, 3.6f, 0.6f),
                            ToOutput(surfaceToOutput, 0.9f, 0.3f, 0.9f, 2.7f),
                            ToOutput(surfaceToOutput, 2.0f, 0.3f, 2.0f, 2.7f),
                            ToOutput(surfaceToOutput, 3.1f, 0.3f, 3.1f, 2.7f),
                        };

        var refined = new Vector2[4];
        Assert.True(LineRectifier.TryRefineQuad(lines, size, startQuad, refined));

        Assert.True(Homography.TryComputeQuadToQuad(refined, RectOf(size), out var outputToSurface));
        foreach (var line in lines)
        {
            var a = outputToSurface.TransformPoint(new Vector2(line.X, line.Y));
            var b = outputToSurface.TransformPoint(new Vector2(line.Z, line.W));
            LineRectifier.IsHorizontal(a, b, out var deviation);
            Assert.True(deviation < 0.2f, $"residual {deviation:0.###}° on {a} .. {b}");
        }
    }

    /// <summary>An already-aligned pin has nothing to gain, and the regularization must keep it put.</summary>
    [Fact]
    public void Refine_LeavesAnAlignedQuadAlone()
    {
        var size = new Vector2(4, 3);
        Vector2[] quad = [new(100, 100), new(1800, 100), new(1800, 1200), new(100, 1200)];

        Assert.True(Homography.TryComputeQuadToQuad(RectOf(size), quad, out var surfaceToOutput));
        var lines = new[]
                        {
                            ToOutput(surfaceToOutput, 0.4f, 0.6f, 3.6f, 0.6f),
                            ToOutput(surfaceToOutput, 0.9f, 0.3f, 0.9f, 2.7f),
                        };

        var refined = new Vector2[4];
        Assert.True(LineRectifier.TryRefineQuad(lines, size, quad, refined));

        for (var i = 0; i < 4; i++)
            Assert.True((refined[i] - quad[i]).Length() < 1f, $"corner {i} drifted to {refined[i]} from {quad[i]}");
    }

    /// <summary>
    /// Straighten is not a ratchet: pressing it again must not keep reshaping the picture. Hand-traced lines
    /// never agree perfectly, and an angular score cannot see scale — so squashing one axis brings every line
    /// *relatively* closer to it, and each press would trade a little more of the wall's shape for a little
    /// less residual. Measured before the gauge was pinned, five presses took the aspect from 1.58 to 0.73.
    /// </summary>
    [Fact]
    public void Refine_RepeatedStraightenKeepsTheShape()
    {
        var size = new Vector2(4, 3);
        Vector2[] startQuad = [new(300, 120), new(1750, 40), new(1900, 1180), new(60, 1020)];
        Assert.True(Homography.TryComputeQuadToQuad(startQuad, RectOf(size), out var outputToSurface));
        Assert.True(Homography.TryComputeQuadToQuad(RectOf(size), startQuad, out var surfaceToOutput));

        // Five lines that each claim an axis and disagree with one another by a fraction of a degree, so no
        // quad can bring the residual to zero and every press still has something to trade.
        var lines = new[]
                        {
                            ToOutput(surfaceToOutput, 0.4f, 0.6f, 3.6f, 0.68f),
                            ToOutput(surfaceToOutput, 0.4f, 2.3f, 3.6f, 2.18f),
                            ToOutput(surfaceToOutput, 0.5f, 1.4f, 3.5f, 1.52f),
                            ToOutput(surfaceToOutput, 0.9f, 0.3f, 0.96f, 2.7f),
                            ToOutput(surfaceToOutput, 3.1f, 0.3f, 3.02f, 2.7f),
                        };

        var spans = AxisSpans(startQuad);
        var current = startQuad;
        for (var press = 1; press <= 5; press++)
        {
            var refined = new Vector2[4];
            Assert.True(LineRectifier.TryRefineQuad(lines, size, current, refined));
            AssertSpans(spans, AxisSpans(refined), $"after press {press}");
            current = refined;
        }

        // And it settles: the fifth press has nothing left to move.
        var again = new Vector2[4];
        Assert.True(LineRectifier.TryRefineQuad(lines, size, current, again));
        for (var i = 0; i < 4; i++)
            Assert.True((again[i] - current[i]).Length() < 2f, $"corner {i} still moving: {current[i]} -> {again[i]}");

        // The residual stays — inconsistent traces cannot be made to agree by any keystone, and pretending
        // otherwise is exactly what the drift was.
        Assert.True(ResidualOf(outputToSurface, lines[0]) > 0.05f);
    }

    /// <summary>
    /// A short line states its direction from two close-together ends, so the same imprecision reads as a far
    /// bigger angle: it is the weaker measurement and must not outvote a long one drawn along the same wall.
    /// </summary>
    [Fact]
    public void Refine_TrustsTheLongerLine()
    {
        var size = new Vector2(4, 3);
        Vector2[] quad = [new(100, 100), new(1800, 100), new(1800, 1200), new(100, 1200)];
        Assert.True(Homography.TryComputeQuadToQuad(RectOf(size), quad, out var surfaceToOutput));

        // One long line along a real feature, and a short one traced 3° off it.
        var lines = new[]
                        {
                            ToOutput(surfaceToOutput, 0.2f, 1.5f, 3.8f, 1.5f),
                            ToOutput(surfaceToOutput, 1.0f, 0.5f, 1.4f, 0.5f + 0.4f * MathF.Tan(3f * MathF.PI / 180f)),
                        };

        var refined = new Vector2[4];
        Assert.True(LineRectifier.TryRefineQuad(lines, size, quad, refined));
        Assert.True(Homography.TryComputeQuadToQuad(refined, RectOf(size), out var outputToSurface));

        var longResidual = ResidualOf(outputToSurface, lines[0]);
        var shortResidual = ResidualOf(outputToSurface, lines[1]);
        Assert.True(longResidual < 0.3f, $"the long line was pulled off its feature: {longResidual:0.###}°");
        Assert.True(shortResidual > longResidual * 3, $"the short line had as much say: {shortResidual:0.###}° vs {longResidual:0.###}°");
    }

    private static float ResidualOf(in Homography outputToSurface, Vector4 line)
    {
        var a = outputToSurface.TransformPoint(new Vector2(line.X, line.Y));
        var b = outputToSurface.TransformPoint(new Vector2(line.Z, line.W));
        LineRectifier.IsHorizontal(a, b, out var deviation);
        return deviation;
    }

    /// <summary>A quad's span along each of its axes, as the solver's gauge measures it.</summary>
    private static Vector2 AxisSpans(Vector2[] quad)
    {
        return new Vector2(((quad[1] - quad[0]).Length() + (quad[2] - quad[3]).Length()) * 0.5f,
                           ((quad[3] - quad[0]).Length() + (quad[2] - quad[1]).Length()) * 0.5f);
    }

    private static void AssertSpans(Vector2 expected, Vector2 actual, string when)
    {
        Assert.True(MathF.Abs(actual.X / expected.X - 1) < 0.01f, $"width changed {when}: {expected.X:0.#} -> {actual.X:0.#}");
        Assert.True(MathF.Abs(actual.Y / expected.Y - 1) < 0.01f, $"height changed {when}: {expected.Y:0.#} -> {actual.Y:0.#}");
    }

    private static Vector2[] RectOf(Vector2 size)
    {
        return [Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y)];
    }

    private static Vector4 ToOutput(in Homography h, float x1, float y1, float x2, float y2)
    {
        var a = h.TransformPoint(new Vector2(x1, y1));
        var b = h.TransformPoint(new Vector2(x2, y2));
        return new Vector4(a.X, a.Y, b.X, b.Y);
    }

    /// <summary>The mis-pinned surface a user would be tracing on: the true rect seen through a keystone.</summary>
    private static Homography Keystone(Vector2 size)
    {
        Vector2[] source = [Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y)];
        Vector2[] destination =
            [
                new Vector2(0.3f, 0.1f),
                new Vector2(size.X - 0.05f, -0.2f),
                new Vector2(size.X + 0.4f, size.Y + 0.3f),
                new Vector2(-0.2f, size.Y - 0.15f),
            ];

        Assert.True(Homography.TryComputeQuadToQuad(source, destination, out var h));
        return h;
    }

    private static Vector4 Distort(in Homography h, float x1, float y1, float x2, float y2)
    {
        var a = h.TransformPoint(new Vector2(x1, y1));
        var b = h.TransformPoint(new Vector2(x2, y2));
        return new Vector4(a.X, a.Y, b.X, b.Y);
    }
}
