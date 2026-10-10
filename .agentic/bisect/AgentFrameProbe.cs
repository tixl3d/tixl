// Bisect measurement probe. Injected into a checked-out commit by inject-probe.ps1; never commit it into Editor/.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ImGuiNET;
using T3.Core.Logging;

internal static class AgentFrameProbe
{
    /** Replaced by inject-probe.ps1 with "sha,date,subject". */
    private const string CommitInfo = "__COMMIT_INFO__";
    /** Replaced by inject-probe.ps1 with the folder holding the bisect tools. */
    private const string OutputFolder = @"__OUTPUT_FOLDER__";
    private const double SettleSec = 2;
    private const double MeasureSec = 10;

    private enum Phases { Idle, SettleVsyncOn, MeasureVsyncOn, SettleVsyncOff, MeasureVsyncOff }

    private static Phases _phase = Phases.Idle;
    private static long _phaseStartTicks;
    private static long _lastNewFrameTicks;
    private static long _newFrameTicks;
    private static long _beforePresentTicks;
    private static bool _haveWork;
    private static float _pendingWorkMs;
    private static float _pendingPresentMs;
    private static bool? _originalVsync;
    private static readonly List<float> Interval = new(4096), Work = new(4096), Present = new(4096);
    private static readonly StringBuilder Lines = new();
    private static readonly StringBuilder Raw = new();
    private static string _stamp = "";
    private static long _exitAtTicks;

    public static void DrawMeasureButton()
    {
        ImGui.SameLine();
        var label = _phase == Phases.Idle
                        ? "Start Measure"
                        : $"{_phase} {Math.Max(0, PhaseDurationSec(_phase) - PhaseElapsedSec(Stopwatch.GetTimestamp())):0}s";
        if (ImGui.Button(label + "###agentMeasure") && _phase == Phases.Idle)
        {
            _stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Lines.Clear();
            Raw.Clear();
            _originalVsync = GetVsync();
            SetVsync(true);
            Enter(Phases.SettleVsyncOn, Stopwatch.GetTimestamp());
        }
    }

    public static void OnNewFrame()
    {
        var now = Stopwatch.GetTimestamp();
        if (_exitAtTicks != 0 && now >= _exitAtTicks)
            Environment.Exit(0);
        var intervalMs = _lastNewFrameTicks == 0 ? 0f : (float)((now - _lastNewFrameTicks) * 1000.0 / Stopwatch.Frequency);
        _lastNewFrameTicks = now;

        if (_phase is Phases.MeasureVsyncOn or Phases.MeasureVsyncOff && _haveWork)
        {
            Interval.Add(intervalMs);
            Work.Add(_pendingWorkMs);
            Present.Add(_pendingPresentMs);
        }
        _haveWork = false;
        _newFrameTicks = now;

        if (_phase == Phases.Idle || PhaseElapsedSec(now) < PhaseDurationSec(_phase))
            return;

        try
        {
            switch (_phase)
            {
                case Phases.SettleVsyncOn:
                    Enter(Phases.MeasureVsyncOn, now);
                    break;
                case Phases.MeasureVsyncOn:
                    Summarize("vsync_on");
                    SetVsync(false);
                    Enter(Phases.SettleVsyncOff, now);
                    break;
                case Phases.SettleVsyncOff:
                    Enter(Phases.MeasureVsyncOff, now);
                    break;
                case Phases.MeasureVsyncOff:
                    Summarize("vsync_off");
                    Complete();
                    break;
            }
        }
        catch (Exception e)
        {
            _phase = Phases.Idle;
            Log.Error("Measure failed: " + e.Message);
        }
    }

    public static void BeforePresent()
    {
        _beforePresentTicks = Stopwatch.GetTimestamp();
    }

    public static void AfterPresent()
    {
        if (_newFrameTicks == 0)
            return;
        var now = Stopwatch.GetTimestamp();
        _pendingWorkMs = (float)((_beforePresentTicks - _newFrameTicks) * 1000.0 / Stopwatch.Frequency);
        _pendingPresentMs = (float)((now - _beforePresentTicks) * 1000.0 / Stopwatch.Frequency);
        _haveWork = true;
    }

