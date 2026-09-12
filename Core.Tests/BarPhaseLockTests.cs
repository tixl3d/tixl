using System;
using T3.Core.Audio;
using T3.Core.Audio.Timing;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Drives <see cref="BarPhaseLock"/> with synthetic wrapped phase sequences, the way the DanceAi model
/// produces them: one sample every 400/24000 s.
/// </summary>
public class BarPhaseLockTests
{
    private const double FrameSec = 400.0 / 24000.0;

    [Fact]
    public void LocksOntoSteadyTempo()
    {
        var phaseLock = new BarPhaseLock();
        var clock = new SyntheticPhase(bpm: 128);

        RunSeconds(phaseLock, clock, seconds: 8, smoothing: 0.5f);

        Assert.True(phaseLock.IsLocked);
        Assert.InRange(phaseLock.Bpm, 127, 129);
        Assert.InRange(Math.Abs(PhaseDistance(phaseLock.BarTime, clock.Bars)), 0, 0.02);
    }

    [Fact]
    public void FollowsTempoChange()
    {
        var phaseLock = new BarPhaseLock();
        var clock = new SyntheticPhase(bpm: 120);

        RunSeconds(phaseLock, clock, seconds: 8, smoothing: 0f);
        clock.Bpm = 140;
        RunSeconds(phaseLock, clock, seconds: 12, smoothing: 0f);

        Assert.InRange(phaseLock.Bpm, 138, 142);
        Assert.InRange(Math.Abs(PhaseDistance(phaseLock.BarTime, clock.Bars)), 0, 0.03);
    }

    [Fact]
    public void HoldsTempoThroughStall()
    {
        var phaseLock = new BarPhaseLock();
        var clock = new SyntheticPhase(bpm: 100);

        RunSeconds(phaseLock, clock, seconds: 8, smoothing: 0.5f);
        var bpmBeforeStall = phaseLock.Bpm;

        clock.IsStalled = true;
        RunSeconds(phaseLock, clock, seconds: 6, smoothing: 0.5f);

        Assert.InRange(phaseLock.Bpm, bpmBeforeStall - 0.5, bpmBeforeStall + 0.5);
    }

    [Fact]
    public void NeverStepsTheClock()
    {
        var phaseLock = new BarPhaseLock();
        var clock = new SyntheticPhase(bpm: 128);

        RunSeconds(phaseLock, clock, seconds: 4, smoothing: 0f);

        // Force a half-bar disagreement, the worst case for a wrapped phase detector.
        clock.Bars += 0.5;
        var previous = phaseLock.BarTime;
        var maxStep = 0.0;
        for (var frame = 0; frame < (int)(8 / FrameSec); frame++)
        {
            Step(phaseLock, clock, 0f);
            maxStep = Math.Max(maxStep, Math.Abs(phaseLock.BarTime - previous));
            previous = phaseLock.BarTime;
        }

        // One frame at 128 BPM is 0.0089 bars; a 25 % slew on top stays well below 0.012.
        Assert.InRange(maxStep, 0, 0.012);
        Assert.InRange(Math.Abs(PhaseDistance(phaseLock.BarTime, clock.Bars)), 0, 0.03);
    }

    private static void RunSeconds(BarPhaseLock phaseLock, SyntheticPhase clock, double seconds, float smoothing)
    {
        var frames = (int)(seconds / FrameSec);
        for (var frame = 0; frame < frames; frame++)
        {
            Step(phaseLock, clock, smoothing);
        }
    }

    private static void Step(BarPhaseLock phaseLock, SyntheticPhase clock, float smoothing)
    {
        clock.Advance(FrameSec);
        phaseLock.Update(clock.Phase, clock.BarDurationSec, clock.TimeSec, FrameSec, smoothing);
    }

    /// <summary>Wrapped distance between two bar positions, in [-0.5, 0.5).</summary>
    private static double PhaseDistance(double a, double b)
    {
        var d = (a - b) % 1;
        if (d < -0.5) d += 1;
        if (d >= 0.5) d -= 1;
        return d;
    }

    private sealed class SyntheticPhase
    {
        public SyntheticPhase(double bpm)
        {
            Bpm = bpm;
        }

        public double Bpm;
        public double Bars;
        public double TimeSec;
        public bool IsStalled;

        public double Phase => Bars - Math.Floor(Bars);
        public double BarDurationSec => 240.0 / Bpm;

        public void Advance(double dtSec)
        {
            TimeSec += dtSec;
            if (!IsStalled)
                Bars += dtSec * Bpm / 240.0;
        }
    }
}
