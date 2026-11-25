#nullable enable
using System.Diagnostics;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Editor.Gui.UiHelpers;
using T3.Graphics.Compat;

namespace T3.Editor.Gui.Windows.Analyze.Benchmark;

/// <summary>
/// Runs the benchmark suite, one measured frame per editor frame. Each test is instantiated on its own and pulled at
/// a fixed resolution; its load is swept small to large until a step takes longer than <see cref="StopAboveMs"/>, then
/// back down and up again, so every step is measured <see cref="PassCount"/> times. Every measured frame waits for the
/// GPU before and after the test, so only the test's own work is timed.
/// </summary>
internal static class BenchmarkRunner
{
    /** The end of realtime (20 fps): the sweep stops at the first step above it. */
    public const double StopAboveMs = 50;

    /** Three samples per step show how much the machine varies; alternating directions balance out heat build-up. */
    private const int PassCount = 3;

    public static bool IsRunning => _tests.Count > 0;

    /** The run being measured, or the last one finished in this session. */
    public static BenchmarkRun? LatestRun { get; private set; }

    /** Bumped whenever a run finishes and is saved, so views can reload the saved runs. */
    public static int SavedRunsVersion { get; private set; }

    public static string Progress { get; private set; } = string.Empty;

    /// <param name="note">Stored with the result, e.g. what is uncommitted on top of the build's commit.</param>
    public static void Start(string note = "")
    {
        if (IsRunning)
            return;

        _tests.Clear();
        _tests.AddRange(BenchmarkSuite.Tests);
        _testIndex = -1;

        var system = BenchmarkSystemInfo.Capture();
        LatestRun = new BenchmarkRun
                        {
                            StartedUtc = DateTime.UtcNow,
                            System = system,
                            Label = CreateLabel(system),
                            Note = note,
                        };

        _savedUseVSync = UserSettings.Config.UseVSync;
        _savedTargetFrameRate = UserSettings.Config.TargetFrameRate;

        // A GPU waiting for vblank clocks down between frames, which makes the small steps look slow.
        UserSettings.Config.UseVSync = false;
        UserSettings.Config.TargetFrameRate = 0;

        StartNextTest();
    }

    public static void Cancel()
    {
        if (!IsRunning)
            return;

        if (_currentResult != null)
            _currentResult.StopReason = "Cancelled";

        Finish(save: false);
        Progress = "Cancelled";
    }

    /// <summary>Called once per frame, before any window draws.</summary>
    public static void Update()
    {
        if (!IsRunning)
            return;

        try
        {
            UpdateStep();
        }
        catch (Exception e)
        {
            Log.Warning($"Benchmark test {_tests[_testIndex].Key} failed: {e.Message}");
            if (_currentResult != null)
                _currentResult.StopReason = $"Error: {e.Message}";

            StartNextTest();
        }
    }

