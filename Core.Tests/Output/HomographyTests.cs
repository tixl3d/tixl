using System;
using System.Numerics;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

public class HomographyTests
{
    [Fact]
    public void IdentityQuad_YieldsIdentity()
    {
        var quad = UnitQuad();
        Assert.True(Homography.TryComputeQuadToQuad(quad, quad, out var h));

        AssertTransformsClose(h, new Vector2(0.3f, 0.7f), new Vector2(0.3f, 0.7f));
    }

    [Fact]
    public void KnownCorners_MapExactly()
    {
        var source = UnitQuad();
        var destination = new[]
                              {
                                  new Vector2(210, 95),
                                  new Vector2(1660, 120),
                                  new Vector2(1655, 940),
                                  new Vector2(215, 905),
                              };

        Assert.True(Homography.TryComputeQuadToQuad(source, destination, out var h));
        for (var i = 0; i < 4; i++)
            AssertTransformsClose(h, source[i], destination[i]);
    }

    [Fact]
    public void InteriorPoint_RoundTripsThroughInverse()
    {
        var source = UnitQuad();
        var destination = new[]
                              {
                                  new Vector2(430, 300),
                                  new Vector2(1450, 180),
                                  new Vector2(1450, 900),
                                  new Vector2(430, 830),
                              };

        Assert.True(Homography.TryComputeQuadToQuad(source, destination, out var h));
        Assert.True(h.TryInvert(out var inverse));

        var p = new Vector2(0.42f, 0.77f);
        var roundTripped = inverse.TransformPoint(h.TransformPoint(p));
        Assert.True((roundTripped - p).Length() < 1e-4f);
    }

    [Fact]
    public void CollinearCorners_Fail()
    {
        var degenerate = new[]
                             {
                                 new Vector2(0, 0),
                                 new Vector2(1, 0),
                                 new Vector2(2, 0),
                                 new Vector2(3, 0),
                             };

        Assert.False(Homography.TryComputeQuadToQuad(UnitQuad(), degenerate, out _));
    }

    [Fact]
    public void SourceCorners_StayOnPositiveWSide()
    {
        // A strongly foreshortened quad exercises the w-sign fix
        var source = new[]
                         {
                             new Vector2(0, 0),
                             new Vector2(4032, 0),
                             new Vector2(4032, 3024),
                             new Vector2(0, 3024),
                         };
        var destination = new[]
                              {
                                  new Vector2(1800, 200),
                                  new Vector2(2000, 220),
                                  new Vector2(1500, 2800),
                                  new Vector2(100, 1900),
                              };

        Assert.True(Homography.TryComputeQuadToQuad(source, destination, out var h));
        foreach (var p in source)
        {
            var w = h.M31 * p.X + h.M32 * p.Y + h.M33;
            Assert.True(w > 0);
        }
    }

    [Fact]
    public void LargePixelCoordinates_StayAccurate()
    {
        // Hartley normalization keeps the solve conditioned for camera-resolution inputs
        var source = new[]
                         {
                             new Vector2(430, 300),
                             new Vector2(1450, 180),
                             new Vector2(1450, 900),
                             new Vector2(430, 830),
                         };
        var destination = new[]
                              {
                                  new Vector2(0, 0),
                                  new Vector2(5.0f, 0),
                                  new Vector2(5.0f, 3.0f),
                                  new Vector2(0, 3.0f),
                              };

        Assert.True(Homography.TryComputeQuadToQuad(source, destination, out var h));
        for (var i = 0; i < 4; i++)
            AssertTransformsClose(h, source[i], destination[i]);
    }

    [Fact]
    public void ToMatrix4x4_MatchesTransformPoint()
    {
        var source = UnitQuad();
        var destination = new[]
                              {
                                  new Vector2(210, 95),
                                  new Vector2(1660, 120),
                                  new Vector2(1655, 940),
                                  new Vector2(215, 905),
                              };

        Assert.True(Homography.TryComputeQuadToQuad(source, destination, out var h));

        var m = h.ToMatrix4x4();
        var p = new Vector2(0.25f, 0.66f);
        var v = Vector4.Transform(new Vector4(p.X, p.Y, 0.5f, 1), m);
        var projected = new Vector2(v.X / v.W, v.Y / v.W);

        Assert.True((projected - h.TransformPoint(p)).Length() < 1e-3f);
        Assert.Equal(0.5f, v.Z, 4);
    }

    private static Vector2[] UnitQuad()
    {
        return
        [
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1),
        ];
    }

    private static void AssertTransformsClose(in Homography h, Vector2 input, Vector2 expected)
    {
        var actual = h.TransformPoint(input);
        Assert.True((actual - expected).Length() < Math.Max(1e-3f, expected.Length() * 1e-5f),
                    $"expected {expected}, got {actual}");
    }
}
