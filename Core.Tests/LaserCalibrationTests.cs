#nullable enable
using T3.Core.DataTypes;
using Xunit;

namespace Core.Tests;

/** Tests for the calibration features: gamma/TTL color levels, the static-beam cap and the flip. */
public class LaserCalibrationTests
{
    private static LaserPoint Pt(int x, int y, int r, int g, int b) => new(x, y, r, g, b);

    // ---------- gamma ----------

    [Fact]
    public void Gamma_CompressesMidtones_AtDefault()
    {
        var points = new List<LaserPoint> { Pt(0, 0, 32767, 0, 0) };
        LaserSafetyTransforms.ApplyColorLevels(points, new LaserSafetyTransforms.Config { ColorGammaR = 2.2f });
        // out = (0.5)^(1/2.2) is about 0.73 of full scale
        Assert.InRange(points[0].R, 46000, 50000);
    }

    [Fact]
    public void Gamma_LinearAtOne_LeavesValuesUntouched()
    {
        var points = new List<LaserPoint> { Pt(0, 0, 32767, 0, 0) };
        LaserSafetyTransforms.ApplyColorLevels(points, new LaserSafetyTransforms.Config());
        Assert.Equal(32767, points[0].R);
    }

    [Fact]
    public void Gamma_FullScale_StaysFullScale()
    {
        var points = new List<LaserPoint> { Pt(0, 0, 65535, 65535, 65535) };
        LaserSafetyTransforms.ApplyColorLevels(points, new LaserSafetyTransforms.Config { ColorGammaG = 3.0f });
        Assert.Equal(65535, points[0].G);
    }

    [Fact]
    public void Gamma_Zero_StaysZero()
    {
        var points = new List<LaserPoint> { Pt(0, 0, 0, 0, 0) };
        LaserSafetyTransforms.ApplyColorLevels(points, new LaserSafetyTransforms.Config { ColorGammaB = 2.2f });
        Assert.Equal(0, points[0].B);
    }

    // ---------- TTL ----------

    [Fact]
    public void Ttl_AtOrAboveHalf_IsFull()
    {
        var points = new List<LaserPoint> { Pt(0, 0, 32768, 32766, 65535) };
        LaserSafetyTransforms.ApplyColorLevels(points, new LaserSafetyTransforms.Config { TtlMode = true });
        Assert.Equal(65535, points[0].R);
        Assert.Equal(0, points[0].G);
        Assert.Equal(65535, points[0].B);
    }

    [Fact]
    public void Ttl_BelowHalf_IsOff()
    {
        var points = new List<LaserPoint> { Pt(0, 0, 10000, 20000, 0) };
        LaserSafetyTransforms.ApplyColorLevels(points, new LaserSafetyTransforms.Config { TtlMode = true });
        Assert.Equal(0, points[0].R);
        Assert.Equal(0, points[0].G);
        Assert.Equal(0, points[0].B);
    }

    // ---------- static intensity ----------

    [Fact]
    public void StaticBeam_BeyondDwell_BlanksByDefault()
    {
        var points = new List<LaserPoint> { Pt(5000, 100, 65535, 65535, 65535) };
        for (var i = 0; i < 10; i++)
            points.Add(Pt(100, 100, 65535, 65535, 65535));
        var cfg = new LaserSafetyTransforms.Config { MaxStaticPoints = 4 };
        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.LimitStaticDwell(points, cfg, ref stats);
        Assert.Equal(65535, points[4].R);  // within the dwell cap (4 static points after the move): lit
        Assert.Equal(0, points[10].R);     // beyond the dwell cap: blanked
    }

    [Fact]
    public void StaticBeam_WithStaticIntensity_DimsInstead()
    {
        var points = new List<LaserPoint> { Pt(5000, 100, 65535, 65535, 65535) };
        for (var i = 0; i < 10; i++)
            points.Add(Pt(100, 100, 65535, 65535, 65535));
        var cfg = new LaserSafetyTransforms.Config { MaxStaticPoints = 4, MaxStaticIntensity = 0.05f };
        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.LimitStaticDwell(points, cfg, ref stats);
        Assert.Equal(65535, points[4].R);                // within the dwell cap: full
        Assert.Equal((int)(65535 * 0.05f), points[10].R); // beyond the dwell cap: dimmed to 5%
    }

    // ---------- flip ----------

    [Fact]
    public void FlipX_MirrorsWithinZone()
    {
        var points = new List<LaserPoint> { Pt(20000, 5000, 65535, 0, 0) };
        var cfg = new LaserSafetyTransforms.Config
                      {
                          FlipX = true,
                          ZoneCenterX = 0f,
                          ZoneHalfWidth = 32767f,
                          ZoneHalfHeight = 32767f,
                      };
        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.ClampToScanField(points, cfg, ref stats);
        Assert.Equal(-20000, points[0].X);
        Assert.Equal(5000, points[0].Y);
    }

    [Fact]
    public void FlipY_MirrorsWithinZone()
    {
        var points = new List<LaserPoint> { Pt(5000, -10000, 0, 65535, 0) };
        var cfg = new LaserSafetyTransforms.Config { FlipY = true, ZoneHalfWidth = 32767f, ZoneHalfHeight = 32767f };
        var stats = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.ClampToScanField(points, cfg, ref stats);
        Assert.Equal(5000, points[0].X);
        Assert.Equal(10000, points[0].Y);
    }
}