    private static void UpdateStep()
    {
        if (_output == null || _loadInput == null)
        {
            StartNextTest();
            return;
        }

        var test = _tests[_testIndex];
        if (_phase == Phase.PreWarm)
        {
            Progress = $"{test.Category}: warming up";
            MeasureFrame(out _, out _, out _);
            _preWarmFrames++;
            if (_preWarmFrames >= MinPreWarmFrames && Stopwatch.GetElapsedTime(_phaseStartTimestamp).TotalSeconds >= PreWarmSec)
                _phase = Phase.Setup;

            return;
        }

        var load = _passLoads[_passStepIndex];
        if (_phase == Phase.Setup)
        {
            SetLoad(load);
            _stepSamples.Clear();
            _cpuSamples.Clear();
            _gpuSamples.Clear();
            _phaseStartTimestamp = Stopwatch.GetTimestamp();
            _phase = Phase.WarmUp;
        }

        Progress = $"{test.Category}: {FormatLoad(load)} {test.UnitLabel}, pass {_pass + 1} of {PassCount}";

        MeasureFrame(out var stepMs, out var cpuMs, out var gpuMs);
        var phaseSec = Stopwatch.GetElapsedTime(_phaseStartTimestamp).TotalSeconds;

        if (_phase == Phase.WarmUp)
        {
            _stepSamples.Add(stepMs);
            if (!IsWarmedUp(phaseSec))
                return;

            _stepSamples.Clear();
            _phaseStartTimestamp = Stopwatch.GetTimestamp();
            _phase = Phase.Measure;
            return;
        }

        _stepSamples.Add(stepMs);
        _cpuSamples.Add(cpuMs);
        if (!double.IsNaN(gpuMs))
            _gpuSamples.Add(gpuMs);

        var hasEnoughFrames = _stepSamples.Count >= MeasuredFrameCount
                              || (_stepSamples.Count >= MinMeasuredFrameCount && phaseSec > MaxMeasureSec);
        if (!hasEnoughFrames)
            return;

        var step = new BenchmarkStep
                       {
                           Load = load,
                           Pass = _pass,
                           StepMs = Percentile(_stepSamples, 0.5),
                           StepP90Ms = Percentile(_stepSamples, 0.9),
                           CpuMs = Percentile(_cpuSamples, 0.5),
                           CpuP90Ms = Percentile(_cpuSamples, 0.9),
                           GpuMs = _gpuSamples.Count > 0 ? Percentile(_gpuSamples, 0.5) : double.NaN,
                       };
        _currentResult!.Steps.Add(step);
        AdvanceStep(test, step);
    }

    /// <summary>At least 10 frames, then until two consecutive 10-frame medians agree within 5 %, at most 3 s.</summary>
    private static bool IsWarmedUp(double phaseSec)
    {
        const int window = 10;
        var count = _stepSamples.Count;
        if (count >= MinMeasuredFrameCount && phaseSec > MaxWarmUpSec)
            return true;

        if (count < 2 * window)
            return false;

        var recent = Percentile(_stepSamples.GetRange(count - window, window), 0.5);
        var before = Percentile(_stepSamples.GetRange(count - 2 * window, window), 0.5);
        return Math.Abs(recent - before) <= 0.05 * Math.Max(recent, before);
    }

    private static void AdvanceStep(BenchmarkTest test, BenchmarkStep step)
    {
        _phase = Phase.Setup;
        var plotted = test.PlotsCpuTime ? step.CpuMs : step.StepMs;

        if (_pass == 0)
        {
            string? stopReason = null;
            if (plotted > StopAboveMs)
            {
                stopReason = $"Above {StopAboveMs:0} ms";
            }
            else if (IsGpuMemoryLow())
            {
                stopReason = "GPU memory above 80 %";
            }
            else if (_passStepIndex == _passLoads.Count - 1)
            {
                stopReason = "Range complete";
            }

            if (stopReason == null)
            {
                _passStepIndex++;
                return;
            }

            _currentResult!.StopReason = stopReason;

            // The later passes go back and forth over the steps the first one reached.
            _passLoads.RemoveRange(_passStepIndex + 1, _passLoads.Count - _passStepIndex - 1);
            StartNextPass();
            return;
        }

        if (_passStepIndex < _passLoads.Count - 1)
        {
            _passStepIndex++;
            return;
        }

        if (_pass < PassCount - 1)
        {
            StartNextPass();
            return;
        }

        StartNextTest();
    }

    private static void StartNextPass()
    {
        _passLoads.Reverse();
        _pass++;
        _passStepIndex = 0;
    }

