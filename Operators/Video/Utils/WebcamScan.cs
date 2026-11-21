#nullable enable
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using DirectShowLib;
using FormatType = DirectShowLib.FormatType;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Utils;
using T3.VideoServices;

namespace Lib.io.video;

/// <summary>
/// Shared webcam device scanning for the video capture operators: sysfs plus V4L2 frame-size enumeration on
/// Linux (the capture node of each V4L2 device — the one whose `index` is 0), DirectShow plus capability
/// enumeration on Windows, and FFmpeg's avfoundation <c>list_devices</c> output on macOS. The result is
/// cached per session; <see cref="Invalidate"/> lets a reconnect pick up hot-plugged cameras.
/// </summary>
public static class WebcamScan
{
    private static readonly object _capabilitiesLock = new();
    private static readonly Lock _scanLock = new();
    private static bool _scanned;
    private static List<WebcamWithIndex>? _devices;
    private static Dictionary<string, List<(int Width, int Height, double Fps)>>? _capabilities;

    /// <summary>The devices found by the last scan (empty when none were found or scanning hasn't run).</summary>
    public static IReadOnlyList<WebcamWithIndex> Devices
    {
        get
        {
            Scan();
            return _devices ?? [];
        }
    }

    /// <summary>Resolution/fps capabilities per device name — from the DirectShow stream-config on Windows
    /// and the V4L2 frame-size/frame-interval ioctls on Linux; empty on macOS (avfoundation exposes no
    /// capability query), where the dropdowns fall back to their defaults.</summary>
    public static IReadOnlyList<(int Width, int Height, double Fps)> GetCapabilities(string deviceName)
    {
        Scan();
        lock (_capabilitiesLock)
        {
            if (_capabilities != null && _capabilities.TryGetValue(deviceName, out var caps))
                return caps;
        }

        return [];
    }

    public static bool TryGetIndexForName(string? deviceName, out int index)
    {
        index = -1;
        foreach (var device in Devices)
        {
            if (device.Name != deviceName)
                continue;

            index = device.Index;
            return true;
        }

        return false;
    }

    public static void Invalidate()
    {
        lock (_scanLock)
        {
            _scanned = false;
        }
    }

    public static void Scan()
    {
        lock (_scanLock)
        {
            if (_scanned)
                return;

            _devices = [];
            lock (_capabilitiesLock)
            {
                _capabilities = new Dictionary<string, List<(int Width, int Height, double Fps)>>();
            }

            try
            {
                if (OperatingSystem.IsWindows())
                    ScanWindowsCameras(_devices);
                else if (OperatingSystem.IsLinux())
                    ScanLinuxCameras(_devices);
                else if (OperatingSystem.IsMacOS())
                    ScanMacCameras(_devices);
            }
            catch (Exception e)
            {
                Log.Debug("Failed to scan webcams: " + e.Message);
            }

            _scanned = true;
        }
    }

    private static void ScanLinuxCameras(List<WebcamWithIndex> webcams)
    {
        // V4L2 nodes and their friendly names come from sysfs. A physical camera often exposes several
        // nodes (capture, metadata, output); the capture node is the one whose `index` file is 0.
        const string sysfsRoot = "/sys/class/video4linux";
        if (!Directory.Exists(sysfsRoot))
            return;

        foreach (var node in Directory.GetDirectories(sysfsRoot).Order())
        {
            var nodeName = Path.GetFileName(node);
            if (!nodeName.StartsWith("video", StringComparison.Ordinal) ||
                !int.TryParse(nodeName["video".Length..], out var deviceIndex))
            {
                continue;
            }

            try
            {
                var indexFilePath = Path.Combine(node, "index");
                if (File.Exists(indexFilePath) &&
                    int.TryParse(File.ReadAllText(indexFilePath).Trim(), out var nodeIndex) &&
                    nodeIndex != 0)
                {
                    continue;
                }

                var namePath = Path.Combine(node, "name");
                var name = File.Exists(namePath) ? File.ReadAllText(namePath).Trim() : nodeName;
                webcams.Add(new WebcamWithIndex(name, deviceIndex));

                // The resolution/fps dropdowns read from here; without the ioctl enumeration they would
                // only offer their generic fallbacks.
                var capabilities = new List<(int Width, int Height, double Fps)>();
                try
                {
                    ScanLinuxCapabilities(deviceIndex, capabilities);
                }
                catch (Exception e)
                {
                    Log.Debug($"Failed to enumerate frame sizes of {nodeName}: {e.Message}");
                }

                lock (_capabilitiesLock)
                {
                    // Two identical UVC cameras report the same sysfs name; the dictionary is name-keyed,
                    // so the twin keeps the first one's list instead of throwing here. (The devices
                    // themselves stay distinct — identity is the node index, and the capture URL keys on
                    // the name only on Windows/macOS, matching how those demuxers address devices.)
                    _capabilities!.TryAdd(name, capabilities);
                }
            }
            catch (Exception e)
            {
                Log.Debug($"Failed to inspect V4L2 node {nodeName}: {e.Message}");
            }
        }
    }

