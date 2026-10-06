#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Settings;

namespace T3.Core.Resource.ShaderCompiling;

/// <summary>
/// Keeps the shaders that failed to compile, with the source that was handed to the compiler and the operator
/// that asked for it.
/// </summary>
/// <remarks>
/// A generated shader exists only in memory, so when one fails there is nothing to open and read - and a
/// single bad line in one field node fails the whole assembled shader, which then draws nothing at all. The
/// log says what the compiler disliked; this says what it was looking at. Kept small and in memory, with the
/// source also written next to the other temp files so external tools can be pointed straight at it.
/// </remarks>
public static class ShaderCompileFailures
{
    public sealed record Failure(
        DateTime TimeUtc,
        string Name,
        string EntryPoint,
        string Reason,
        string OwnerPath,
        Guid OwnerId,
        string? SourcePath,
        int SourceLineCount);

    /// <summary>Most recent first. A snapshot, safe to walk while compilation continues.</summary>
    public static IReadOnlyList<Failure> Recent
    {
        get
        {
            lock (_lock)
                return _failures.ToArray();
        }
    }

    public static void Clear()
    {
        lock (_lock)
            _failures.Clear();
    }

    internal static void Record(string name, string entryPoint, string reason, string sourceCode, IResourceConsumer? owner)
    {
        var (ownerPath, ownerId) = DescribeOwner(owner);
        var sourcePath = TryWriteSource(name, entryPoint, sourceCode);

        var failure = new Failure(DateTime.UtcNow, name, entryPoint, reason, ownerPath, ownerId, sourcePath,
                                  CountLines(sourceCode));

        lock (_lock)
        {
            _failures.Insert(0, failure);
            if (_failures.Count > MaxKept)
                _failures.RemoveRange(MaxKept, _failures.Count - MaxKept);
        }
    }

    /// <summary>The op that asked for the shader, and the chain of parents that leads to it.</summary>
    private static (string Path, Guid Id) DescribeOwner(IResourceConsumer? owner)
    {
        if (owner is not Instance instance)
            return (owner?.Package?.DisplayName ?? string.Empty, Guid.Empty);

        var id = instance.SymbolChildId;
        var names = new List<string>();

        for (var op = instance; op != null; op = op.Parent)
        {
            names.Add(op.Symbol.Name);
            if (names.Count > 32)
                break;
        }

        names.Reverse();
        return (string.Join(" / ", names), id);
    }

    /// <summary>
    /// Writes the source so it can be compiled by hand. Failing to write must never mask the compile error
    /// that is being reported, so the path is simply left out.
    /// </summary>
    private static string? TryWriteSource(string name, string entryPoint, string sourceCode)
    {
        try
        {
            // Beside the logs and crash reports rather than in the temp folder: this is a diagnostic the
            // user is explicitly pointed at, and nobody goes looking in a temp folder.
            var logDirectory = FileWriter.Instance?.LogDirectory;
            if (string.IsNullOrEmpty(logDirectory))
                return null;

            var folder = Path.Combine(logDirectory, "ShaderErrors");
            Directory.CreateDirectory(folder);

            // Numbered, because the interesting names repeat: every generated pixel shader is called
            // "PixelShader @psMain", and one overwriting the next would leave only the last one to look at.
            var safeName = string.Join("_", $"{name}_{entryPoint}".Split(Path.GetInvalidFileNameChars()));
            var index = Interlocked.Increment(ref _writeCounter);
            var path = Path.Combine(folder, $"{safeName}.{index:00}.hlsl");
            File.WriteAllText(path, sourceCode);
            return path;
        }
        catch (Exception e)
        {
            Log.Debug($"Could not keep a copy of the failed shader '{name}': {e.Message}");
            return null;
        }
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var lines = 1;
        foreach (var c in text)
        {
            if (c == '\n')
                lines++;
        }

        return lines;
    }

    /// <summary>Enough to cover one frame's worth of breakage without holding many megabytes of source.</summary>
    private const int MaxKept = 16;

    private static int _writeCounter;
    private static readonly Lock _lock = new();
    private static readonly List<Failure> _failures = [];
}