    private static void StartNextTest()
    {
        ReleaseCurrentTest();
        _testIndex++;
        if (_testIndex >= _tests.Count)
        {
            Finish(save: true);
            return;
        }

        var test = _tests[_testIndex];
        _currentResult = new BenchmarkTestResult { Key = test.Key };
        LatestRun!.Tests.Add(_currentResult);

        if (!SymbolRegistry.TryGetSymbol(test.SymbolId, out var symbol)
            || !symbol.TryGetParentlessInstance(out var instance))
        {
            _currentResult.StopReason = "Test operator not found";
            Log.Warning($"Benchmark test {test.Key}: symbol {test.SymbolId} could not be instantiated.");
            StartNextTest();
            return;
        }

        _currentResult.Fingerprint = BenchmarkFingerprint.Compute(symbol);
        _loadInput = FindLoadInput(symbol, instance);
        _output = instance.Outputs.Count > 0 ? instance.Outputs[0] : null;
        if (_loadInput == null || _output == null)
        {
            _currentResult.StopReason = "Test operator needs an int input named Load and an output";
            StartNextTest();
            return;
        }

        _passLoads.Clear();
        _passLoads.AddRange(BenchmarkSuite.GetLoads(test));
        _pass = 0;
        _passStepIndex = 0;

        // .NET compiles hot code again with optimizations after it has run for a while. Without this, the first
        // pass measures the unoptimized code and disagrees with the second.
        SetLoad(test.Reference);
        _preWarmFrames = 0;
        _phaseStartTimestamp = Stopwatch.GetTimestamp();
        _phase = Phase.PreWarm;
    }

    private static InputSlot<int>? FindLoadInput(Symbol symbol, Instance instance)
    {
        foreach (var definition in symbol.InputDefinitions)
        {
            if (definition.Name != "Load")
                continue;

            foreach (var input in instance.Inputs)
            {
                if (input.Id == definition.Id)
                    return input as InputSlot<int>;
            }
        }

        return null;
    }

    /// <summary>Leaves the test at its smallest load, so large buffers and targets shrink again.</summary>
    private static void ReleaseCurrentTest()
    {
        if (_loadInput != null && _output != null && _testIndex >= 0 && _testIndex < _tests.Count)
        {
            try
            {
                SetLoad(_tests[_testIndex].MinLoad);
                DirtyFlag.GlobalInvalidationTick++;
                _output.InvalidateGraph();
                _output.Update(_evaluationContext);
            }
            catch (Exception e)
            {
                Log.Debug($"Resetting benchmark test {_tests[_testIndex].Key} failed: {e.Message}");
            }
        }

        _loadInput = null;
        _output = null;
    }

    /// <summary>
    /// <see cref="InputSlot{T}.SetTypedInputValue"/> ignores the change when the slot was already visited in the current
    /// invalidation pass, so consumers would keep the previous load.
    /// </summary>
    private static void SetLoad(int load)
    {
        _loadInput!.SetTypedInputValue(load);
        _loadInput.DirtyFlag.ForceInvalidate();
    }

    private static void Finish(bool save)
    {
        ReleaseCurrentTest();
        _tests.Clear();
        _currentResult = null;

        UserSettings.Config.UseVSync = _savedUseVSync;
        UserSettings.Config.TargetFrameRate = _savedTargetFrameRate;

        if (!save || LatestRun == null)
            return;

        Progress = LatestRun.TrySave()
                       ? $"Saved {System.IO.Path.GetFileName(LatestRun.FilePath)}"
                       : "Finished, but the result could not be saved";
        SavedRunsVersion++;
    }

    /// <summary>
    /// Times one evaluation of the test. Earlier work of this frame is drained first; afterwards the CPU waits until
    /// the GPU has finished, so the step time covers exactly the test's work on both processors.
    /// </summary>
    private static void MeasureFrame(out double stepMs, out double cpuMs, out double gpuMs)
    {
        var device = ResourceManager.Device;
        var context = device.ImmediateContext;
        context.Flush();
        device.Backend.WaitForGpu();

        _timestampQueries ??= new TimestampQueries(device);
        context.Begin(_timestampQueries.Disjoint);
        context.End(_timestampQueries.Start);

        // Tests read this through [GetFloatVar], which re-evaluates every frame, so their work runs every frame
        // like an animated scene's instead of returning cached results.
        _evaluationContext.FloatVariables[FrameVariableName] = _frameCounter++;

        var startTimestamp = Stopwatch.GetTimestamp();
        _evaluationContext.RequestedResolution = _resolution;
        _evaluationContext.NotifyPullResolution();
        DirtyFlag.GlobalInvalidationTick++;
        _output!.InvalidateGraph();
        _output.Update(_evaluationContext);
        var evaluatedTimestamp = Stopwatch.GetTimestamp();

        context.End(_timestampQueries.End);
        context.End(_timestampQueries.Disjoint);
        context.Flush();
        device.Backend.WaitForGpu();

        cpuMs = Stopwatch.GetElapsedTime(startTimestamp, evaluatedTimestamp).TotalMilliseconds;
        stepMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        gpuMs = _timestampQueries.TryRead(context, out var measuredGpuMs) ? measuredGpuMs : double.NaN;
    }

