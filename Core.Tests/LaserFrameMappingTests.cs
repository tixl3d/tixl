using T3.Core.DataTypes;
using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Tests for <see cref="LaserPipeline.MapFrameIntoZone"/>: the camera frame is the scanner field,
/// so mapping it onto the aimed zone must be a fixed transform of that field. In particular it must
/// not depend on the content's bounding box - a rotating object would otherwise pulse in size.
/// </summary>
public class LaserFrameMappingTests
{
    private static LaserProcessingSettings.SafetySettings Zone(float width, float height, float centerX = 0f, float centerY = 0f)
    {
        return new LaserProcessingSettings.SafetySettings
                   {
                       SetupCenterX = centerX,
                       SetupCenterY = centerY,
                       SetupWidth = width,
                       SetupHeight = height,
                   };
    }

    private static LaserPoint Lit(int x, int y) => new(x, y, 65535, 65535, 65535);

    [Fact]
    public void Fit_OnFullFieldZone_IsAPassThrough()
    {
        var frame = new[] { Lit(12345, -6789), Lit(-32767, 32767), Lit(0, 0) };

        var mapped = LaserPipeline.MapFrameIntoZone(frame, frame.Length, Zone(1f, 1f), LaserFrameMappingMode.Fit);

        Assert.NotNull(mapped);
        for (var i = 0; i < frame.Length; i++)
        {
            Assert.Equal(frame[i].X, mapped![i].X);
            Assert.Equal(frame[i].Y, mapped[i].Y);
        }
    }

    [Fact]
    public void Mapping_DoesNotDependOnTheContentExtent()
    {
        // The same probe point in a frame whose content fills the field and in one whose content
        // is tiny must land on the same scanner coordinate - the size of what is lit this frame
        // cannot change the camera's framing.
        var bigFrame = new[] { Lit(-30000, -30000), Lit(30000, 30000), Lit(4000, 2000) };
        var smallFrame = new[] { Lit(-500, -500), Lit(500, 500), Lit(4000, 2000) };

        var settings = Zone(0.5f, 0.5f);
        var big = LaserPipeline.MapFrameIntoZone(bigFrame, bigFrame.Length, settings, LaserFrameMappingMode.Fit)!;
        var small = LaserPipeline.MapFrameIntoZone(smallFrame, smallFrame.Length, settings, LaserFrameMappingMode.Fit)!;

        Assert.Equal(big[2].X, small[2].X);
        Assert.Equal(big[2].Y, small[2].Y);
        Assert.Equal(2000, big[2].X);   // 0.5 zone scale => half field
        Assert.Equal(1000, big[2].Y);
    }

    [Fact]
    public void Fit_KeepsTheAspect_OnANonSquareZone()
    {
        // 1.0 wide, 0.5 tall: the shorter axis decides the uniform scale, so a diagonal keeps
        // its slope (a circle stays a circle, letterboxed horizontally).
        var frame = new[] { Lit(10000, 10000) };
        var mapped = LaserPipeline.MapFrameIntoZone(frame, frame.Length, Zone(1f, 0.5f), LaserFrameMappingMode.Fit)!;

        Assert.Equal(5000, mapped[0].X);
        Assert.Equal(5000, mapped[0].Y);
    }

    [Fact]
    public void Stretch_ScalesBothAxesIndependently()
    {
        var frame = new[] { Lit(10000, 10000) };
        var mapped = LaserPipeline.MapFrameIntoZone(frame, frame.Length, Zone(1f, 0.5f), LaserFrameMappingMode.Stretch)!;

        Assert.Equal(10000, mapped[0].X);
        Assert.Equal(5000, mapped[0].Y);

        var tall = LaserPipeline.MapFrameIntoZone(frame, frame.Length, Zone(0.5f, 1f), LaserFrameMappingMode.Stretch)!;
        Assert.Equal(5000, tall[0].X);
        Assert.Equal(10000, tall[0].Y);
    }

    [Fact]
    public void Crop_BlanksWhatFallsOutsideTheAimedZone()
    {
        // 1.0 wide, 0.5 tall: Crop fills width, so the vertical overflow is cut (blanked), while
        // points inside the zone keep their color.
        var frame = new[] { Lit(0, 1000), Lit(0, 20000) };
        var mapped = LaserPipeline.MapFrameIntoZone(frame, frame.Length, Zone(1f, 0.5f), LaserFrameMappingMode.Crop)!;

        Assert.True(mapped[0].I > 0, "a point inside the zone stays lit");
        Assert.Equal(0, mapped[1].I);
        Assert.True(mapped[1].R == 0 && mapped[1].G == 0 && mapped[1].B == 0, "cut points are fully blanked");
    }

    [Fact]
    public void Off_ReturnsTheInputUnchanged()
    {
        var frame = new[] { Lit(1234, 5678) };
        var mapped = LaserPipeline.MapFrameIntoZone(frame, frame.Length, Zone(0.2f, 0.2f), LaserFrameMappingMode.Off);

        Assert.Same(frame, mapped);
    }
}