    // VIDIOC_ENUM_FRAMESIZES / VIDIOC_ENUM_FRAMEINTERVALS — request codes via the kernel's _IOWR('V', nr,
    // struct) macro, like the control ioctls in CameraControls. Sizes verified against the UAPI headers
    // (struct v4l2_frmsizeenum = 44 bytes, struct v4l2_frmivalenum = 52) and both are part of the stable
    // V4L2 ABI, so the values hold across distros.
    private static readonly ulong VidIocEnumFrameSizes = Iowr(74, 44);
    private static readonly ulong VidIocEnumFrameIntervals = Iowr(75, 52);

    private const int O_RDWR = 2;
    private const uint V4L2PixFmtYuyv = 0x56595559;  // 'YUYV'
    private const uint V4L2PixFmtMjpeg = 0x47504A4D; // 'MJPG'
    private const uint V4L2FrmsizeTypeDiscrete = 1;
    private const uint V4L2FrmivalTypeDiscrete = 1;

    private static ulong Iowr(byte number, uint structSize)
        => (3UL << 30) | ((ulong)structSize << 16) | (0x56UL << 8) | number;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct V4L2FrameSizeEnum
    {
        public uint Index;
        public uint PixelFormat;
        public uint Type;      // V4L2_FRMSIZE_TYPE_*
        public uint Width;     // union: discrete.width (valid for Type == Discrete)
        public uint Height;    // union: discrete.height
        public uint Union0;    // union: stepwise — not modeled here
        public uint Union1;
        public uint Union2;
        public uint Union3;
        public uint Reserved0;
        public uint Reserved1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct V4L2FrameIntervalEnum
    {
        public uint Index;
        public uint PixelFormat;
        public uint Width;
        public uint Height;
        public uint Type;                  // V4L2_FRMIVAL_TYPE_*
        public uint IntervalNumerator;     // union: discrete.interval — interval = num/den seconds
        public uint IntervalDenominator;
        public uint Union0;                // union: stepwise — not modeled here
        public uint Union1;
        public uint Union2;
        public uint Union3;
        public uint Reserved0;
        public uint Reserved1;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref V4L2FrameSizeEnum arg);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref V4L2FrameIntervalEnum arg);

