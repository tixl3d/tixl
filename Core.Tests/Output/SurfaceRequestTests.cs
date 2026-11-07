using System;
using System.Numerics;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using Xunit;

namespace Core.Tests.Output;

public class SurfaceRequestTests
{
    [Fact]
    public void WholeSource_RendersAtTheSurfaceSizeTimesItsDensity()
    {
        var (setup, sendId, source) = SetupWithSend();
        AddSurface(setup, source, new Vector2(2, 0.5f), 400, new Vector4(0, 0, 1, 1));

        Assert.True(OutputContentResolver.TryGetSurfaceRequest(setup, sendId, out var resolution, out var hasConflict));
        Assert.Equal(800, resolution.Width);
        Assert.Equal(200, resolution.Height);
        Assert.False(hasConflict);
    }

    [Fact]
    public void MappedSurface_RendersAtTheDensityItIsProjectedWith()
    {
        var (setup, sendId, source) = SetupWithSend();
        var surface = AddSurface(setup, source, new Vector2(1, 0.5f), 400, new Vector4(0, 0, 1, 1));
        var output = setup.Outputs[0];
        output.CanvasResolution = new T3.Core.DataTypes.Vector.Int2(1920, 1080);

        // Fills the canvas: 1920 px across 1 m, 1080 px across 0.5 m. The denser direction wins, capped so the longest
        // side doesn't exceed the canvas' 1920 px, and stepped by up to about 4.4%.
        surface.OutputMappings.Add(Surface.OutputMapping.CreateFilling(output.Id));

        Assert.True(OutputContentResolver.TryGetSurfaceRequest(setup, sendId, out var resolution, out _));
        Assert.InRange(resolution.Width, 1840, 1920);
        Assert.Equal(2f, resolution.Width / (float)resolution.Height, 2);
    }

    [Fact]
    public void ExtremePin_IsCappedAtTheCanvasSize()
    {
        var (setup, sendId, source) = SetupWithSend();
        var surface = AddSurface(setup, source, new Vector2(0.4f, 0.6f), 400, new Vector4(0, 0, 1, 1));
        var output = setup.Outputs[0];
        output.CanvasResolution = new T3.Core.DataTypes.Vector.Int2(1920, 1080);

        // A pin mid-drag with a corner thrown far off the canvas.
        surface.OutputMappings.Add(new Surface.OutputMapping
                                       {
                                           OutputId = output.Id,
                                           Quad = [new Vector2(0.4f, 0.1f), new Vector2(40f, -30f), new Vector2(0.8f, 0.9f), new Vector2(0.4f, 0.9f)],
                                       });

        Assert.True(OutputContentResolver.TryGetSurfaceRequest(setup, sendId, out var resolution, out _));
        Assert.True(resolution.Height <= 1920, $"{resolution.Width}×{resolution.Height}");
    }

    [Fact]
    public void SmallPinChanges_KeepTheSameSize()
    {
        var (setup, sendId, source) = SetupWithSend();
        var surface = AddSurface(setup, source, new Vector2(0.4f, 0.6f), 400, new Vector4(0, 0, 1, 1));
        var output = setup.Outputs[0];
        output.CanvasResolution = new T3.Core.DataTypes.Vector.Int2(1920, 1080);
        var mapping = new Surface.OutputMapping
                          {
                              OutputId = output.Id,
                              Quad = [new Vector2(0.40f, 0.10f), new Vector2(0.70f, 0.12f), new Vector2(0.71f, 0.90f), new Vector2(0.40f, 0.88f)],
                          };
        surface.OutputMappings.Add(mapping);
        Assert.True(OutputContentResolver.TryGetSurfaceRequest(setup, sendId, out var before, out _));

        // One pixel of drag on a corner.
        mapping.Quad[2] += new Vector2(1f / 1920, 1f / 1080);
        Assert.True(OutputContentResolver.TryGetSurfaceRequest(setup, sendId, out var after, out _));
        Assert.Equal(before, after);
    }

    [Fact]
    public void Slice_ScalesUpSoTheCutLandsOneToOne()
    {
        var (setup, sendId, source) = SetupWithSend();
        AddSurface(setup, source, new Vector2(1, 1), 100, new Vector4(0, 0, 0.5f, 0.25f));

        Assert.True(OutputContentResolver.TryGetSurfaceRequest(setup, sendId, out var resolution, out _));
        Assert.Equal(200, resolution.Width);
        Assert.Equal(400, resolution.Height);
    }

    [Fact]
    public void SurfacesOfDifferentAspect_ReportAConflict_FirstOneDecides()
    {
        var (setup, sendId, source) = SetupWithSend();
        AddSurface(setup, source, new Vector2(2, 1), 100, new Vector4(0, 0, 1, 1));
        AddSurface(setup, source, new Vector2(1, 1), 100, new Vector4(0, 0, 1, 1));

        Assert.True(OutputContentResolver.TryGetSurfaceRequest(setup, sendId, out var resolution, out var hasConflict));
        Assert.Equal(200, resolution.Width);
        Assert.Equal(100, resolution.Height);
        Assert.True(hasConflict);
    }

    [Fact]
    public void Aspect_FollowsTheSurfaceMetresAndTheSlice_NotTheProjection()
    {
        var (setup, sendId, source) = SetupWithSend();
        var surface = AddSurface(setup, source, new Vector2(1, 2), 400, new Vector4(0, 0, 0.5f, 1));
        surface.OutputMappings.Add(Surface.OutputMapping.CreateFilling(setup.Outputs[0].Id));

        // 1 m by 2 m from half of the source: the full source is 2 by 2.
        Assert.True(OutputContentResolver.TryGetSurfaceAspect(setup, sendId, out var aspect, out var hasConflict));
        Assert.Equal(1f, aspect, 3);
        Assert.False(hasConflict);
    }

    [Fact]
    public void SendNotShownOnASurface_HasNoRequest()
    {
        var (setup, sendId, _) = SetupWithSend();

        Assert.False(OutputContentResolver.TryGetSurfaceRequest(setup, sendId, out _, out _));
    }

    private static (Setup Setup, Guid SendId, ContentSource Source) SetupWithSend()
    {
        var setup = Setup.CreateDefault();
        var sendId = Guid.NewGuid();
        var source = new ContentSource { SymbolChildId = sendId, Name = "send" };
        setup.ContentSources.Add(source);
        return (setup, sendId, source);
    }

    private static Surface AddSurface(Setup setup, ContentSource source, Vector2 sizeInMeters, float pixelsPerMeter, Vector4 uvRect)
    {
        var slice = new Slice { SourceId = source.Id, UvRect = uvRect };
        setup.Slices.Add(slice);
        var surface = new Surface { SizeInMeters = sizeInMeters, PixelsPerMeter = pixelsPerMeter, SliceId = slice.Id };
        setup.Surfaces.Add(surface);
        return surface;
    }
}
