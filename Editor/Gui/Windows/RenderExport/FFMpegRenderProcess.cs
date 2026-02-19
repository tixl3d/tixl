#nullable enable
using System;
using System.IO;
using System.Linq;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport.FFMpeg;
using T3.Editor.UiModel.ProjectHandling;
using FFMpegCore;
using FFMpegCore.Helpers;
using FFMpegCore.Extensions.Downloader;
using System.Threading.Tasks;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static class FFMpegRenderProcess
{
    public static string LastHelpString { get; private set; } = string.Empty;
    public static string LastOutputPath { get; private set; } = string.Empty;
    public static int FrameIndex => _frameIndex;
    public static int FrameCount => _frameCount;
    public static double Progress => _frameCount <= 1 ? 0.0 : (_frameIndex / (double)(_frameCount - 1));

    public static Type? MainOutputType { get; private set; }
    public static Int2 MainOutputOriginalSize;
    public static Int2 MainOutputRenderedSize;
    public static Texture2D? MainOutputTexture;

    public static States State;

    public static bool IsExporting { get; private set; }
    public static bool IsToollRenderingSomething { get; private set; }

    public static double ExportStartedTimeLocal;

    public enum States
    {
        NoOutputWindow,
        NoValidOutputType,
        NoValidOutputTexture,
        WaitingForExport,
        Exporting,
        Downloading // New state
    }

    public static float DownloadProgress { get; private set; }

    public static void Update()
    {
        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            State = States.NoOutputWindow;
            return;
        }

        MainOutputTexture = outputWindow.GetCurrentTexture();
        if (MainOutputTexture == null)
        {
            State = States.NoValidOutputTexture;
            return;
        }

        MainOutputType = outputWindow.ShownInstance?.Outputs.FirstOrDefault()?.ValueType;
        if (MainOutputType != typeof(Texture2D))
        {
            State = States.NoValidOutputType;
            return;
        }

        // HandleRenderShortCuts(); // Disable shortcuts for FFMpeg window for now to avoid conflicts

        if (!IsExporting)
        {
            var desc = MainOutputTexture.Description;
            MainOutputOriginalSize.Width = desc.Width;
            MainOutputOriginalSize.Height = desc.Height;

            var width = (int)(desc.Width * FFMpegRenderSettings.Current.ResolutionFactor);
            var height = (int)(desc.Height * FFMpegRenderSettings.Current.ResolutionFactor);

            // Ensure even dimensions for video codecs
            width = (width / 2 * 2).Clamp(2, 16384);
            height = (height / 2 * 2).Clamp(2, 16384);

            MainOutputRenderedSize = new Int2(width, height);

            State = States.WaitingForExport;
            return;
        }

        if (IsExporting && _videoWriter != null && _videoWriter.IsBufferFull)
        {
            return;
        }

        State = States.Exporting;

        // Process frame
        double localFxTime = _frameIndex / _renderSettings.Fps;
        var audioFrameFloat = AudioRendering.GetFullMixDownBuffer(1.0 / _renderSettings.Fps);
        // Safety: ensure audioFrameFloat is valid and sized
        if (audioFrameFloat == null || audioFrameFloat.Length == 0)
        {
            Log.Error($"RenderProcess: AudioRendering.GetFullMixDownBuffer returned null or empty at frame {_frameIndex}", typeof(RenderProcess));
            int sampleRate = RenderAudioInfo.SoundtrackSampleRate();
            int channels = RenderAudioInfo.SoundtrackChannels();
            int floatCount = (int)Math.Max(Math.Round((1.0 / _renderSettings.Fps) * sampleRate), 0.0) * channels;
            audioFrameFloat = new float[floatCount]; // silence
        }
        var audioFrame = new byte[audioFrameFloat.Length * sizeof(float)];
        Buffer.BlockCopy(audioFrameFloat, 0, audioFrame, 0, audioFrame.Length);
        AudioRendering.EvaluateAllAudioMeteringOutputs(localFxTime, audioFrameFloat);

        bool success = true;
        try
        {
            _videoWriter?.ProcessFrames(MainOutputTexture, ref audioFrame);
            _frameIndex++;
            // We need to advance time!
            RenderTiming.SetPlaybackTimeForFrame(ref _renderSettings, _frameIndex, _frameCount, ref _runtime);
        }
        catch (Exception e)
        {
            LastHelpString = e.Message;
            success = false;
        }

        // Update stats
        var currentFrame = _frameIndex;
        var completed = currentFrame >= _frameCount || !success;

        if (!completed)
            return;

        Finished();
    }

    private static void Finished()
    {
        var duration = Playback.RunTimeInSecs - _exportStartedTime;
        LastHelpString = $"FFMpeg Render finished in {StringUtils.HumanReadableDurationFromSeconds(duration)}";
        Log.Debug(LastHelpString);

        if (_renderSettings.Bitrate > 0 && true) // Check auto-increment?
        {
            // Maybe implement increment if settings have it
        }

        Cleanup();
        IsToollRenderingSomething = false;
    }

    public static void TryStart(FFMpegRenderSettings renderSettings)
    {
        if (IsExporting)
        {
            Log.Warning("Export is already in progress");
            return;
        }

        var targetFilePath = RenderPaths.GetTargetFilePath(renderSettings.RenderMode);

        // Ensure directory exists for Image Sequence (in case it wasn't created by overwrite check or new folder)
        if (renderSettings.RenderMode == RenderSettings.RenderModes.ImageSequence)
        {
            // Resolve paths
            var mainFolder = RenderPaths.ResolveProjectRelativePath(UserSettings.Config.RenderSequenceFilePath);
            var subFolder = UserSettings.Config.RenderSequenceFileName;
            var prefix = UserSettings.Config.RenderSequencePrefix;

            // Handle Auto-Increment
            if (renderSettings.CreateSubFolder && renderSettings.AutoIncrementSubFolder)
            {
                // Find next free folder
                var foundPath = RenderPaths.GetNextVersionForFolder(mainFolder, subFolder);

                // Update User Settings to reflect the new state for next time
                var newSubFolderName = Path.GetFileName(foundPath);
                UserSettings.Config.RenderSequenceFileName = newSubFolderName;
                UserSettings.Save();

                subFolder = newSubFolderName;
            }

            // Construct final directory
            var exportDir = renderSettings.CreateSubFolder
                            ? Path.Combine(mainFolder, subFolder)
                            : mainFolder;

            if (!Directory.Exists(exportDir))
            {
                try { Directory.CreateDirectory(exportDir); }
                catch (Exception e)
                {
                    Log.Error($"Could not create directory {exportDir}: {e.Message}");
                    return;
                }
            }

            var extension = renderSettings.FileFormat.ToString().ToLower();
            targetFilePath = Path.Combine(exportDir, $"{prefix}_%04d.{extension}");
        }
        else
        {
            var correctExtension = FFMpegRenderSettings.GetFileExtension(renderSettings.Codec);
            targetFilePath = Path.ChangeExtension(targetFilePath, correctExtension);

            if (renderSettings.AutoIncrementVideo)
            {
                if (!RenderPaths.IsFilenameIncrementable(targetFilePath))
                {
                    targetFilePath = RenderPaths.GetNextIncrementedPath(targetFilePath);
                }

                while (File.Exists(targetFilePath))
                {
                    targetFilePath = RenderPaths.GetNextIncrementedPath(targetFilePath);
                }

                UserSettings.Config.RenderVideoFilePath = targetFilePath;
                UserSettings.Save();
            }
        }

        LastOutputPath = targetFilePath;

        var tempSettings = new RenderSettings()
        {
            Reference = (RenderSettings.TimeReference)renderSettings.Reference,
            StartInBars = renderSettings.StartInBars,
            EndInBars = renderSettings.EndInBars,
            Fps = renderSettings.Fps,
            FrameCount = renderSettings.FrameCount,
            TimeRange = (RenderSettings.TimeRanges)renderSettings.TimeRange
        };
        renderSettings.FrameCount = RenderTiming.ComputeFrameCount(tempSettings);

        IsToollRenderingSomething = true;
        ExportStartedTimeLocal = Core.Animation.Playback.RunTimeInSecs;

        _renderSettings = tempSettings;

        _frameIndex = 0;
        _frameCount = Math.Max(_renderSettings.FrameCount, 0);
        _exportStartedTime = Playback.RunTimeInSecs;

        var channels = 2;
        var sampleRate = 48000;

        // Only get audio info if exporting audio and NOT image sequence
        var exportAudio = renderSettings.ExportAudio && renderSettings.RenderMode != RenderSettings.RenderModes.ImageSequence;
        if (exportAudio)
        {
            channels = RenderAudioInfo.SoundtrackChannels();
            sampleRate = RenderAudioInfo.SoundtrackSampleRate();
        }

        _videoWriter = new FFMpegVideoWriter(targetFilePath, MainOutputOriginalSize, MainOutputRenderedSize, exportAudio, sampleRate, channels)
        {
            Bitrate = renderSettings.Bitrate,
            Framerate = renderSettings.Fps,
            Codec = renderSettings.Codec,
            Crf = renderSettings.CrfQuality,
            Preset = renderSettings.Preset,
            WebpQuality = renderSettings.WebpQuality,
            WebpCompressionLevel = renderSettings.WebpCompressionLevel,
            IsImageSequence = renderSettings.RenderMode == RenderSettings.RenderModes.ImageSequence,
            ImageFormat = renderSettings.FileFormat
        };

        _videoWriter.Start();

        RenderTiming.SetPlaybackTimeForFrame(ref _renderSettings, _frameIndex, _frameCount, ref _runtime);
        IsExporting = true;
        LastHelpString = "Rendering with FFMpeg...";
    }

    public static void Cancel(string? reason = null)
    {
        LastHelpString = reason ?? "Cancelled";
        Cleanup();
        IsToollRenderingSomething = false;
    }

    private static void Cleanup()
    {
        IsExporting = false;
        _videoWriter?.Dispose();
        _videoWriter = null;
        RenderTiming.ReleasePlaybackTime(ref _renderSettings, ref _runtime);
    }

    // FFMpeg install logic
    public static bool IsFFMpegAvailable()
    {
        try
        {
            var folder = GetFFBinariesFolder();
            return File.Exists(Path.Combine(folder, "ffmpeg.exe"));
        }
        catch
        {
            return false;
        }
    }

    public static async void InstallFFMpeg()
    {
        var folder = GetFFBinariesFolder();
        State = States.Downloading;
        DownloadProgress = 0f;

        try
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            Log.Info($"Downloading FFMpeg to {folder}...");

            await FFMpegDownloader.DownloadBinaries(FFMpegCore.Extensions.Downloader.Enums.FFMpegVersions.LatestAvailable,
                                                    options: new FFOptions { BinaryFolder = folder });

            DownloadProgress = 1.0f;

            Log.Info("FFMpeg download complete.");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to download FFMpeg: {e.Message}");
        }
        finally
        {
            State = States.NoOutputWindow; // Reset state or let Update() fix it
        }

        // Configure options
        GlobalFFOptions.Configure(new FFOptions { BinaryFolder = folder });
    }

    private static string GetFFBinariesFolder()
    {
        // Save in user profile or app data
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tixl", "ffmpeg");
    }

    // Initialize
    static FFMpegRenderProcess()
    {
        // Check availability on init
        var folder = GetFFBinariesFolder();
        if (File.Exists(Path.Combine(folder, "ffmpeg.exe")))
        {
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = folder });
        }
    }

    // State
    private static FFMpegVideoWriter? _videoWriter;
    private static double _exportStartedTime;
    private static int _frameIndex;
    private static int _frameCount;

    private static RenderSettings _renderSettings = null!;
    private static RenderTiming.Runtime _runtime;
}
