using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using T3.Core.Video;
using Xunit;

namespace T3.VideoServices.Tests;

/// <summary>
/// End-to-end checks of the RTMP listen path that <c>VideoStreamInput</c>'s RtmpServer mode depends on, plus
/// the two failure guarantees a live source needs: a publisher that leaves must not block the reader, and a
/// source that stops sending must be abandoned instead of pinning its thread.
/// </summary>
/// <remarks>
/// The publisher is the ffmpeg CLI — the same push path OBS and phone streaming apps use. Point
/// <c>TIXL_FFMPEG_CLI</c> at a build with the rtmp protocol and an h264 encoder; otherwise the publisher
/// tests look for <c>ffmpeg</c> on PATH and return without exercising anything when it is absent. The
/// cancellation and stall tests need no publisher and always run.
/// </remarks>
public class RtmpListenTests
{
    private const int FrameWidth = 320;
    private const int FrameHeight = 240;
    private const string StreamPath = "live";

    [Fact]
    public void Listen_AcceptsPublisher_DecodesItsFrames()
    {
        if (!TryFindFfmpegCli(out var ffmpeg))
            return;

        var url = $"rtmp://{IPAddress.Loopback}:{FindFreePort()}/{StreamPath}";
        using var publisher = new FfmpegPublisher(ffmpeg, url, seconds: 4);

        var session = ListenUntilPublisherConnects(url);
        using (session)
        {
            Assert.NotNull(session);
            Assert.Equal(FrameWidth, session!.Width);
            Assert.Equal(FrameHeight, session.Height);

            var frames = 0;
            var read = Stopwatch.StartNew();
            while (frames < 10 && read.Elapsed < TimeSpan.FromSeconds(20))
            {
                if (!session.TryReadNextFrame(out _))
                    break;
                frames++;
            }

            Assert.True(frames >= 10, $"Expected frames from the RTMP publisher, got {frames}.");
        }
    }

    /// <summary>
    /// A publisher that went away must not leave the reader blocked. Ending on its own is fine whether FFmpeg
    /// reports the stream's end, the I/O watchdog abandons a stalled read, or — the common RTMP case — the
    /// peer close surfaces as a read error, which the operator catches so it can re-listen. What must never
    /// happen is an indefinite block.
    /// </summary>
    [Fact]
    public void Read_AfterPublisherLeaves_TerminatesWithoutBlocking()
    {
        if (!TryFindFfmpegCli(out var ffmpeg))
            return;

        var url = $"rtmp://{IPAddress.Loopback}:{FindFreePort()}/{StreamPath}";
        using var publisher = new FfmpegPublisher(ffmpeg, url, seconds: 2);

        var ioTimeout = TimeSpan.FromSeconds(2);
        var session = ListenUntilPublisherConnects(url, ioTimeout);
        using (session)
        {
            Assert.NotNull(session);

            var read = Stopwatch.StartNew();
            while (read.Elapsed < TimeSpan.FromSeconds(20))
            {
                try
                {
                    if (!session!.TryReadNextFrame(out _))
                        break;
                }
                catch (Exception)
                {
                    break; // stream end, stalled read or peer close — all end the loop, none hang it
                }
            }

            read.Stop();
            Assert.True(read.Elapsed < ioTimeout + TimeSpan.FromSeconds(8),
                        $"Reading past the end of the publisher took {read.Elapsed.TotalSeconds:0.0} s.");
        }
    }

    /// <summary>A token cancelled before the open is answered without any network I/O at all.</summary>
    [Fact]
    public void Open_WithCancelledToken_ReturnsImmediately()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var elapsed = Stopwatch.StartNew();
        var session = VideoDecoderSession.TryOpen($"rtmp://{IPAddress.Loopback}:{FindFreePort()}/{StreamPath}",
                                                  VideoPlaybackOptimization.FastSeeking, out var error,
                                                  cancellationToken: cancellation.Token);
        elapsed.Stop();
        session?.Dispose();

