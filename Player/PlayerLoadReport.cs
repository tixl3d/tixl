#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using T3.Core.Logging;
using T3.Serialization;

namespace T3.Player;

/// <summary>
/// Keeps the most recent log message for the loading screen's status line.
/// </summary>
internal sealed class LastLogLineWriter : ILogWriter
{
    public ILogEntry.EntryLevel Filter { get; set; } = ILogEntry.EntryLevel.All;

    public string? Text
    {
        get
        {
            lock (_lock)
                return _text;
        }
    }

    public void ProcessEntry(ILogEntry entry)
    {
        // Only the first line fits the status line; multi-line messages (settings dumps, stack traces) are cut
        var message = entry.Message;
        var lineBreak = message.IndexOfAny(_lineBreaks);
        if (lineBreak >= 0)
            message = message[..lineBreak];

        lock (_lock)
            _text = message.Trim();
    }

    public void Dispose()
    {
    }

    private static readonly char[] _lineBreaks = ['\r', '\n'];
    private readonly object _lock = new();
    private string? _text;
}

/// <summary>
/// What the player loaded and how long each stage took. Logged once after start-up and saved next to the log,
/// so slow exports can be diagnosed without attaching a profiler.
/// </summary>
internal sealed class PlayerLoadReport
{
    public int PackageCount;
    public int SymbolCount;
    public int InstanceCount;
    public int ShadersCompiled;
    public int ShadersFromCache;
    public int AssetFileCount;
    public long AssetBytes;
    public double TotalSeconds;
    public readonly Dictionary<string, double> StageSeconds = new();

    public void BeginStage(string name)
    {
        EndStage();
        _currentStage = name;
        _stageWatch.Restart();
    }

    public void EndStage()
    {
        if (_currentStage == null)
            return;

        StageSeconds[_currentStage] = _stageWatch.Elapsed.TotalSeconds;
        _currentStage = null;
    }

    public void Complete()
    {
        EndStage();
        TotalSeconds = _totalWatch.Elapsed.TotalSeconds;
    }

    public void CountAssets(string operatorsDirectory, string assetsSubfolder)
    {
        try
        {
            if (!Directory.Exists(operatorsDirectory))
                return;

            foreach (var packageDir in Directory.EnumerateDirectories(operatorsDirectory))
            {
                var assetDir = Path.Combine(packageDir, assetsSubfolder);
                if (!Directory.Exists(assetDir))
                    continue;

                foreach (var file in Directory.EnumerateFiles(assetDir, "*", SearchOption.AllDirectories))
                {
                    AssetFileCount++;
                    AssetBytes += new FileInfo(file).Length;
                }
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Failed to measure assets: {e.Message}");
        }
    }

    public void LogAndSave(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Loaded in {TotalSeconds:0.00}s: {PackageCount} packages, {SymbolCount} symbols, {InstanceCount} instances, " +
                      $"{ShadersCompiled} shaders compiled, {ShadersFromCache} from cache, " +
                      $"{AssetFileCount} asset files ({AssetBytes / (1024.0 * 1024.0):0.0} MB)");
        foreach (var (stage, seconds) in StageSeconds)
        {
            sb.AppendLine($"  {stage}: {seconds:0.00}s");
        }

        Log.Info(sb.ToString().TrimEnd());

        try
        {
            JsonUtils.TrySaveJson(this, path);
        }
        catch (Exception e)
        {
            Log.Debug($"Failed to save load report: {e.Message}");
        }
    }

    private readonly Stopwatch _totalWatch = Stopwatch.StartNew();
    private readonly Stopwatch _stageWatch = new();
    private string? _currentStage;
}