    private static void ScanLinuxCapabilities(int deviceIndex, List<(int Width, int Height, double Fps)> capabilities)
    {
        // Enumerates the discrete frame sizes and, per size, the frame intervals the driver reports. The
        // capture open later negotiates with the driver directly — this list only feeds the dropdowns.
        // Probed for the two formats the FFmpeg v4l2 demuxer commonly negotiates: YUYV (the UVC default)
        // and MJPEG (which usually carries the higher resolutions). The extra open handle is fine — V4L2
        // allows several fds per device (the controls in CameraControls work the same way).
        var fd = open($"/dev/video{deviceIndex}", O_RDWR);
        if (fd < 0)
            return;

        try
        {
            uint[] pixelFormats = [V4L2PixFmtYuyv, V4L2PixFmtMjpeg];
            foreach (var pixelFormat in pixelFormats)
            {
                for (uint sizeIndex = 0; sizeIndex < 256; sizeIndex++)
                {
                    var size = new V4L2FrameSizeEnum { Index = sizeIndex, PixelFormat = pixelFormat };
                    if (ioctl(fd, VidIocEnumFrameSizes, ref size) != 0)
                        break; // EINVAL — no (more) sizes for this format

                    if (size.Type != V4L2FrmsizeTypeDiscrete)
                        break; // stepwise/continuous (tuner-style devices) don't reduce to a dropdown list

                    for (uint intervalIndex = 0; intervalIndex < 256; intervalIndex++)
                    {
                        var interval = new V4L2FrameIntervalEnum
                                       {
                                           Index = intervalIndex,
                                           PixelFormat = pixelFormat,
                                           Width = size.Width,
                                           Height = size.Height,
                                       };
                        if (ioctl(fd, VidIocEnumFrameIntervals, ref interval) != 0)
                            break;

                        if (interval.Type != V4L2FrmivalTypeDiscrete)
                            break;

                        if (interval.IntervalNumerator > 0)
                            capabilities.Add(((int)size.Width, (int)size.Height,
                                              (double)interval.IntervalDenominator / interval.IntervalNumerator));
                    }
                }
            }
        }
        finally
        {
            close(fd);
        }
    }

