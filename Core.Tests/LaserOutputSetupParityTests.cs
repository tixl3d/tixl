using System.Text;
using Newtonsoft.Json;
using T3.Core.DataTypes;
using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Output-setup parity with the MadLaser guide: desired-FPS pacing knobs, the scanning readout
/// math, and the safety-shape rendering options.
/// </summary>
public class LaserOutputSetupParityTests
{
    private static LaserPoint Pt(int x, int y, int r, int g, int b) => new(x, y, r, g, b);

    // --- Desired FPS / effective frame rate -------------------------------------------

    [Fact]
    public void DesiredFps_RoundTripsThroughJson()
    {
        var settings = new LaserProcessingSettings();
        settings.Safety.DesiredFps = 24;

        var restored = WriteThenRead(settings);

        Assert.Equal(24, restored!.Safety.DesiredFps);
    }

    [Fact]
    public void DesiredFps_MissingInOldFile_FallsBackToSixty()
    {
        var settings = new LaserProcessingSettings();
        var token = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(WriteJson(settings))!;
        token.Remove("DesiredFps");

        var restored = LaserProcessingSettings.ReadFromJson(token);

        Assert.Equal(60, restored!.Safety.DesiredFps);
    }

    [Fact]
    public void EffectiveFrameRate_IsPpsOverPoints()
    {
        Assert.Equal(60, LaserProcessingSettings.SafetySettings.EffectiveFrameRate(30000, 500));
        Assert.Equal(50, LaserProcessingSettings.SafetySettings.EffectiveFrameRate(30000, 600));
        Assert.Equal(0, LaserProcessingSettings.SafetySettings.EffectiveFrameRate(30000, 0));
        Assert.Equal(0, LaserProcessingSettings.SafetySettings.EffectiveFrameRate(0, 500));
    }

    // --- Safety shape rendering --------------------------------------------------------

    [Fact]
    public void SafetyShapes_Default_UsesSemanticColors()
    {
        var safety = new LaserProcessingSettings.SafetySettings { RenderShapesLevel = 0.5f };
        safety.Masks.Add(new LaserProcessingSettings.SafetySettings.MaskQuad
                             {
                                 X1 = -0.5f, Y1 = -0.5f, X2 = 0.5f, Y2 = -0.5f,
                                 X3 = 0.5f, Y3 = 0.5f, X4 = -0.5f, Y4 = 0.5f,
                             });

        var buffer = new LaserPoint[4096];
        var count = LaserPipeline.BuildSafetyShapes(safety, buffer);

        Assert.True(count > 0);
        var lit = buffer.Take(count).Where(p => p.I > 0).ToArray();
        // Zone outline: green lit samples (R off). Mask outline: orange (R on, G half).
        Assert.Contains(lit, p => p.R == 0 && p.G > 0);
        Assert.Contains(lit, p => p.R > 0 && p.G > 0 && p.B == 0);
    }

    [Fact]
    public void SafetyShapes_CustomColor_ReplacesBoth()
    {
        var safety = new LaserProcessingSettings.SafetySettings
                         {
                             RenderShapesLevel = 1f,
                             RenderShapesUseCustomColor = true,
                             RenderShapesColorR = 1f,
                             RenderShapesColorG = 0f,
                             RenderShapesColorB = 0.25f,
                         };
        safety.Masks.Add(new LaserProcessingSettings.SafetySettings.MaskQuad
                             {
                                 X1 = -0.5f, Y1 = -0.5f, X2 = 0.5f, Y2 = -0.5f,
                                 X3 = 0.5f, Y3 = 0.5f, X4 = -0.5f, Y4 = 0.5f,
                             });

        var buffer = new LaserPoint[4096];
        var count = LaserPipeline.BuildSafetyShapes(safety, buffer);

        Assert.True(count > 0);
        var lit = buffer.AsSpan(0, count).ToArray().Where(p => p.I > 0).ToArray();
        Assert.True(lit.Length > 0);
        // Every lit shape sample carries exactly the custom color (red with a quarter blue).
        Assert.All(lit, p => Assert.Equal(65535, p.R));
        Assert.All(lit, p => Assert.Equal(0, p.G));
        Assert.All(lit, p => Assert.Equal(16383, p.B));
    }

    [Fact]
    public void SafetyShapes_CustomColor_RoundTripsThroughJson()
    {
        var settings = new LaserProcessingSettings();
        settings.Safety.RenderShapesUseCustomColor = true;
        settings.Safety.RenderShapesColorR = 0.2f;
        settings.Safety.RenderShapesColorG = 0.8f;
        settings.Safety.RenderShapesColorB = 0.5f;

        var restored = WriteThenRead(settings);

        Assert.True(restored!.Safety.RenderShapesUseCustomColor);
        Assert.Equal(0.2f, restored.Safety.RenderShapesColorR, 3);
        Assert.Equal(0.8f, restored.Safety.RenderShapesColorG, 3);
        Assert.Equal(0.5f, restored.Safety.RenderShapesColorB, 3);
    }

    // --- helpers -----------------------------------------------------------------------

    private static string WriteJson(LaserProcessingSettings settings)
    {
        var sb = new StringBuilder();
        using (var sw = new System.IO.StringWriter(sb))
        using (var writer = new JsonTextWriter(sw))
        {
            settings.WriteToJson(writer);
            writer.Flush();
        }

        return sb.ToString();
    }

    private static LaserProcessingSettings? WriteThenRead(LaserProcessingSettings settings)
    {
        return LaserProcessingSettings.ReadFromJson(JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(WriteJson(settings)));
    }
}
