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

    private sealed class CapturingLogWriter : ILogWriter
    {
        public ILogEntry.EntryLevel Filter { get; set; } = ILogEntry.EntryLevel.All;
        public readonly List<string> Messages = new();
        public void ProcessEntry(ILogEntry entry) => Messages.Add(entry.Message);
        public void Dispose() { }
    }
}
