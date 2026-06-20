#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Video;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.RenderExport;

/// <summary>
/// Editor-side background queue that transcodes video sources into seek-friendly proxies, one at a time. The
/// actual decode→encode runs in the video assembly (<see cref="VideoExport.Factory"/>'s
/// <c>GenerateProxy</c>); this owns the queue, the sibling-file path, progress/status, and the per-machine
/// <see cref="UserSettings.ConfigData.ProxyFormat"/> / <c>ProxyResolution</c>. Editor-only — the player never
/// generates proxies.
/// </summary>
internal static class ProxyGenerationService
{
    internal enum JobState { Queued, Generating, Done, Failed }

    internal readonly record struct JobStatus(JobState State, float Progress, string? Error);

    /// <summary>Per-source status, for UI feedback.</summary>
    public static IReadOnlyDictionary<string, JobStatus> Jobs => _jobs;

    /// <summary>The sibling proxy path for a source (e.g. <c>clip.mp4</c> → <c>clip.proxy.mov</c>).</summary>
    public static string ProxyPathFor(string sourcePath)
        => ProxyPathFor(sourcePath, UserSettings.Config.ProxyFormat);

    public static string ProxyPathFor(string sourcePath, VideoExportCodec codec)
        => Path.Combine(Path.GetDirectoryName(sourcePath) ?? ".",
                        Path.GetFileNameWithoutExtension(sourcePath) + ".proxy" + codec.GetFileExtension());

    /// <summary>Queues a proxy generation for <paramref name="sourcePath"/> using the current settings. No-op if
    /// one is already queued or running for that source.</summary>
    public static void Enqueue(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            Log.Warning($"Can't generate proxy — source not found: '{sourcePath}'");
            return;
        }

        if (_jobs.TryGetValue(sourcePath, out var existing) && existing.State is JobState.Queued or JobState.Generating)
            return;

        _jobs[sourcePath] = new JobStatus(JobState.Queued, 0, null);
        _queue.Enqueue(sourcePath);

        if (Interlocked.CompareExchange(ref _workerRunning, 1, 0) == 0)
            Task.Run(ProcessQueue);
    }

    private static void ProcessQueue()
    {
        try
        {
            while (_queue.TryDequeue(out var source))
                Process(source);
        }
        finally
        {
            Interlocked.Exchange(ref _workerRunning, 0);
            // Something may have been enqueued between the empty-check and the flag reset — pick it up.
            if (!_queue.IsEmpty && Interlocked.CompareExchange(ref _workerRunning, 1, 0) == 0)
                Task.Run(ProcessQueue);
        }
    }

    private static void Process(string source)
    {
        var factory = ResolveFactory();
        if (factory == null)
        {
            _jobs[source] = new JobStatus(JobState.Failed, 0, "Video package not loaded.");
            Log.Warning($"Can't generate proxy for '{source}': the video package isn't loaded.");
            return;
        }

        var codec = UserSettings.Config.ProxyFormat;
        var scale = Math.Clamp(UserSettings.Config.ProxyResolution, 0.1f, 1f);
        var proxyPath = ProxyPathFor(source, codec);

        _jobs[source] = new JobStatus(JobState.Generating, 0, null);
        var progress = new SynchronousProgress(p => _jobs[source] = new JobStatus(JobState.Generating, (float)p, null));

        var error = factory.GenerateProxy(source, proxyPath, codec, scale, progress, CancellationToken.None);

        if (error == null)
        {
            _jobs[source] = new JobStatus(JobState.Done, 1, null);
            Log.Gated.VideoRender($"Proxy ready: {proxyPath}");
        }
        else
        {
            _jobs[source] = new JobStatus(JobState.Failed, 0, error);
            Log.Warning($"Proxy generation failed for '{source}': {error}");
        }
    }

    // The factory registers via the Video package's [ModuleInitializer]; force it if a project hasn't touched a
    // video op yet (same nudge the render-export path uses).
    private static IVideoEncoderFactory? ResolveFactory()
    {
        if (VideoExport.Factory != null)
            return VideoExport.Factory;

        foreach (var package in SymbolPackage.AllPackages)
            package.AssemblyInformation.RunModuleInitializers();

        return VideoExport.Factory;
    }

    // Reports synchronously on the calling (worker) thread, so progress can't race past the final status.
    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private static readonly ConcurrentDictionary<string, JobStatus> _jobs = new();
    private static readonly ConcurrentQueue<string> _queue = new();
    private static int _workerRunning;
}
