using System;
using System.Collections.Generic;
using System.Diagnostics;
using T3.Core.Operator;

namespace T3.Core.Stats;

/// <summary>
/// Opt-in per-operator update timing, switched on from the debug bridge. While disabled the only cost
/// in the update path is a single static bool test. Durations are inclusive: an operator's time contains
/// the time of every operator it evaluates, so rows must not be summed.
/// </summary>
public static class OpTimings
{
    /// <summary>When false, <see cref="Measure"/> must not be called.</summary>
    public static volatile bool Enabled;

    public static void Measure(string operatorName, Action<EvaluationContext>? updateAction, EvaluationContext context)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            updateAction?.Invoke(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetTimestamp() - start;
            lock (_lock)
            {
                if (!_frame.TryGetValue(operatorName, out var accumulator))
                {
                    accumulator = new Accumulator();
                    _frame[operatorName] = accumulator;
                }

                accumulator.Ticks += elapsed;
                accumulator.Count++;
            }
        }
    }

    public static void StartNewFrame()
    {
        if (!Enabled)
            return;

        lock (_lock)
        {
            (_previous, _frame) = (_frame, _previous);
            foreach (var accumulator in _frame.Values)
            {
                accumulator.Ticks = 0;
                accumulator.Count = 0;
            }
        }
    }

    public static void Reset()
    {
        lock (_lock)
        {
            _frame.Clear();
            _previous.Clear();
        }
    }

    /// <summary>The slowest operators of the previous frame, slowest first.</summary>
    public static List<(string Name, double Milliseconds, int Count)> Snapshot(int maxCount)
    {
        var result = new List<(string Name, double Milliseconds, int Count)>();
        var ticksPerMs = Stopwatch.Frequency / 1000.0;

        lock (_lock)
        {
            foreach (var (name, accumulator) in _previous)
            {
                if (accumulator.Count > 0)
                    result.Add((name, accumulator.Ticks / ticksPerMs, accumulator.Count));
            }
        }

        result.Sort((a, b) => b.Milliseconds.CompareTo(a.Milliseconds));
        if (result.Count > maxCount)
            result.RemoveRange(maxCount, result.Count - maxCount);

        return result;
    }

    private sealed class Accumulator
    {
        public long Ticks;
        public int Count;
    }

    private static readonly object _lock = new();
    private static Dictionary<string, Accumulator> _frame = new();
    private static Dictionary<string, Accumulator> _previous = new();
}