    private static void ScanWindowsCameras(List<WebcamWithIndex> webcams)
    {
        var videoInputDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        for (var i = 0; i < videoInputDevices.Length; i++)
        {
            var device = videoInputDevices[i];
            webcams.Add(new WebcamWithIndex(device.Name, i));

            var capabilities = new List<(int Width, int Height, double Fps)>();
            try
            {
                if (device.Mon is not IMoniker moniker)
                {
                    Log.Debug($"Moniker is null for device {device.Name}");
                    continue;
                }

                var filterGuid = typeof(IBaseFilter).GUID;
                moniker.BindToObject(null!, null!, ref filterGuid, out var filter);
                if (filter is not IBaseFilter captureFilter)
                {
                    Log.Debug($"Failed to create capture filter for {device.Name}");
                    continue;
                }

                captureFilter.EnumPins(out var enumPins);
                var pins = new IPin[1];
                while (enumPins.Next(1, pins, IntPtr.Zero) == 0)
                {
                    pins[0].QueryPinInfo(out var pinInfo);
                    if (pinInfo.dir == PinDirection.Output)
                    {
                        if (pins[0] is IAMStreamConfig streamConfig)
                        {
                            streamConfig.GetNumberOfCapabilities(out int count, out int size);
                            if (size > 0)
                            {
                                var capsPtr = Marshal.AllocCoTaskMem(size);
                                try
                                {
                                    for (int j = 0; j < count; j++)
                                    {
                                        streamConfig.GetStreamCaps(j, out var mediaType, capsPtr);
                                        if (mediaType != null &&
                                            mediaType.formatType == FormatType.VideoInfo &&
                                            mediaType.formatPtr != IntPtr.Zero)
                                        {
                                            var vih = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.formatPtr)!;
                                            int w = vih.BmiHeader.Width;
                                            int h = vih.BmiHeader.Height;
                                            double fps = vih.AvgTimePerFrame > 0
                                                             ? 10_000_000.0 / vih.AvgTimePerFrame
                                                             : 30.0;
                                            capabilities.Add((w, h, fps));
                                        }

                                        if (mediaType != null)
                                            DsUtils.FreeAMMediaType(mediaType);
                                    }
                                }
                                finally
                                {
                                    Marshal.FreeCoTaskMem(capsPtr);
                                }
                            }
                        }
                    }

                    Marshal.ReleaseComObject(pins[0]);
                }

                Marshal.ReleaseComObject(enumPins);
                Marshal.ReleaseComObject(captureFilter);
            }
            catch (Exception e)
            {
                Log.Debug($"Failed to get capabilities for {device.Name}: {e.Message}");
            }

            lock (_capabilitiesLock)
            {
                // Two identical cameras report the same DirectShow name; a plain Add would throw and abort
                // the scan (leaving the remaining devices unlisted). Keep the first list, like the Linux scan.
                _capabilities!.TryAdd(device.Name, capabilities);
            }
        }
    }

    private static void ScanMacCameras(List<WebcamWithIndex> webcams)
    {
        // The avfoundation demuxer implements no avdevice device-list callback, so the clean
        // avdevice_list_input_sources path returns ENOSYS for it. The only enumeration it offers is its
        // list_devices option, which prints the devices through av_log at INFO level and then fails the
        // open — so the messages are captured while the open runs (see FfmpegLibrary.WithLogCapture for
        // the process-global caveats of that window).
        if (!FfmpegLibrary.EnsureInitialized())
            return;

        InputFormat? avfoundation;
        try
        {
            avfoundation = InputFormat.FindByShortName("avfoundation");
        }
        catch (Exception e)
        {
            Log.Debug("FFmpeg: could not resolve the avfoundation demuxer — macOS camera enumeration unavailable: " + e.Message);
            return;
        }

        if (avfoundation == null)
        {
            Log.Debug("FFmpeg: no avfoundation demuxer in this build — macOS camera enumeration unavailable.");
            return;
        }

        var output = new List<string>();
        FfmpegLibrary.WithLogCapture(output.Add, () =>
        {
            try
            {
                using var options = new MediaDictionary();
                options["list_devices"] = "true";
                using var context = FormatContext.OpenInputUrl("default", avfoundation, options);
            }
            catch
            {
                // Expected: list_devices prints the devices and read_header then fails with AVERROR(EIO).
            }
        });

        ParseAvFoundationDeviceList(output, webcams);
    }

    private static void ParseAvFoundationDeviceList(List<string> logLines, List<WebcamWithIndex> webcams)
    {
        // Printed by avfoundation's list_devices option: "AVFoundation video devices:", then "[i] name"
        // lines for video and muxed devices, then "Capture screen i" entries, then "AVFoundation audio
        // devices:". Only the camera part of the video section is usable here — capture cards with
        // audio (muxed) are included, screens are not. Device names double as the capture URL on macOS,
        // where avfoundation matches them by prefix against the device's localizedName.
        //
        // Every line carries FFmpeg's standard log prefix ("[AVFoundation indev @ 0x7f…] ") because
        // Sdcb's log callback renders through av_log_format_line2 with printPrefix=1 — strip it first.
        var inVideoSection = false;
        foreach (var line in logLines.SelectMany(l => l.Split('\n')))
        {
            var text = StripLogPrefix(line.Trim());
            if (text.StartsWith("AVFoundation video devices", StringComparison.Ordinal))
            {
                inVideoSection = true;
                continue;
            }

            if (text.StartsWith("AVFoundation audio devices", StringComparison.Ordinal))
            {
                inVideoSection = false;
                continue;
            }

            if (!inVideoSection)
                continue;

            // "[12] FaceTime HD Camera"
            if (!text.StartsWith("[", StringComparison.Ordinal))
                continue;

            var bracketEnd = text.IndexOf(']');
            if (bracketEnd < 1 || !int.TryParse(text[1..bracketEnd], out _))
                continue;

            var name = text[(bracketEnd + 1)..].Trim();
            if (name.Length == 0 || name.StartsWith("Capture screen", StringComparison.Ordinal))
                continue;

            // The operator identifies devices by this index while avfoundation matches by name, so the
            // index only needs to be unique among the listed cameras.
            webcams.Add(new WebcamWithIndex(name, webcams.Count));
        }
    }

    // "[AVFoundation indev @ 0x7f8a1c003000] " — the standard av_log line prefix. Device lines like
    // "[0] FaceTime HD Camera" don't match (no " @ 0x<hex>]" inside the brackets) and pass through.
    private static readonly System.Text.RegularExpressions.Regex LogPrefixRegex =
        new(@"^\[[^\]]* @ 0x[0-9a-fA-F]+\] ", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string StripLogPrefix(string line)
    {
        var match = LogPrefixRegex.Match(line);
        return match.Success ? line[match.Length..] : line;
    }
}

/// <summary>A capture device by list name and platform device index.</summary>
public sealed record WebcamWithIndex(string Name, int Index);
