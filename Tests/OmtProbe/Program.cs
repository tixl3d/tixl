using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using libomtnet;

// Sends or receives an OMT test stream from the command line, with the libomtnet build and codec binaries TiXL ships,
// so a link between two machines can be checked without the editor on either side.
//
//   OmtProbe list                        sources discovered on the network
//   OmtProbe send [name]                 an animated 1280x720 test pattern at 30 fps
//   OmtProbe receive <source|omt://host:port>

NativeLibrary.SetDllImportResolver(typeof(OMTSend).Assembly, ResolveCodec);
OMTLogging.SetFilename(null);
OMTLogging.SetCallback(line => Console.WriteLine("  omt: " + line));

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
                          {
                              e.Cancel = true;
                              stop.Cancel();
                          };

Console.WriteLine($"OmtProbe on {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}, machine {OMTPlatform.GetInstance().GetMachineName()}");
return mode switch
           {
               "list"    => List(stop.Token),
               "send"    => Send(args.Length > 1 ? args[1] : "OmtProbe", stop.Token),
               "receive" when args.Length > 1 => Receive(args[1], stop.Token),
               _         => Usage(),
           };

static int Usage()
{
    Console.WriteLine("""
                      Usage:
                        OmtProbe list
                        OmtProbe send [name]
                        OmtProbe receive <"MACHINE (Name)" | omt://host:port>
                      """);
    return 1;
}

static int List(CancellationToken stop)
{
    var discovery = OMTDiscovery.GetInstance();
    var last = "";
    for (var second = 0; second < 15 && !stop.IsCancellationRequested; second++)
    {
        var current = string.Join(Environment.NewLine + "  ", discovery.GetAddresses());
        if (current != last)
        {
            Console.WriteLine(current.Length == 0 ? "No sources yet..." : "Sources:" + Environment.NewLine + "  " + current);
            last = current;
        }

        Thread.Sleep(1000);
    }

    return 0;
}

static unsafe int Send(string name, CancellationToken stop)
{
    const int width = 1280;
    const int height = 720;
    const int frameRate = 30;
    const int stride = width * 2;

    using var sender = new OMTSend(name, OMTQuality.Default);
    Console.WriteLine($"Sending '{sender.Address}' at {sender.URL} (and omt://<this machine's IP>:{sender.Port})");

    var buffer = (byte*)NativeMemory.AlignedAlloc(stride * height, 64);
    try
    {
        var clock = Stopwatch.StartNew();
        var lastConnections = -1;
        for (long frameIndex = 0; !stop.IsCancellationRequested; frameIndex++)
        {
            // UYVY bars that scroll one pixel per frame, so a frozen stream is obvious on the receiving side.
            for (var y = 0; y < height; y++)
            {
                var row = buffer + y * stride;
                for (var pair = 0; pair < width / 2; pair++)
                {
                    var luma = (byte)(16 + (((pair * 2 + frameIndex) / 160) % 8) * 31);
                    row[pair * 4 + 0] = 128;
                    row[pair * 4 + 1] = luma;
                    row[pair * 4 + 2] = 128;
                    row[pair * 4 + 3] = luma;
                }
            }

            sender.Send(new OMTMediaFrame
                            {
                                Type = OMTFrameType.Video,
                                Codec = (int)OMTCodec.UYVY,
                                Width = width,
                                Height = height,
                                Stride = stride,
                                FrameRateN = frameRate,
                                FrameRateD = 1,
                                AspectRatio = (float)width / height,
                                ColorSpace = OMTColorSpace.BT709,
                                Timestamp = -1, // OMT paces this loop to the frame rate
                                Data = (IntPtr)buffer,
                                DataLength = stride * height,
                            });

            if (sender.Connections != lastConnections)
            {
                lastConnections = sender.Connections;
                Console.WriteLine($"[{clock.Elapsed:mm\\:ss}] {lastConnections} receiver(s) connected");
            }
        }
    }
    finally
    {
        NativeMemory.AlignedFree(buffer);
    }

    return 0;
}

static unsafe int Receive(string source, CancellationToken stop)
{
    using var receiver = new OMTReceive(source, OMTFrameType.Video, OMTPreferredVideoFormat.BGRA, OMTReceiveFlags.None);
    Console.WriteLine($"Receiving '{source}'...");

    var clock = Stopwatch.StartNew();
    var frames = 0;
    var totalFrames = 0L;
    var lastReport = 0L;
    var frame = new OMTMediaFrame();
    var exitCode = 2;
    while (!stop.IsCancellationRequested)
    {
        if (receiver.Receive(OMTFrameType.Video, 500, ref frame))
        {
            frames++;
            totalFrames++;
            exitCode = 0;
        }

        if (clock.ElapsedMilliseconds - lastReport < 1000)
            continue;

        lastReport = clock.ElapsedMilliseconds;
        if (frames == 0)
        {
            Console.WriteLine($"[{clock.Elapsed:mm\\:ss}] no frames, connected={receiver.IsConnected()}");
            continue;
        }

        var center = (byte*)frame.Data + (frame.Height / 2) * frame.Stride + (frame.Width / 2) * 4;
        var stats = receiver.GetVideoStatistics();
        Console.WriteLine($"[{clock.Elapsed:mm\\:ss}] {frames} fps  {frame.Width}x{frame.Height}  center BGRA {center[0]},{center[1]},{center[2]},{center[3]}  "
                          + $"{stats.BytesReceivedSinceLast * 8 / 1_000_000.0:F1} Mbit/s  dropped {stats.FramesDropped}  total {totalFrames}");
        frames = 0;
    }

    return exitCode;
}

static IntPtr ResolveCodec(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
{
    if (libraryName != "libvmx")
        return IntPtr.Zero;

    var fileName = OperatingSystem.IsWindows() ? "libvmx.dll" : OperatingSystem.IsMacOS() ? "libvmx.dylib" : "libvmx.so";
    var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    string[] runtimeIds = OperatingSystem.IsWindows() ? [$"win-{architecture}"]
                          : OperatingSystem.IsMacOS() ? ["osx", $"osx-{architecture}"]
                          : [$"linux-{architecture}"];
    foreach (var runtimeId in runtimeIds)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "runtimes", runtimeId, "native", fileName);
        if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
            return handle;
    }

    return IntPtr.Zero;
}