        Assert.Null(session);
        Assert.Equal("Cancelled.", error);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
    }

    /// <summary>Cancelling a live session stops its reads at the next frame boundary.</summary>
    [Fact]
    public void Read_AfterCancellation_ReturnsFalseImmediately()
    {
        if (!TryFindFfmpegCli(out var ffmpeg))
            return;

        var url = $"rtmp://{IPAddress.Loopback}:{FindFreePort()}/{StreamPath}";
        using var publisher = new FfmpegPublisher(ffmpeg, url, seconds: 5);
        using var cancellation = new CancellationTokenSource();

        var session = ListenUntilPublisherConnects(url, cancellation: cancellation.Token);
        using (session)
        {
            Assert.NotNull(session);
            Assert.True(session!.TryReadNextFrame(out _), "expected a first frame before cancelling");

            cancellation.Cancel();
            var elapsed = Stopwatch.StartNew();
            var stillReading = session.TryReadNextFrame(out _);
            elapsed.Stop();

            Assert.False(stillReading);
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1),
                        $"A cancelled session kept reading for {elapsed.Elapsed.TotalSeconds:0.0} s.");
        }
    }

    /// <summary>
    /// A source that accepts the connection and then sends nothing must be abandoned within the I/O timeout:
    /// this is the <c>rw_timeout</c> backstop the operator relies on for protocols without a socket timeout.
    /// </summary>
    [Fact]
    public void StalledSource_IsAbandonedWithinIoTimeout()
    {
        using var silentServer = new TcpListener(IPAddress.Loopback, 0);
        silentServer.Start();
        var port = ((IPEndPoint)silentServer.LocalEndpoint).Port;

        var accepted = new Task(() =>
                                    {
                                        try
                                        {
                                            using var client = silentServer.AcceptTcpClient();
                                            Thread.Sleep(TimeSpan.FromSeconds(15)); // accepted, but never sends
                                        }
                                        catch
                                        {
                                            // server torn down at the end of the test
                                        }
                                    });
        accepted.Start();

        var ioTimeout = TimeSpan.FromSeconds(2);
        var elapsed = Stopwatch.StartNew();
        var session = VideoDecoderSession.TryOpen($"http://{IPAddress.Loopback}:{port}/stream",
                                                  VideoPlaybackOptimization.FastSeeking, out _,
                                                  ioTimeout: ioTimeout);
        elapsed.Stop();
        session?.Dispose();

        Assert.True(elapsed.Elapsed < ioTimeout + TimeSpan.FromSeconds(8),
                    $"A silent source blocked the open for {elapsed.Elapsed.TotalSeconds:0.0} s.");
    }

    /// <summary>
    /// Repeatedly opens the listen URL, as the operator does, until a publisher lands inside one of the
    /// short accept windows. Returns null when none connected in time.
    /// </summary>
    private static VideoDecoderSession? ListenUntilPublisherConnects(string url, TimeSpan? ioTimeout = null,
                                                                    CancellationToken cancellation = default)
    {
        var listenOptions = new Dictionary<string, string>
                                {
                                    ["rtmp_listen"] = "1",
                                    ["timeout"] = "1",   // one-second accept windows, like the operator
                                    ["rtmp_live"] = "live",
                                };

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20) && !cancellation.IsCancellationRequested)
        {
            var session = VideoDecoderSession.TryOpen(url, VideoPlaybackOptimization.FastSeeking, out _,
                                                      listenOptions,
                                                      cancellationToken: cancellation,
                                                      ioTimeout: ioTimeout ?? TimeSpan.FromSeconds(10));
            if (session != null)
                return session;
        }

        return null;
    }

    private static int FindFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static bool TryFindFfmpegCli(out string path)
    {
        var configured = Environment.GetEnvironmentVariable("TIXL_FFMPEG_CLI");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
        {
            path = configured;
            return true;
        }

        var executable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var onPath = Environment.GetEnvironmentVariable("PATH")?
                              .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                              .Select(directory => Path.Combine(directory, executable))
                              .FirstOrDefault(File.Exists);

        path = onPath ?? string.Empty;
        if (onPath == null)
            Console.WriteLine("RtmpListenTests: no ffmpeg CLI found (set TIXL_FFMPEG_CLI) — publisher tests not exercised.");

        return onPath != null;
    }

    /// <summary>
    /// Pushes a synthetic clip to <paramref name="url"/> with the ffmpeg CLI, retrying the connect so the
    /// publisher lands inside one of the listener's one-second accept windows.
    /// </summary>
    private sealed class FfmpegPublisher : IDisposable
    {
        private readonly Thread _thread;
        private Process? _process;
        private volatile bool _stopping;

        public FfmpegPublisher(string ffmpegPath, string url, int seconds)
        {
            _thread = new Thread(() => Publish(ffmpegPath, url, seconds))
                          {
                              IsBackground = true,
                              Name = "RTMP test publisher",
                          };
            _thread.Start();
        }

        private void Publish(string ffmpegPath, string url, int seconds)
        {
            var arguments = $"-hide_banner -loglevel error -re -f lavfi -i testsrc=size={FrameWidth}x{FrameHeight}:rate=25 "
                            + $"-t {seconds} -c:v libx264 -preset ultrafast -pix_fmt yuv420p -f flv {url}";

            for (var attempt = 0; attempt < 60 && !_stopping; attempt++)
            {
                using var process = Process.Start(new ProcessStartInfo(ffmpegPath, arguments)
                                                      {
                                                          UseShellExecute = false,
                                                      });
                if (process == null)
                    return;

                _process = process;
                process.WaitForExit();
                if (process.ExitCode == 0 || _stopping)
                    return;

                Thread.Sleep(200); // the listener was between accept windows — try again
            }
        }

        public void Dispose()
        {
            _stopping = true;
            try
            {
                _process?.Kill(entireProcessTree: true);
            }
            catch
            {
                // already gone
            }

            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
