using System.Numerics;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

public class SurfaceMappingTests
{
    [Fact]
    public void Fill_SurvivesAResize()
    {
        var surface = MappedSurface(MappingModes.Fill);

        SurfaceGeometry.ResizeAnchored(surface, new Vector2(4, 1));

        Assert.Equal(new Vector2(4, 1), surface.SizeInMeters);
        AssertQuad(surface, new Vector2(0, 0), new Vector2(1, 1));
    }

    [Fact]
    public void CornerPin_FollowsAReshapeOnTheWall()
    {
        var surface = MappedSurface(MappingModes.CornerPin);
        surface.OutputMappings[0].Quad = [new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.2f), new Vector2(0.8f, 0.8f), new Vector2(0.2f, 0.8f)];

        // Twice as wide, same height, anchored at the bottom-left corner of the old rectangle.
        SurfaceGeometry.ApplyBounds(surface, Vector2.Zero, new Vector2(4, 1));

        AssertQuad(surface, new Vector2(0.2f, 0.2f), new Vector2(1.4f, 0.8f));
    }

    [Fact]
    public void CornerPin_StaysPutWhenTheSizeIsOnlyDeclared()
    {
        var surface = MappedSurface(MappingModes.CornerPin);
        surface.OutputMappings[0].Quad = [new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.2f), new Vector2(0.8f, 0.8f), new Vector2(0.2f, 0.8f)];

        SurfaceGeometry.ApplyBounds(surface, Vector2.Zero, new Vector2(4, 1), movesMappings: false);

        Assert.Equal(new Vector2(4, 1), surface.SizeInMeters);
        AssertQuad(surface, new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f));
    }

    private static Surface MappedSurface(MappingModes mode)
    {
        var surface = new Surface { SizeInMeters = new Vector2(2, 1), Anchor = new Vector2(-1, -1) };
        var mapping = Surface.OutputMapping.CreateFilling(Guid.NewGuid());
        mapping.Mode = mode;
        surface.OutputMappings.Add(mapping);
        return surface;
    }

    private static void AssertQuad(Surface surface, Vector2 min, Vector2 max)
    {
        var quad = surface.OutputMappings[0].Quad;
        AssertPoint(min, quad[0]);
        AssertPoint(new Vector2(max.X, min.Y), quad[1]);
        AssertPoint(max, quad[2]);
        AssertPoint(new Vector2(min.X, max.Y), quad[3]);
    }

    private static void AssertPoint(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
    }
}
