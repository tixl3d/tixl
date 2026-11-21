using System.Text;
using Newtonsoft.Json;
using T3.Core.DataTypes;
using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// The µs-based scanner motion settings (drawing speed, motion response, corner settle,
/// transitions): the resampler, the optimizer's dwell passes, the passthrough guard and the
/// JSON round-trip.
/// </summary>
public class LaserMotionSettingsTests
{
    private const int Pps = 30000;

    private static LaserPoint Pt(int x, int y) => new(x, y, 0, 65535, 0, 65535);

    private static LaserPoint[] Apply(LaserOptimizerTransforms.Config config, params LaserPoint[] points)
    {
        var scratch = new LaserOptimizerTransforms.Scratch();
        return LaserOptimizerTransforms.Apply(points, points.Length, config, scratch);
    }

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

    // --- Drawing speed -----------------------------------------------------------------

    [Fact]
    public void DrawingSpeed_UpsamplesSegmentsToTheBudget()
    {
        // 20000 units at 8 µs/1000u and 30 kpps: ceil(20000·8·30000/1e9) = 5 intervals = 6 samples.
        var input = new[] { Pt(0, 0), Pt(20000, 0) };
        var output = LaserMotionTransforms.Apply(input, input.Length,
                                                 new LaserMotionTransforms.Config { DrawingSpeedUs = 8f, PointsPerSecond = Pps },
                                                 [], out var outputCount);
        Assert.Equal(6, outputCount);
        for (var i = 0; i < outputCount; i++)
        {
            Assert.Equal(0, output[i].Y);
            Assert.True(output[i].I > 0);
        }
    }

    [Fact]
    public void DrawingSpeed_ZeroBudgetOrUnknownRate_IsPassthrough()
    {
        var input = new[] { Pt(0, 0), Pt(20000, 0), Pt(20000, 15000) };
        var config = new LaserMotionTransforms.Config { DrawingSpeedUs = 0f, PointsPerSecond = Pps };
        var output = LaserMotionTransforms.Apply(input, input.Length, config, [], out var outputCount);
        Assert.Equal(input.Length, outputCount);
        for (var i = 0; i < outputCount; i++)
            Assert.Equal(input[i], output[i]);
    }

    [Fact]
    public void DrawingSpeed_RespectsThePerIntervalCap()
    {
        // A full-field interval at an aggressive budget wants far more samples than the cap
        // allows - the pass must degrade to MaxUpsample, not explode.
        var input = new[] { Pt(-32767, -32767), Pt(32767, 32767) };
        var output = LaserMotionTransforms.Apply(input, input.Length,
                                                 new LaserMotionTransforms.Config { DrawingSpeedUs = 100f, PointsPerSecond = 60_000 },
                                                 [], out var outputCount);
        Assert.True(outputCount <= LaserMotionTransforms.MaxUpsample + 1);
        Assert.True(outputCount >= 2);
    }

    // --- Motion response ----------------------------------------------------------------

    [Fact]
    public void MotionResponse_DwellsAtShapeEntries()
    {
        // 500 µs at 30 kpps = 15 dwell points at each shape's first sample.
        var input = new[]
                        {
                            Pt(-15000, -15000),
                            LaserPoint.CreateBlanked(0, 0),
                            Pt(15000, 15000),
                        };
        var output = Apply(new LaserOptimizerTransforms.Config
                               {
                                   EnableOptimization = false,
                                   BlankingDelayPoints = 5,
                                   MotionResponseUs = 500f,
                                   PointsPerSecond = Pps,
                               }, input);
        // First shape: entry sample + 15 dwells - all identical, all lit.
        for (var i = 0; i < 16; i++)
            Assert.Equal(input[0], output[i]);

        // Second shape: the frame ends on its entry sample + 15 dwells.
        var last = output.Length - 16;
        for (var i = last; i < output.Length; i++)
            Assert.Equal(input[2], output[i]);
    }

    [Fact]
    public void MotionResponse_Zero_IsOff()
    {
        var input = new[]
                        {
                            Pt(-15000, -15000),
                            LaserPoint.CreateBlanked(0, 0),
                            Pt(15000, 15000),
                        };
        var baseline = Apply(new LaserOptimizerTransforms.Config { EnableOptimization = false, BlankingDelayPoints = 5 }, input);
        var withMotion = Apply(new LaserOptimizerTransforms.Config
                                   {
                                       EnableOptimization = false,
                                       BlankingDelayPoints = 5,
                                       MotionResponseUs = 0f,
                                       PointsPerSecond = Pps,
                                   }, input);
        Assert.Equal(baseline.Length, withMotion.Length);
        for (var i = 0; i < baseline.Length; i++)
            Assert.Equal(baseline[i], withMotion[i]);
    }

    // --- Corner settle ------------------------------------------------------------------