    private static bool IsGpuMemoryLow()
    {
        var report = ResourceManager.Device.Backend.QueryMemory();
        return report.BudgetBytes > 0 && report.UsedBytes > 0.8 * report.BudgetBytes;
    }

    private static double Percentile(List<double> values, double fraction)
    {
        if (values.Count == 0)
            return double.NaN;

        _sortScratch.Clear();
        _sortScratch.AddRange(values);
        _sortScratch.Sort();
        var index = (int)Math.Clamp(Math.Ceiling(fraction * _sortScratch.Count) - 1, 0, _sortScratch.Count - 1);
        return _sortScratch[index];
    }

    private static string CreateLabel(BenchmarkSystemInfo system)
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "mac" : OperatingSystem.IsLinux() ? "linux" : "other";
        var backend = App.ProgramWindows.UseVulkanBackend ? "vulkan" : "d3d11";
        return system.IsDebugBuild ? $"{os}-{backend}-debug" : $"{os}-{backend}";
    }

    public static string FormatLoad(double load)
    {
        return load switch
                   {
                       >= 1_000_000 => $"{load / 1_000_000:0.#}M",
                       >= 1_000     => $"{load / 1_000:0.#}K",
                       _            => $"{load:0}",
                   };
    }

    private sealed class TimestampQueries(Device device)
    {
        public readonly Query Disjoint = new(device, new QueryDescription { Type = QueryType.TimestampDisjoint });
        public readonly Query Start = new(device, new QueryDescription { Type = QueryType.Timestamp });
        public readonly Query End = new(device, new QueryDescription { Type = QueryType.Timestamp });

        public bool TryRead(DeviceContext context, out double gpuMs)
        {
            gpuMs = double.NaN;
            if (!context.GetData(Disjoint, AsynchronousFlags.DoNotFlush, out QueryDataTimestampDisjoint disjoint)
                || !context.GetData(Start, AsynchronousFlags.DoNotFlush, out long startTicks)
                || !context.GetData(End, AsynchronousFlags.DoNotFlush, out long endTicks))
                return false;

            // Disjoint means the GPU clock changed in between; the ticks are meaningless then.
            if (disjoint.Disjoint || disjoint.Frequency == 0 || endTicks < startTicks)
                return false;

            gpuMs = (endTicks - startTicks) * 1000.0 / disjoint.Frequency;
            return true;
        }
    }

    private enum Phase
    {
        PreWarm,
        Setup,
        WarmUp,
        Measure,
    }

    private const string FrameVariableName = "BenchmarkFrame";
    private const int MeasuredFrameCount = 30;
    private const int MinMeasuredFrameCount = 5;
    private const double MaxMeasureSec = 2;
    private const double MaxWarmUpSec = 3;
    private const double PreWarmSec = 1.5;
    private const int MinPreWarmFrames = 30;

    private static readonly Int2 _resolution = new(1920, 1080);
    private static readonly List<BenchmarkTest> _tests = [];
    private static readonly List<int> _passLoads = [];
    private static readonly List<double> _stepSamples = [];
    private static readonly List<double> _cpuSamples = [];
    private static readonly List<double> _gpuSamples = [];
    private static readonly List<double> _sortScratch = [];
    private static readonly EvaluationContext _evaluationContext = new();

    private static int _testIndex;
    private static int _frameCounter;
    private static int _preWarmFrames;
    private static int _pass;
    private static int _passStepIndex;
    private static Phase _phase;
    private static long _phaseStartTimestamp;
    private static BenchmarkTestResult? _currentResult;
    private static InputSlot<int>? _loadInput;
    private static ISlot? _output;
    private static TimestampQueries? _timestampQueries;
    private static bool _savedUseVSync;
    private static int _savedTargetFrameRate;
}
