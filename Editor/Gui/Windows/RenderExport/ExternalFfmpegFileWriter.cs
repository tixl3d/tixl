#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using T3.Core.Logging;
using T3.Core.Video;

namespace T3.Editor.Gui.Windows.RenderExport;

/// <summary>
/// Tier-2 render-export sink: pipes raw RGBA frames into an external <c>ffmpeg.exe</c> for codecs the bundled
/// LGPL build can't encode in-process (HAP; software H.264/HEVC). Implements the Core <see cref="IVideoFileWriter"/>
/// so it slots behind the same texture-readback adapter as the in-process encoder.
///
/// Video streams on the subprocess's stdin. Audio (when present) can't share that single pipe, so it's a
/// two-pass mux: pass 1 encodes a temp video-only file while the PCM is appended to a temp file, then pass 2
/// muxes them (video stream-copied, audio encoded to AAC). With no audio it's a single pass straight to the
/// target. Not thread-safe — driven by one render loop.
/// </summary>
internal sealed class ExternalFfmpegFileWriter : IVideoFileWriter
{
    /// <summary>Returns null (with a reason) when no external ffmpeg can encode the requested codec.</summary>
    public static ExternalFfmpegFileWriter? TryCreate(RenderProcess.ExportSession session, out string? error)
    {
        error = null;
        var codec = session.Settings.VideoCodec;

        if (!ExternalFfmpegResolver.TryGetExeForCodec(codec, out var exePath) || exePath == null)
        {
            error = $"No external FFmpeg with an encoder for {codec} was found.";
            return null;
        }

        try
        {
            return new ExternalFfmpegFileWriter(exePath, session);
        }
        catch (Exception e)
        {
            error = "Failed to start the external FFmpeg encoder: " + e.Message;
            return null;
        }
    }

    private ExternalFfmpegFileWriter(string exePath, RenderProcess.ExportSession session)
    {
        var s = session.Settings;
        _exePath = exePath;
        _targetPath = session.TargetFilePath;
        _codec = s.VideoCodec;
        _bitRate = s.Bitrate;

        // Crop to the encoder's block constraint (HAP needs ×4; H.264/yuv420p needs even) — matches the
        // in-process path; the readback feeds full frames and we pipe only the rounded region.
        var blockSize = IsHap(_codec) ? 4 : 2;
        _encoderWidth = Math.Max(blockSize, session.RenderToFileResolution.Width / blockSize * blockSize);
        _encoderHeight = Math.Max(blockSize, session.RenderToFileResolution.Height / blockSize * blockSize);
        _rowBytes = _encoderWidth * 4;

        var fps = Math.Max(1, (int)Math.Round(s.FrameRate));
        _audioChannels = RenderAudioInfo.SoundtrackChannels();
        _audioSampleRate = RenderAudioInfo.SoundtrackSampleRate();
        _hasAudio = s.ExportAudio && _audioChannels > 0;

        // With audio, pass 1 writes a temp video-only file (muxed with audio in pass 2); otherwise straight out.
        var videoTarget = _targetPath;
        if (_hasAudio)
        {
            var dir = Path.GetDirectoryName(_targetPath) ?? ".";
            var stem = Path.GetFileNameWithoutExtension(_targetPath);
            var ext = Path.GetExtension(_targetPath);
            var token = Guid.NewGuid().ToString("N")[..8];
            _tempVideoPath = Path.Combine(dir, $"{stem}.tier2-{token}{ext}");
            _tempAudioPath = Path.Combine(dir, $"{stem}.tier2-{token}.f32le");
            videoTarget = _tempVideoPath;
            _audioStream = new FileStream(_tempAudioPath, FileMode.Create, FileAccess.Write);
        }

        _videoProcess = StartFfmpeg(exePath, BuildVideoPassArguments(fps, videoTarget), redirectStdin: true, out _videoErrors);
        _videoStdin = _videoProcess.StandardInput.BaseStream;
    }

    public void AddVideoFrame(ReadOnlySpan<byte> rgbaPixels, int rowStride)
    {
        if (_finished)
            throw new InvalidOperationException("Encoder already finished");

        try
        {
            for (var y = 0; y < _encoderHeight; y++)
                _videoStdin.Write(rgbaPixels.Slice(y * rowStride, _rowBytes));
        }
        catch (IOException e)
        {
            // A broken pipe means ffmpeg exited early — surface its stderr rather than the opaque IO error.
            throw new InvalidOperationException("External FFmpeg stopped reading frames: " + TailOf(_videoErrors), e);
        }
    }

    public void AddAudioSamples(ReadOnlySpan<byte> interleavedFloatPcm)
    {
        if (_finished)
            throw new InvalidOperationException("Encoder already finished");

        _audioStream?.Write(interleavedFloatPcm);
    }