    [Fact]
    public void CornerSettle_AddsDwellAtSharpCorners()
    {
        // A needle-thin spike: the interior angle at (0,0) is ~0°, far below the 30° threshold.
        var input = new[] { Pt(-10000, 0), Pt(0, 0), Pt(-10000, 1) };
        var output = Apply(new LaserOptimizerTransforms.Config
                               {
                                   EnableOptimization = false,
                                   CornerAngleThreshold = 30f,
                                   CornerAnchorCount = 1,
                                   CornerSettleUs = 500f,
                                   PointsPerSecond = Pps,
                               }, input);
        var cornerCount = 0;
        for (var i = 0; i < output.Length; i++)
        {
            if (output[i].X == 0 && output[i].Y == 0)
                cornerCount++;
        }

        // 1 anchor (the base sample) + 15 settle repeats.
        Assert.Equal(16, cornerCount);
    }

    // --- Transitions --------------------------------------------------------------------

    [Fact]
    public void Transition_ExtendsTheBlankRunBetweenShapes()
    {
        var input = new[]
                        {
                            Pt(-20000, -20000),
                            LaserPoint.CreateBlanked(0, 0),
                            Pt(20000, 20000),
                        };
        var baseline = Apply(new LaserOptimizerTransforms.Config
                                 {
                                     EnableOptimization = false,
                                     BlankingDelayPoints = 5,
                                     MaxJumpDistance = 5000f,
                                 }, input);
        var extended = Apply(new LaserOptimizerTransforms.Config
                                 {
                                     EnableOptimization = false,
                                     BlankingDelayPoints = 5,
                                     MaxJumpDistance = 5000f,
                                     TransitionUs = 1000f,
                                     PointsPerSecond = Pps,
                                 }, input);

        Assert.Equal(CountBlanks(baseline), CountBlanks(extended) - 30); // 1000 µs @ 30 kpps = 30
        Assert.True(CountBlanks(extended) > CountBlanks(baseline));
    }

    private static int CountBlanks(LaserPoint[] frame)
    {
        var n = 0;
        for (var i = 0; i < frame.Length; i++)
        {
            if (frame[i].I <= 0)
                n++;
        }

        return n;
    }

    // --- Pipeline composition -----------------------------------------------------------

    [Fact]
    public void Pipeline_AllMotionZero_MatchesTheManualComposition()
    {
        var input = new[]
                        {
                            Pt(-15000, -15000),
                            Pt(15000, -15000),
                            LaserPoint.CreateBlanked(0, 0),
                            Pt(15000, 15000),
                        };
        var settings = new LaserProcessingSettings { Safety = { Calibrated = true } };
        var session = new LaserPipeline.Session();
        var result = session.Apply(settings, input, input.Length, out var frameCount, Pps);

        // Manual composition of what the pipeline runs with the budgets at zero:
        // resampler skipped, optimizer, then the safety stage.
        var optimized = LaserOptimizerTransforms.Apply(input, input.Length,
                                                       settings.Optimizer.ToConfig(null, Pps),
                                                       new LaserOptimizerTransforms.Scratch());
        var work = new List<LaserPoint>(optimized);
        var spare = new List<LaserPoint>();
        var stats = new LaserSafetyTransforms.Stats();
        var safetyConfig = settings.Safety.ToConfig();
        var safetyResult = LaserSafetyTransforms.Apply(work, spare, in safetyConfig, ref stats);

        Assert.Equal(safetyResult.Count, frameCount);
        for (var i = 0; i < frameCount; i++)
            Assert.Equal(safetyResult[i], result[i]);
    }

    [Fact]
    public void Pipeline_DrawingSpeedStretchesTheFrame()
    {
        var input = new[] { Pt(0, 0), Pt(20000, 0) };
        var settings = new LaserProcessingSettings { Safety = { Calibrated = true } };
        settings.Motion.DrawingSpeedUs = 8f;
        var session = new LaserPipeline.Session();
        var result = session.Apply(settings, input, input.Length, out var frameCount, Pps);
        Assert.Equal(6, frameCount);
        Assert.True(frameCount > input.Length);
        for (var i = 0; i < frameCount; i++)
            Assert.True(result[i].I > 0);
    }

    // --- Settings JSON ------------------------------------------------------------------

    [Fact]
    public void Motion_RoundTripsThroughJson()
    {
        var settings = new LaserProcessingSettings();
        settings.Motion.DrawingSpeedUs = 7.5f;
        settings.Motion.MotionResponseUs = 200f;
        settings.Motion.CornerSettleUs = 120f;
        settings.Motion.TransitionUs = 400f;

        var restored = LaserProcessingSettings.ReadFromJson(
            JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(WriteJson(settings)))!;

        Assert.Equal(7.5f, restored.Motion.DrawingSpeedUs);
        Assert.Equal(200f, restored.Motion.MotionResponseUs);
        Assert.Equal(120f, restored.Motion.CornerSettleUs);
        Assert.Equal(400f, restored.Motion.TransitionUs);
    }

    [Fact]
    public void Motion_MissingInOldFile_FallsBackToZero()
    {
        var settings = new LaserProcessingSettings();
        var token = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(WriteJson(settings))!;
        token.Remove("Motion");

        var restored = LaserProcessingSettings.ReadFromJson(token)!;

        Assert.Equal(0f, restored.Motion.DrawingSpeedUs);
        Assert.Equal(0f, restored.Motion.MotionResponseUs);
        Assert.Equal(0f, restored.Motion.CornerSettleUs);
        Assert.Equal(0f, restored.Motion.TransitionUs);
    }
}
