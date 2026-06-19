#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using T3.Core.Logging;
using T3.Core.Video;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Windows.RenderExport;

/// <summary>
/// Locates an external <c>ffmpeg.exe</c> for tier-2 render-export — codecs the bundled LGPL build can't encode
/// in-process (software H.264/HEVC, which needs a GPL build, and HAP, which any build with the <c>hap</c>
/// encoder serves). Resolution order: <see cref="UserSettings.ConfigData.ExternalFfmpegPath"/> → the
/// <c>TIXL_FFMPEG_EXE</c> env override → <c>ffmpeg</c> on <c>PATH</c>. Availability is checked **per encoder**
/// (a system ffmpeg may have <c>hap</c> but not <c>libx264</c>), so each codec is verified with
/// <c>-h encoder=…</c>. All probing spawns ffmpeg, so callers must stay off the UI thread; results are cached.
/// </summary>
internal static class ExternalFfmpegResolver
{
    /// <summary>The FFmpeg encoder a tier-2 codec maps to, or null if the codec isn't a tier-2 candidate.</summary>
    public static string? EncoderNameForCodec(VideoExportCodec codec) => codec switch
                                                                             {
                                                                                 VideoExportCodec.Hap => "hap",
                                                                                 VideoExportCodec.HapAlpha => "hap",
                                                                                 VideoExportCodec.HapQ => "hap",
                                                                                 VideoExportCodec.H264 => "libx264",
                                                                                 _ => null,
                                                                             };

    /// <summary>True when a located external ffmpeg can encode <paramref name="codec"/> (its encoder is present).</summary>
    public static bool CanEncode(VideoExportCodec codec) => TryGetExeForCodec(codec, out _);

    /// <summary>Resolves an ffmpeg.exe that has the encoder <paramref name="codec"/> needs; null path when none.</summary>
    public static bool TryGetExeForCodec(VideoExportCodec codec, out string? exePath)
    {
        exePath = null;
        var encoderName = EncoderNameForCodec(codec);
        if (encoderName == null)
            return false;

        var exe = ResolveExePath();
        if (exe == null)
            return false;

        if (!HasEncoder(exe, encoderName))
            return false;

        exePath = exe;
        return true;
    }

    /// <summary>Drops the cached exe path and encoder results — call after the user changes the configured path.</summary>
    public static void Invalidate()
    {
        lock (_exeLock)
        {
            _exeResolved = false;
            _exePath = null;
        }

        _encoderCache.Clear();
    }

    // First working ffmpeg from: configured path → env override → PATH. Cached (incl. the "none" result).
    private static string? ResolveExePath()
    {
        lock (_exeLock)
        {
            if (_exeResolved)
                return _exePath;

            _exeResolved = true;
            _exePath = null;

            foreach (var candidate in EnumerateCandidates())
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (IsWorkingFfmpeg(candidate))
                {
                    _exePath = candidate;
                    Log.Debug($"External ffmpeg for export resolved: {candidate}");
                    break;
                }
            }

            return _exePath;
        }
    }

    private static IEnumerable<string?> EnumerateCandidates()
    {
        yield return UserSettings.Config.ExternalFfmpegPath;
        yield return Environment.GetEnvironmentVariable("TIXL_FFMPEG_EXE");
        yield return "ffmpeg"; // resolved via PATH by the OS
    }

    private static bool IsWorkingFfmpeg(string exe)
    {
        var output = RunAndCapture(exe, "-hide_banner -version");
        return output != null && output.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasEncoder(string exe, string encoderName)
    {
        var key = exe + "\n" + encoderName;
        return _encoderCache.GetOrAdd(key, _ =>
                                           {
                                               var output = RunAndCapture(exe, $"-hide_banner -h encoder={encoderName}");
                                               // A present encoder prints "Encoder <name> […]"; an absent one prints
                                               // "Codec '<name>' is not recognized" / "Unknown encoder".
                                               return output != null
                                                      && output.Contains("Encoder " + encoderName, StringComparison.OrdinalIgnoreCase);
                                           });
    }

    // Runs ffmpeg with a short timeout and returns stdout+stderr, or null if it couldn't start / timed out.
    private static string? RunAndCapture(string exe, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
                                {
                                    FileName = exe,
                                    Arguments = arguments,
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(8000))
            {
                try { process.Kill(true); } catch { /* best effort */ }
                return null;
            }

            return output.ToString();
        }
        catch (Exception e)
        {
            Log.Debug($"Probing ffmpeg '{exe}' failed: {e.Message}");
            return null;
        }
    }

    private static readonly object _exeLock = new();
    private static bool _exeResolved;
    private static string? _exePath;

    // Keyed by "<exe>\n<encoder>" so a path change (after Invalidate) re-probes.
    private static readonly ConcurrentDictionary<string, bool> _encoderCache = new();
}
