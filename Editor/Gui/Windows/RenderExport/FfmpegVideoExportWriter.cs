#nullable enable
using System;
using SharpDX.Direct3D11;
using T3.Core.Model;
using T3.Core.Resource;
using T3.Core.Video;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Editor.Gui.Windows.RenderExport;

/// <summary>The render-export video writer contract, shared by the Media Foundation and FFmpeg backends.</summary>
internal interface IRenderVideoWriter : IDisposable
{
    bool ProcessFrames(Texture2D gpuTexture, ref byte[] audioFrame, int channels, int sampleRate);
}

/// <summary>
/// Render-export writer backed by the FFmpeg encoder. The encoder lives in the operator-loaded video assembly
/// (the editor cannot depend on it directly) and is reached through the Core <see cref="VideoExport"/> facade.
/// Each output frame is converted and read back to RGBA8 on the immediate context (synchronously — render
/// export is offline, so a per-frame GPU stall is acceptable and avoids the async-readback bookkeeping), then
/// handed to the encoder together with the audio mixdown.
/// </summary>
internal sealed class FfmpegVideoExportWriter : IRenderVideoWriter
{
    /// <summary>Returns null (with a reason) when the FFmpeg encoder isn't available — the caller falls back.</summary>
    public static FfmpegVideoExportWriter? TryCreate(RenderProcess.ExportSession session, out string? error)
    {
        error = null;
        var factory = VideoExport.Factory;
        if (factory == null)
        {
            // The factory registers via the Video package's [ModuleInitializer], which only fires once that
            // package's code has run (e.g. a video op was used). For an export from a project with no video
            // operator, force each loaded package's module initializer so the encoder registers regardless.
            foreach (var package in SymbolPackage.AllPackages)
                package.AssemblyInformation.RunModuleInitializers();
            factory = VideoExport.Factory;
        }

        if (factory == null)
        {
            error = "FFmpeg video encoder is unavailable (video package not loaded).";
            return null;
        }

        var settings = new VideoExportSettings
                           {
                               FilePath = session.TargetFilePath,
                               Width = session.RenderToFileResolution.Width,
                               Height = session.RenderToFileResolution.Height,
                               FrameRate = session.Settings.FrameRate,
                               BitRate = session.Settings.Bitrate,
                               Codec = session.Settings.VideoCodec,
                               ExportAudio = session.Settings.ExportAudio,
                               AudioChannels = RenderAudioInfo.SoundtrackChannels(),
                               AudioSampleRate = RenderAudioInfo.SoundtrackSampleRate(),
                           };

        var writer = factory.TryCreateWriter(settings, out error);
        return writer == null ? null : new FfmpegVideoExportWriter(writer);
    }

    private FfmpegVideoExportWriter(IVideoFileWriter writer) => _writer = writer;

    public unsafe bool ProcessFrames(Texture2D gpuTexture, ref byte[] audioFrame, int channels, int sampleRate)
    {
        // Synchronous convert (any source format) → RGBA8 staging texture, then map and feed the bytes.
        var cpuTexture = _readAccess.ConvertToCpuReadableBgra(gpuTexture);
        var context = ResourceManager.Device.ImmediateContext;
        var dataBox = context.MapSubresource(cpuTexture, 0, 0, MapMode.Read, MapFlags.None, out _);
        try
        {
            var pixels = new ReadOnlySpan<byte>((void*)dataBox.DataPointer, cpuTexture.Description.Height * dataBox.RowPitch);
            _writer.AddVideoFrame(pixels, dataBox.RowPitch);
            if (audioFrame is { Length: > 0 })
                _writer.AddAudioSamples(audioFrame);
        }
        finally
        {
            context.UnmapSubresource(cpuTexture, 0);
        }

        return true;
    }

    public void Dispose()
    {
        try
        {
            _writer.Finish();
        }
        catch (Exception e)
        {
            Log.Warning("FFmpeg export: finishing the output failed - " + e.Message);
        }

        _writer.Dispose();
        _readAccess.Dispose();
    }

    private readonly IVideoFileWriter _writer;

    // Immediate (synchronous) readback, converting straight to RGBA8 so the bytes match the encoder's input.
    private readonly TextureBgraReadAccess _readAccess = new(useImmediateReadback: true,
                                                             targetFormat: SharpDX.DXGI.Format.R8G8B8A8_UNorm);
}