    public void Finish()
    {
        if (_finished)
            return;

        _finished = true;

        _videoStdin.Flush();
        _videoStdin.Close(); // EOF → ffmpeg flushes and exits
        WaitOrThrow(_videoProcess, _videoErrors, "video encode");

        if (_hasAudio)
        {
            _audioStream!.Flush();
            _audioStream.Dispose();
            _audioStream = null;
            RunMuxPass();
        }

        CleanupTempFiles();
    }

    public void Dispose()
    {
        try
        {
            if (!_finished)
            {
                try { _videoStdin.Close(); } catch { /* ignore */ }
                KillIfRunning(_videoProcess);
            }
        }
        finally
        {
            _videoProcess.Dispose();
            _audioStream?.Dispose();
            CleanupTempFiles();
        }
    }

    private string BuildVideoPassArguments(int fps, string videoTarget)
    {
        var sb = new StringBuilder();
        sb.Append("-y -hide_banner ");
        sb.Append($"-f rawvideo -pixel_format rgba -video_size {_encoderWidth}x{_encoderHeight} -framerate {fps} -i pipe:0 ");

        switch (_codec)
        {
            case VideoExportCodec.Hap:
                sb.Append("-c:v hap -format hap");
                break;
            case VideoExportCodec.HapAlpha:
                sb.Append("-c:v hap -format hap_alpha");
                break;
            case VideoExportCodec.HapQ:
                sb.Append("-c:v hap -format hap_q");
                break;
            case VideoExportCodec.H264:
            default:
                sb.Append($"-c:v libx264 -preset medium -pix_fmt yuv420p -b:v {Math.Max(1, _bitRate)}");
                break;
        }

        sb.Append($" \"{videoTarget}\"");
        return sb.ToString();
    }

    private void RunMuxPass()
    {
        var args = $"-y -hide_banner -i \"{_tempVideoPath}\" "
                   + $"-f f32le -ar {_audioSampleRate} -ac {_audioChannels} -i \"{_tempAudioPath}\" "
                   + $"-c:v copy -c:a aac -b:a 192k -shortest \"{_targetPath}\"";

        using var mux = StartFfmpeg(_exePath, args, redirectStdin: false, out var muxErrors);
        WaitOrThrow(mux, muxErrors, "audio mux");
    }

    private static Process StartFfmpeg(string exePath, string arguments, bool redirectStdin, out StringBuilder errors)
    {
        var startInfo = new ProcessStartInfo
                            {
                                FileName = exePath,
                                Arguments = arguments,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardInput = redirectStdin,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                            };

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null");

        // Drain stdout (discarded) and collect stderr so a failure can report ffmpeg's reason.
        var collected = new StringBuilder();
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) collected.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        errors = collected;
        return process;
    }

    private static void WaitOrThrow(Process process, StringBuilder errors, string what)
    {
        if (!process.WaitForExit(WaitForExitMs))
        {
            KillIfRunning(process);
            throw new InvalidOperationException($"External FFmpeg timed out during {what}.");
        }

        process.WaitForExit(); // ensure async stderr is flushed
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"External FFmpeg failed during {what} (exit {process.ExitCode}): {TailOf(errors)}");
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch { /* best effort */ }
    }

    private void CleanupTempFiles()
    {
        TryDelete(_tempVideoPath);
        TryDelete(_tempAudioPath);
        _tempVideoPath = null;
        _tempAudioPath = null;
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e)
        {
            Log.Debug($"Could not delete tier-2 temp file '{path}': {e.Message}");
        }
    }

    private static string TailOf(StringBuilder errors)
    {
        var text = errors.ToString().Trim();
        const int max = 400;
        return text.Length <= max ? text : "…" + text[^max..];
    }

    private static bool IsHap(VideoExportCodec codec)
        => codec is VideoExportCodec.Hap or VideoExportCodec.HapAlpha or VideoExportCodec.HapQ;

    private const int WaitForExitMs = 120_000;

    private readonly string _exePath;
    private readonly string _targetPath;
    private readonly VideoExportCodec _codec;
    private readonly long _bitRate;
    private readonly int _encoderWidth;
    private readonly int _encoderHeight;
    private readonly int _rowBytes;
    private readonly bool _hasAudio;
    private readonly int _audioChannels;
    private readonly int _audioSampleRate;

    private readonly Process _videoProcess;
    private readonly Stream _videoStdin;
    private readonly StringBuilder _videoErrors;
    private FileStream? _audioStream;
    private string? _tempVideoPath;
    private string? _tempAudioPath;
    private bool _finished;
}
