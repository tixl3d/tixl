using T3.Core.DataTypes;
using Xunit;

namespace Core.Tests;

public static class LaserProbe
{
    public static string Run()
    {
        var sb = new System.Text.StringBuilder();
        var p3 = new List<LaserPoint> { new(5000, 100, 65535, 65535, 65535) };
        for (var i = 0; i < 10; i++)
            p3.Add(new(100, 100, 65535, 65535, 65535));
        var cfg = new LaserSafetyTransforms.Config { MaxStaticPoints = 4 };
        var st3 = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.LimitStaticDwell(p3, cfg, ref st3);
        sb.Append($"static idx6={p3[6].R} idx10={p3[10].R} blanked={st3.StaticBlanked}  ");

        var p4 = new List<LaserPoint> { new(5000, 100, 65535, 65535, 65535) };
        for (var i = 0; i < 10; i++)
            p4.Add(new(100, 100, 65535, 65535, 65535));
        var cfg4 = new LaserSafetyTransforms.Config { MaxStaticPoints = 4, MaxStaticIntensity = 0.05f };
        var st4 = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.LimitStaticDwell(p4, cfg4, ref st4);
        sb.Append($"staticDim idx6={p4[6].R} idx10={p4[10].R}  ");

        var p5 = new List<LaserPoint> { new(5000, -10000, 0, 65535, 0) };
        var st5 = new LaserSafetyTransforms.Stats();
        LaserSafetyTransforms.ClampToScanField(p5, new LaserSafetyTransforms.Config { FlipY = true, ZoneHalfHeight = 32767f }, ref st5);
        sb.Append($"flipY: {p5[0].X},{p5[0].Y}");
        return sb.ToString();
    }
}

public class LaserProbeTests
{
    [Fact]
    public void Probe_Actuals()
    {
        Assert.Contains("static idx6=0 idx10=0", LaserProbe.Run());
    }
}
