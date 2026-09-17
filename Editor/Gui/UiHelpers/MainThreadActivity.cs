#nullable enable
using System.Diagnostics;
using System.Threading;
using System.IO;
using Newtonsoft.Json;
using T3.Core.Settings;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Labels long-running work on the main thread so the stall overlay can show a message and an
/// estimated progress. Scopes are optional: an unlabeled stall still gets an overlay, just without estimate.
/// </summary>
/// <remarks>
/// Only scopes opened on the main thread count. Durations are averaged per key and persisted, because
/// compile and load times are specific to the machine.
/// </remarks>
internal static class MainThreadActivity
{
    internal readonly struct Scope : IDisposable
    {
        internal Scope(bool isActive)
        {
            _isActive = isActive;
        }

        public void Dispose()
        {
            if (_isActive)
                End();
        }

        private readonly bool _isActive;
    }

    internal readonly record struct Snapshot(string Message, double ElapsedSeconds, double EstimatedSeconds);

    internal static void InitializeForMainThread()
    {
        _mainThreadId = Environment.CurrentManagedThreadId;
        LoadEstimates();
    }

    /// <param name="estimateKey">Identifies the kind of work for duration averaging, e.g. "compile".</param>
    /// <param name="message">User facing, e.g. "Compiling MyProject...".</param>
    internal static Scope Begin(string estimateKey, string message)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return new Scope(false);

        lock (_lock)
        {
            if (_depth == 0)
            {
                _outerKey = estimateKey;
                _outerStartTimestamp = Stopwatch.GetTimestamp();
                _estimates.TryGetValue(estimateKey, out _outerEstimateSeconds);
            }

            if (_depth < _messages.Length)
                _messages[_depth] = message;

            _depth++;
        }

        return new Scope(true);
    }

    internal static bool TryGetCurrent(out Snapshot snapshot)
    {
        lock (_lock)
        {
            if (_depth == 0)
            {
                snapshot = default;
                return false;
            }

            var messageIndex = Math.Min(_depth, _messages.Length) - 1;
            snapshot = new Snapshot(_messages[messageIndex],
                                    Stopwatch.GetElapsedTime(_outerStartTimestamp).TotalSeconds,
                                    _outerEstimateSeconds);
            return true;
        }
    }

    private static void End()
    {
        var needsSaving = false;
        lock (_lock)
        {
            _depth--;
            if (_depth > 0)
                return;

            var duration = Stopwatch.GetElapsedTime(_outerStartTimestamp).TotalSeconds;
            if (duration >= MinRecordedSeconds)
            {
                _estimates[_outerKey] = _estimates.TryGetValue(_outerKey, out var average)
                                            ? average + (duration - average) * AveragingWeight
                                            : duration;
                needsSaving = true;
            }
        }

        if (needsSaving)
            SaveEstimates();
    }

    private static void LoadEstimates()
    {
        try
        {
            if (!File.Exists(EstimatesFilePath))
                return;

            var file = JsonConvert.DeserializeObject<EstimatesFile>(File.ReadAllText(EstimatesFilePath));
            if (file?.AverageSeconds == null)
                return;

            lock (_lock)
            {
                foreach (var (key, seconds) in file.AverageSeconds)
                {
                    _estimates[key] = seconds;
                }
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Can't read activity estimates: {e.Message}");
        }
    }

    private static void SaveEstimates()
    {
        try
        {
            EstimatesFile file;
            lock (_lock)
            {
                file = new EstimatesFile { AverageSeconds = new Dictionary<string, double>(_estimates) };
            }

            File.WriteAllText(EstimatesFilePath, JsonConvert.SerializeObject(file, Formatting.Indented));
        }
        catch (Exception e)
        {
            Log.Debug($"Can't save activity estimates: {e.Message}");
        }
    }

    private sealed class EstimatesFile
    {
        public int Version = 1;
        public Dictionary<string, double>? AverageSeconds;
    }

    private static string EstimatesFilePath => Path.Combine(FileLocations.SettingsDirectory, "activityEstimates.json");

    /// <summary>Shorter work barely shows an overlay and would only add noise to the average.</summary>
    private const double MinRecordedSeconds = 0.5;

    private const double AveragingWeight = 0.3;

    private static readonly Lock _lock = new();
    private static readonly Dictionary<string, double> _estimates = new();
    private static readonly string[] _messages = new string[8];
    private static int _mainThreadId = -1;
    private static int _depth;
    private static string _outerKey = string.Empty;
    private static long _outerStartTimestamp;
    private static double _outerEstimateSeconds;
}
