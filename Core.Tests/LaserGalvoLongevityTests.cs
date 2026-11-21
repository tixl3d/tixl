using System;
using System.Collections.Generic;
using T3.Core.DataTypes;
using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Scanner-longevity properties of the laser data generation, pinned after an oscilloscope
/// report showed the galvos making maximal-acceleration jumps and visiting null between
/// shapes. The frame data must chain shape to shape: travel happens on smooth blanked ramps
/// in bounded steps, never as single-sample steps, and the loop seam (the DAC replays the
/// finished frame continuously) is the shortest link of the tour rather than a cross-field
/// sweep near null.
/// </summary>
public class LaserGalvoLongevityTests
{
    private static LaserPoint[] BuildDottedEllipse(int dots, int dotDwell, float radiusX, float radiusY)
    {
        var input = new List<LaserPoint>(dots * (dotDwell + 1));
        for (var i = 0; i < dots; i++)
        {
            var a = i / (float)dots * MathF.PI * 2f;
            var x = (int)(MathF.Cos(a) * radiusX);
            var y = (int)(MathF.Sin(a) * radiusY);
            input.Add(LaserPoint.CreateBlanked(x, y));
            for (var d = 0; d < dotDwell; d++)
                input.Add(new LaserPoint(x, y, 0, 65535, 0, 65535));
        }

        return input.ToArray();
    }

    private static float MaxConsecutiveStep(LaserPoint[] frame, int count)
    {
        var maxStep = 0f;
        for (var i = 1; i < count; i++)
        {
            var dx = frame[i].X - frame[i - 1].X;
            var dy = frame[i].Y - frame[i - 1].Y;
            maxStep = MathF.Max(maxStep, MathF.Sqrt(dx * (float)dx + dy * (float)dy));
        }

        return maxStep;
    }

    private const int NearNullRadius = 300;

    private static int CountNearNullSamples(LaserPoint[] frame, int frameCount)
    {
        var count = 0;
        for (var i = 0; i < frameCount; i++)
        {
            var p = frame[i];
            if (Math.Abs(p.X) < NearNullRadius && Math.Abs(p.Y) < NearNullRadius)
                count++;
        }

        return count;
    }

    private static LaserProcessingSettings CalibratedDefaults()
    {
        var processing = LaserProcessingSettings.Default;
        processing.Safety.Calibrated = true;
        return processing;
    }

    [Fact]
    public void DottedEllipseFrame_NeverVisitsNull()
    {
        var pipelined = new LaserPipeline.Session().Apply(CalibratedDefaults(), BuildDottedEllipse(80, 8, 16000, 9000), 720, out _, out var frameCount);

        // The data generation must chain dot to dot: the scanners are never commanded to the
        // park position mid-frame (that pattern reads on a scope as the beam dwelling at null
        // between every shape).
        Assert.Equal(0, CountNearNullSamples(pipelined, frameCount));
    }

    [Fact]
    public void FarApartShapes_TravelOnRampedBlanks_InBoundedSteps()
    {
        // Two dots on opposite sides of the field: 30000 units apart, blanking budget 5
        // samples, jump scale 5000. Stacked-at-destination blanking steps the full 30000
        // in one sample; a ramp spreads it so every sample step stays small.
        var input = new List<LaserPoint>
                        {
                            LaserPoint.CreateBlanked(-15000, 0),
                            new LaserPoint(-15000, 0, 0, 65535, 0, 65535),
                            new LaserPoint(-15000, 0, 0, 65535, 0, 65535),
                            LaserPoint.CreateBlanked(15000, 0),
                            new LaserPoint(15000, 0, 0, 65535, 0, 65535),
                            new LaserPoint(15000, 0, 0, 65535, 0, 65535),
                        };

        var pipelined = new LaserPipeline.Session().Apply(CalibratedDefaults(), input.ToArray(), input.Count, out _, out var frameCount);

        // 30000 travel over 5 blank samples: the largest single step must be well under the
        // full distance (stacked blanking would step 30000 exactly).
        var maxStep = MaxConsecutiveStep(pipelined, frameCount);
        Assert.True(maxStep < 20000, $"travel steps should be ramped, max step was {maxStep}");
    }

    [Fact]
    public void OptimizedTour_LoopSeamIsTheShortestLink()
    {
        // The DAC loops the finished frame continuously: the seam from the last emitted shape
        // back to the first is scanned at loop rate and must not be a cross-field jump. With
        // dots on a ring, the tour's neighbouring dots are ~1255 units apart; the seam must be
        // of that scale, not of field scale (30000+).
        var pipelined = new LaserPipeline.Session().Apply(CalibratedDefaults(), BuildDottedEllipse(80, 8, 16000, 9000), 720, out _, out var frameCount);

        // frameCount, not Length: the session returns its reused buffer, whose Length is capacity.
        var seamDx = pipelined[frameCount - 1].X - pipelined[0].X;
        var seamDy = pipelined[frameCount - 1].Y - pipelined[0].Y;
        var seam = MathF.Sqrt(seamDx * (float)seamDx + seamDy * (float)seamDy);

        Assert.True(seam < 3000, $"loop seam should be a neighbour hop, was {seam}");
    }

    [Fact]
    public void DarkApproach_RampsFromGivenPosition_ToTheFirstPoint()
    {
        var frame = new[] { new LaserPoint(10000, 5000, 0, 65535, 0, 65535) };
        var scratch = Array.Empty<LaserPoint>();

        var approached = LaserOutputStream.PrependDarkApproach(frame, -8000, -4000, ref scratch);

        // Starts near the scanners' actual position, ends exactly on the frame's first point,
        // and every step along the way is bounded (no maximal jump anywhere).
        Assert.Equal(7, approached.Length);
        Assert.True(Math.Abs(approached[0].X - (-8000)) < 3500, "ramp must start near the origin position");
        Assert.Equal(10000, approached[6].X);
        Assert.Equal(5000, approached[6].Y);

        var maxStep = MaxConsecutiveStep(approached, approached.Length);
        Assert.True(maxStep < 4000, $"approach steps should be ramped, max was {maxStep}");

        foreach (var p in approached[..6])
            Assert.Equal(0, p.I); // the whole ramp is dark
    }

    [Fact]
    public void DottedEllipseFrame_KeepsDotsAndDwell()
    {
        // The smoothing must not eat content: a 720-point dotted ellipse still carries every
        // dot with its dwell run intact (allowing the optimizer's added travel samples).
        var pipelined = new LaserPipeline.Session().Apply(CalibratedDefaults(), BuildDottedEllipse(80, 8, 16000, 9000), 720, out _, out var frameCount);

        var litRuns = 0;
        var inRun = false;
        for (var i = 0; i < frameCount; i++)
        {
            var p = pipelined[i];
            if (p.I > 0 && !inRun)
            {
                litRuns++;
                inRun = true;
            }
            else if (p.I == 0)
            {
                inRun = false;
            }
        }

        Assert.Equal(80, litRuns);
    }
}
