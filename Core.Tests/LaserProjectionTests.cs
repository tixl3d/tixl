using System.Numerics;
using Lib.Utils;
using T3.Core.Utils.Geometry;
using Xunit;

namespace Core.Tests;

/// <summary>
/// The camera-to-scanner projection shared by LaserCamera and MeshToLaserPoints: it has to follow
/// the lens exactly, cut at the frame instead of pinning to it, and never turn NaN into a coordinate.
/// </summary>
public class LaserProjectionTests
{
    /// <summary>Camera 2.4141 in front of the origin with a 45 degree lens: at the origin's depth
    /// the frame spans exactly ±1 world unit, so world X maps linearly to NDC X.</summary>
    private static Matrix4x4 Lens(float near = 0.01f, float far = 1000f)
    {
        var view = GraphicsMath.LookAtRH(new Vector3(0, 0, 2.4141f), Vector3.Zero, Vector3.UnitY);
        var projection = GraphicsMath.PerspectiveFovRH(45f * MathF.PI / 180f, 1f, near, far);
        return view * projection;
    }

    [Fact]
    public void TryProject_OriginLandsInTheFieldCenter()
    {
        Assert.True(LaserProjection.TryProject(Vector3.Zero, Lens(), out var x, out var y));
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Theory]
    [InlineData(0.01f, 1000f)]
    [InlineData(0.5f, 10f)]
    [InlineData(1.0f, 5f)]
    public void TryProject_FollowsTheLensWhateverTheClipPlanes(float near, float far)
    {
        Assert.True(LaserProjection.TryProject(new Vector3(0.5f, 0.25f, 0), Lens(near, far), out var x, out var y));

        // Dividing by the clip-space Z instead of W skews this by the near/far ratio.
        Assert.InRange(x, 16383 - 2, 16384 + 2);
        Assert.InRange(y, 8191 - 2, 8192 + 2);
    }

    [Fact]
    public void TryProject_DropsPointsBehindTheCameraAndOutsideTheFrame()
    {
        var lens = Lens();
        Assert.False(LaserProjection.TryProject(new Vector3(0, 0, 5), lens, out _, out _));
        Assert.False(LaserProjection.TryProject(new Vector3(1.5f, 0, 0), lens, out _, out _));
        Assert.False(LaserProjection.TryProject(new Vector3(0, -1.5f, 0), lens, out _, out _));
    }

    [Fact]
    public void TryProject_DropsPointsBeyondTheFarPlane()
    {
        Assert.False(LaserProjection.TryProject(new Vector3(0, 0, -20), Lens(0.01f, 10f), out _, out _));
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, float.NegativeInfinity)]
    public void NonFinitePositionsAreNotDrawable(float px, float py, float pz)
    {
        var lens = Lens();
        Assert.False(LaserProjection.TryProject(new Vector3(px, py, pz), lens, out _, out _));
        Assert.False(LaserProjection.TryProjectSegment(new Vector3(px, py, pz), Vector3.Zero, lens, out _, out _, out _, out _));
        Assert.False(LaserProjection.TryProjectSegment(Vector3.Zero, new Vector3(px, py, pz), lens, out _, out _, out _, out _));
    }

    [Fact]
    public void TryProjectSegment_CutsAtTheFrameInsteadOfBendingAlongIt()
    {
        Assert.True(LaserProjection.TryProjectSegment(Vector3.Zero, new Vector3(5, 0, 0), Lens(), out var ax, out var ay, out var bx, out var by));

        Assert.Equal(0f, ax, 1);
        Assert.Equal(0f, ay, 1);
        Assert.Equal(32767f, bx, 1);
        Assert.Equal(0f, by, 1);
    }

    [Fact]
    public void TryProjectSegment_DropsASegmentEntirelyOutside()
    {
        Assert.False(LaserProjection.TryProjectSegment(new Vector3(2, 2, 0), new Vector3(3, 2, 0), Lens(), out _, out _, out _, out _));
    }

    [Fact]
    public void TryProjectSegment_KeepsTheVisiblePartOfASegmentPassingThroughTheCameraPlane()
    {
        // One end in front of the camera, the other behind it.
        Assert.True(LaserProjection.TryProjectSegment(new Vector3(0, 0, 0), new Vector3(0, 0, 10), Lens(), out var ax, out var ay, out var bx, out var by));

        Assert.True(float.IsFinite(ax + ay + bx + by));
        Assert.InRange(MathF.Abs(bx) + MathF.Abs(by), 0f, 32767f * 2f);
    }

    [Fact]
    public void ToColor_MapsNanToDarkAndClampsTheRest()
    {
        LaserProjection.ToColor(float.NaN, 2f, 0.5f, 1f, out var r, out var g, out var b);

        Assert.Equal(0, r);
        Assert.Equal(65535, g);
        Assert.Equal(32767, b);
    }
}