    private static void Complete()
    {
        _phase = Phases.Idle;
        if (_originalVsync != null)
            SetVsync(_originalVsync.Value);

        Directory.CreateDirectory(Path.Combine(OutputFolder, "raw"));
        var summaryPath = Path.Combine(OutputFolder, "measurements.csv");
        if (!File.Exists(summaryPath))
        {
            File.WriteAllText(summaryPath,
                              "sha,commit_date,subject,measured_at,phase,frames,interval_mean,interval_median,interval_p95,interval_p99,interval_max,interval_std,hitches,work_mean,present_mean,present_p95\n");
        }
        File.AppendAllText(summaryPath, Lines.ToString());

        var sha = CommitInfo.Split(',')[0];
        var rawPath = Path.Combine(OutputFolder, "raw", $"{sha}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(rawPath, "phase,i,interval_ms,work_ms,present_ms\n" + Raw);

        Log.Info($"Measure complete for {sha}: appended to {summaryPath}\n{Lines.ToString().TrimEnd()}");
        Log.Info("Closing editor in 1s.");
        _exitAtTicks = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
    }

    private static void Summarize(string label)
    {
        var c = CultureInfo.InvariantCulture;
        if (Interval.Count == 0)
        {
            Lines.Append(CommitInfo).Append(',').Append(_stamp).Append(',').Append(label).AppendLine(",0,,,,,,,,,,");
            return;
        }

        var sorted = Interval.ToArray();
        Array.Sort(sorted);
        var n = sorted.Length;
        double sum = 0, sumSq = 0;
        foreach (var v in sorted)
        {
            sum += v;
            sumSq += v * v;
        }
        var mean = sum / n;
        var std = Math.Sqrt(Math.Max(0, sumSq / n - mean * mean));
        var median = sorted[n / 2];
        var hitches = 0;
        foreach (var v in sorted)
        {
            if (v > median * 1.5f)
                hitches++;
        }

        var presentSorted = Present.ToArray();
        Array.Sort(presentSorted);

        Lines.Append(CommitInfo).Append(',').Append(_stamp).Append(',').Append(label).Append(',')
             .Append(n.ToString(c)).Append(',')
             .Append(F(mean)).Append(',').Append(F(median)).Append(',')
             .Append(F(Quantile(sorted, 0.95))).Append(',').Append(F(Quantile(sorted, 0.99))).Append(',')
             .Append(F(sorted[n - 1])).Append(',').Append(F(std)).Append(',')
             .Append(hitches.ToString(c)).Append(',')
             .Append(F(Mean(Work))).Append(',').Append(F(Mean(Present))).Append(',')
             .AppendLine(F(Quantile(presentSorted, 0.95)));

        for (var i = 0; i < Interval.Count; i++)
        {
            Raw.Append(label).Append(',').Append(i).Append(',')
               .Append(F(Interval[i])).Append(',').Append(F(Work[i])).Append(',').AppendLine(F(Present[i]));
        }
    }

    private static void Enter(Phases phase, long now)
    {
        _phase = phase;
        _phaseStartTicks = now;
        Interval.Clear();
        Work.Clear();
        Present.Clear();
    }

    private static double PhaseDurationSec(Phases phase) => phase is Phases.MeasureVsyncOn or Phases.MeasureVsyncOff ? MeasureSec : SettleSec;
    private static double PhaseElapsedSec(long now) => (now - _phaseStartTicks) / (double)Stopwatch.Frequency;
    private static float Quantile(float[] sorted, double q) => sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static double Mean(List<float> values)
    {
        double sum = 0;
        foreach (var v in values)
            sum += v;
        return values.Count == 0 ? 0 : sum / values.Count;
    }

    /** UseVSync is a field in older editors and a settings-backed property in newer ones. */
    private static bool? GetVsync()
    {
        var t3Ui = FindType("T3Ui");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        return (bool?)(t3Ui?.GetProperty("UseVSync", flags)?.GetValue(null) ?? t3Ui?.GetField("UseVSync", flags)?.GetValue(null));
    }

    private static void SetVsync(bool on)
    {
        var t3Ui = FindType("T3Ui");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var prop = t3Ui?.GetProperty("UseVSync", flags);
        if (prop != null)
            prop.SetValue(null, on);
        else
            t3Ui?.GetField("UseVSync", flags)?.SetValue(null, on);
    }

    private static Type? FindType(string name)
    {
        foreach (var t in typeof(AgentFrameProbe).Assembly.GetTypes())
        {
            if (t.Name == name)
                return t;
        }
        return null;
    }
}
