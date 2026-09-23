using System.IO;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Output;
using Xunit;

namespace Core.Tests.Output;

public class PatchFitTests
{
    [Fact]
    public void Fit_MatchingAspect_FillsTheCanvas()
    {
        var patch = FittedPatch(new Vector2(16, 9));

        Assert.True(patch.TryFitQuad(new Vector2(1920, 1080)));

        AssertQuad(patch, new Vector2(0, 0), new Vector2(1, 1));
    }

    [Fact]
    public void Fit_WideAspectOnPortraitCanvas_Letterboxes()
    {
        var patch = FittedPatch(new Vector2(16, 9));

        Assert.True(patch.TryFitQuad(new Vector2(1080, 1920)));

        // 1080 wide → 607.5 tall, centred in 1920.
        var halfHeight = 607.5f / 2 / 1920;
        AssertQuad(patch, new Vector2(0, 0.5f - halfHeight), new Vector2(1, 0.5f + halfHeight));
    }

    [Fact]
    public void Fit_ScopeAspectAtHalfScale_IsCentredAndHalfSize()
    {
        var patch = FittedPatch(new Vector2(2.39f, 1));
        patch.Scale = 0.5f;

        Assert.True(patch.TryFitQuad(new Vector2(1920, 1080)));

        // Full fit: 1920 × 803.35; half of it: 960 × 401.67.
        var halfHeight = 1920f / 2.39f * 0.5f / 2 / 1080;
        AssertQuad(patch, new Vector2(0.25f, 0.5f - halfHeight), new Vector2(0.75f, 0.5f + halfHeight));
    }

    [Fact]
    public void Fit_QuarterTurn_FitsThePictureSideways()
    {
        var patch = FittedPatch(new Vector2(16, 9));
        patch.QuarterTurns = 1;

        Assert.True(patch.TryFitQuad(new Vector2(1920, 1080)));

        // Sideways the picture is 9 : 16 on the canvas: 1080 tall → 607.5 wide.
        var halfWidth = 607.5f / 2 / 1920;
        AssertQuad(patch, new Vector2(0.5f - halfWidth, 0), new Vector2(0.5f + halfWidth, 1));
    }

    [Fact]
    public void Stretch_LeavesTheQuadAlone()
    {
        var quad = new[] { new Vector2(0.1f, 0.2f), new Vector2(0.6f, 0.2f), new Vector2(0.6f, 0.7f), new Vector2(0.1f, 0.7f) };
        var patch = new OutputDefinition.Patch { Quad = (Vector2[])quad.Clone() };

        Assert.False(patch.TryFitQuad(new Vector2(1920, 1080)));

        Assert.Equal(quad, patch.Quad);
    }

    [Fact]
    public void Fit_DegenerateAspect_IsRefused()
    {
        var patch = FittedPatch(new Vector2(0, 9));

        Assert.False(patch.TryFitQuad(new Vector2(1920, 1080)));
    }

    [Fact]
    public void FittedPatch_RoundTripsModeAspectAndScale()
    {
        var patch = FittedPatch(new Vector2(2.39f, 1));
        patch.Scale = 0.75f;

        var restored = OutputDefinition.Patch.ReadFromJson(ToJson(patch));

        Assert.Equal(PatchScaleModes.Fit, restored.ScaleMode);
        Assert.Equal(new Vector2(2.39f, 1), restored.AspectRatio);
        Assert.Equal(0.75f, restored.Scale);
    }

    [Fact]
    public void StretchedPatch_WritesNoFitFields_AndReadsDefaults()
    {
        var json = ToJson(new OutputDefinition.Patch());

        Assert.Null(json["ScaleMode"]);
        Assert.Null(json["AspectRatio"]);

        var restored = OutputDefinition.Patch.ReadFromJson(json);
        Assert.Equal(PatchScaleModes.Stretch, restored.ScaleMode);
        Assert.Equal(new Vector2(16, 9), restored.AspectRatio);
        Assert.Equal(1f, restored.Scale);
    }

    private static OutputDefinition.Patch FittedPatch(Vector2 aspect)
    {
        return new OutputDefinition.Patch { ScaleMode = PatchScaleModes.Fit, AspectRatio = aspect };
    }

    private static void AssertQuad(OutputDefinition.Patch patch, Vector2 min, Vector2 max)
    {
        const int precision = 4;
        Assert.Equal(4, patch.Quad.Length);
        AssertPoint(min, patch.Quad[0], precision);
        AssertPoint(new Vector2(max.X, min.Y), patch.Quad[1], precision);
        AssertPoint(max, patch.Quad[2], precision);
        AssertPoint(new Vector2(min.X, max.Y), patch.Quad[3], precision);
    }

    private static void AssertPoint(Vector2 expected, Vector2 actual, int precision)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
    }

    private static JToken ToJson(OutputDefinition.Patch patch)
    {
        using var stringWriter = new StringWriter();
        using (var writer = new JsonTextWriter(stringWriter))
        {
            patch.WriteToJson(writer);
        }

        return JToken.Parse(stringWriter.ToString());
    }
}
