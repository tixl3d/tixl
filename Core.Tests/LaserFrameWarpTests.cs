using System;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Tests for the laser frame-warp keystone (LaserPipeline.WarpFrameIntoQuad +
/// BuildSetupQuad): the corner mapping that replaces the setup rectangle when a laser
/// output's safety has FrameWarp on — the "trapeze the basic screen" feature.
/// </summary>
public class LaserFrameWarpTests
{
    private static LaserProcessingSettings.SafetySettings SafetyWithCorners(
        (float X, float Y) tl, (float X, float Y) tr, (float X, float Y) br, (float X, float Y) bl)
    {
        return new LaserProcessingSettings.SafetySettings
               {
                   WarpCorner1X = tl.X, WarpCorner1Y = tl.Y,
                   WarpCorner2X = tr.X, WarpCorner2Y = tr.Y,
                   WarpCorner3X = br.X, WarpCorner3Y = br.Y,
                   WarpCorner4X = bl.X, WarpCorner4Y = bl.Y,
               };
    }

    [Fact]
    public void WarpFrameIntoQuad_WithRectCorners_MapsFieldEdgesOntoCorners()
    {
        var safety = SafetyWithCorners((-0.5f, 0.5f), (0.5f, 0.5f), (0.5f, -0.5f), (-0.5f, -0.5f));

        var input = new[]
                        {
                            new LaserPoint(-32767, 32767, 65535, 0, 0),   // field top-left
                            new LaserPoint(32767, -32767, 0, 65535, 0),   // field bottom-right
                            new LaserPoint(0, 0, 0, 0, 65535),            // center stays center
                        };

        var mapped = LaserPipeline.WarpFrameIntoQuad(input, input.Length, safety);

        Assert.NotNull(mapped);
        // The corners of the frame land on the quad's corners: ±0.5 field = ±16383.5, so ±16384/±16383.
        Assert.InRange(Math.Abs(mapped![0].X), 16380, 16388);
        Assert.InRange(Math.Abs(mapped[0].Y), 16380, 16388);
        Assert.InRange(Math.Abs(mapped[1].X), 16380, 16388);
        Assert.InRange(Math.Abs(mapped[1].Y), 16380, 16388);
        Assert.Equal(0, mapped[2].X);
        Assert.Equal(0, mapped[2].Y);
        // Colors survive the warp
        Assert.Equal(65535, mapped[0].R);
        Assert.Equal(65535, mapped[1].G);
        Assert.Equal(65535, mapped[2].B);
    }

    [Fact]
    public void WarpFrameIntoQuad_WithTrapezoid_KeepsTopEdgeShorter()
    {
        // Keystone: the top edge pulled in on both sides - the classic trapezoid.
        var safety = SafetyWithCorners((-0.25f, 0.5f), (0.25f, 0.5f), (0.5f, -0.5f), (-0.5f, -0.5f));

        var input = new[]
                        {
                            new LaserPoint(-32767, 32767, 65535, 0, 0),
                            new LaserPoint(32767, 32767, 65535, 0, 0),
                        };

        var mapped = LaserPipeline.WarpFrameIntoQuad(input, input.Length, safety);

        Assert.NotNull(mapped);
        // The frame's top edge maps to the short top edge: half the width of the bottom edge.
        var topWidth = mapped![1].X - mapped[0].X;
        Assert.InRange(topWidth, 16300, 16500);
    }

    [Fact]
    public void WarpFrameIntoQuad_WithDegenerateQuad_FailsClosed()
    {
        // Three collinear corners: no homography exists. The frame goes dark (null) rather than
        // falling back to the unwarped full field the user did not aim at.
        var safety = SafetyWithCorners((-0.5f, 0f), (0.5f, 0f), (0.5f, 0f), (-0.5f, -0.5f));

        var input = new[] { new LaserPoint(-32767, 32767, 65535, 0, 0) };
        var mapped = LaserPipeline.WarpFrameIntoQuad(input, input.Length, safety);

        Assert.Null(mapped);
    }

    [Fact]
    public void WarpFrameIntoQuad_WithEmptyInput_ReturnsInput()
    {
        var safety = SafetyWithCorners((-0.5f, 0.5f), (0.5f, 0.5f), (0.5f, -0.5f), (-0.5f, -0.5f));
        var input = new[] { new LaserPoint(0, 0, 0, 0, 0) };

        Assert.Null(LaserPipeline.WarpFrameIntoQuad(null, 0, safety));
        Assert.Same(input, LaserPipeline.WarpFrameIntoQuad(input, 0, safety));
    }

    [Fact]
    public void BuildSetupQuad_TracesAllFourSidesGreen()
    {
        var quad = new[]
                       {
                           new Vector2(-16384, 16384), new Vector2(16384, 16384),
                           new Vector2(16384, -16384), new Vector2(-16384, -16384),
                       };

        var points = LaserPipeline.BuildSetupQuad(quad);

        Assert.True(points.Length > 8);
        // Every point is green or a blanked jump point
        foreach (var p in points)
        {
            Assert.True(p.R == 0);
            Assert.True(p.G is 0 or 65535);
            Assert.True(p.B == 0);
        }

        // The first corner lands on the quad's first corner
        Assert.Equal(-16384, points[1].X);
        Assert.Equal(16384, points[1].Y);
    }
}
