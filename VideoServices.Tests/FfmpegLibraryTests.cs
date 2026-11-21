using Sdcb.FFmpeg.Utils;
using T3.Core.Logging;
using Xunit;
using SdcbLogLevel = Sdcb.FFmpeg.Raw.LogLevel;

namespace T3.VideoServices.Tests;

public class FfmpegLibraryTests
{
    [Fact]
    public void Initialize_RoutesFfmpegLoggingThroughTheLogger()
    {
        Assert.True(FfmpegLibrary.EnsureInitialized());

        // ConfigureLogging installs a LogWriter so libav* messages flow through TiXL's Log (prefixed "FFmpeg:")
        // instead of FFmpeg's raw-stderr default. Emitting via FFmpegLogger exercises that same callback path.
        var sink = new CapturingLogWriter();
        Log.AddWriter(sink);
        try
        {
            FFmpegLogger.Log(SdcbLogLevel.Error, "tixl-routing-probe");
        }
        finally
        {
            Log.RemoveWriter(sink);
        }

        Assert.Contains(sink.Messages, m => m.Contains("tixl-routing-probe"));
    }

    [Fact]
    public void Initialize_DropsSwscalePerFramePixelFormatNotice()
    {
        Assert.True(FfmpegLibrary.EnsureInitialized());

        var sink = new CapturingLogWriter();
        Log.AddWriter(sink);
        try
        {
            // The exact text FFmpeg 7 emits for a full-range (MJPEG / IP-camera) frame — once per frame.
            FFmpegLogger.Log(SdcbLogLevel.Warning,
                             "[swscaler @ 000001e889629780] deprecated pixel format used, make sure you did set range correctly");

            // Control: proves the writer is still routing, so the assertion below is about the filter and not
            // about a silently dead callback.
            FFmpegLogger.Log(SdcbLogLevel.Warning, "tixl-control-probe");
        }
        finally
        {
            Log.RemoveWriter(sink);
        }

        Assert.DoesNotContain(sink.Messages, m => m.Contains("deprecated pixel format"));
        Assert.Contains(sink.Messages, m => m.Contains("tixl-control-probe"));
    }

    private sealed class CapturingLogWriter : ILogWriter
    {
        public ILogEntry.EntryLevel Filter { get; set; } = ILogEntry.EntryLevel.All;
        public readonly List<string> Messages = new();
        public void ProcessEntry(ILogEntry entry) => Messages.Add(entry.Message);
        public void Dispose() { }
    }
}
