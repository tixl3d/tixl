#nullable enable
using System.Numerics;
using System.Runtime.InteropServices;
using T3.Core.Logging;

namespace T3.IoServices;

/// <summary>
/// The current deflection of a 3Dconnexion SpaceMouse, for the editor camera and the [SpaceMouse] operator. On Linux it
/// is read from the device's input device (evdev) like spacenavd does, so no daemon is needed. On Windows the editor
/// reads raw input from its window and passes each report to <see cref="SetFromRawInput"/>.
/// </summary>
/// <remarks>
/// Each complete report becomes the current deflection, instead of summing the reports of a frame: report and frame
/// rates beat against each other, which made the camera stutter. Translation and rotation are separate groups, as
/// older devices (SpaceNavigator) alternate between a translation and a rotation report; a report updates only the
/// groups it mentions. Axes keep the HID order (X, Y, Z, Rx, Ry, Rz) and raw device units, about ±350 at full
/// deflection.
/// </remarks>
public static unsafe class SpaceMouseInput
{
    public enum States
    {
        NotFound,
        NoAccess,
        Connected,
    }

    public static States State { get; private set; } = States.NotFound;

    /** The device name when connected, or its input device path when it can't be opened. */
    public static string Description { get; private set; } = string.Empty;

    /** Translation in raw device units; see <see cref="FullDeflection"/>. */
    public static Vector3 Translation { get; private set; }

    /** Rotation in raw device units. */
    public static Vector3 Rotation { get; private set; }

    public static bool IsButton0Down { get; private set; }
    public static bool IsButton1Down { get; private set; }

    /** When the device last reported anything; 0 if never. */
    public static long LastReportTicksMs { get; private set; }

    /// <summary>
    /// False while the editor uses the SpaceMouse for its camera. Operators then see it at rest.
    /// </summary>
    public static bool IsAvailableToOperators { get; set; }

    /** Raw units at full deflection of the cap, for normalizing to -1..1. */
    public const float FullDeflection = 350;

    /** Call once per frame on the main thread. Reads pending Linux events and ends motion once reports stop. */
    public static void Update()
    {
        if (OperatingSystem.IsLinux())
            PollLinuxDevice();

        // Zero values are never reported for relative axes, so a pause in a group's reports means it is at rest
        if (!_isRelative)
            return;

        var now = Environment.TickCount64;
        if (now - _lastTranslationReportTicksMs > RestTimeoutMs)
            Translation = Vector3.Zero;

        if (now - _lastRotationReportTicksMs > RestTimeoutMs)
            Rotation = Vector3.Zero;
    }

    /// <summary>
    /// Takes one Windows raw input report. Older devices send translation and rotation in separate reports, so a
    /// null part keeps its last value.
    /// </summary>
    public static void SetFromRawInput(Vector3? translation, Vector3? rotation)
    {
        var now = Environment.TickCount64;
        if (translation != null)
        {
            Translation = translation.Value;
            _lastTranslationReportTicksMs = now;
        }

        if (rotation != null)
        {
            Rotation = rotation.Value;
            _lastRotationReportTicksMs = now;
        }

        _isRelative = true;
        State = States.Connected;
        LastReportTicksMs = now;
    }

    public static void SetButtonsFromRawInput(bool button0, bool button1)
    {
        IsButton0Down = button0;
        IsButton1Down = button1;
    }

    /** Closes the Linux device and looks for one right away, e.g. after replugging or fixing access rights. */
    public static void Rescan()
    {
        if (!OperatingSystem.IsLinux())
            return;

        CloseLinuxDevice();
        _lastScanTicksMs = Environment.TickCount64;
        if (!TryFindLinuxDevice(out var path, out var name))
        {
            State = States.NotFound;
            Description = string.Empty;
            return;
        }

        _fd = open(path, O_RDONLY | O_NONBLOCK | O_CLOEXEC);
        if (_fd < 0)
        {
            State = States.NoAccess;
            Description = path;
            Log.Warning($"Found {name} at {path} but can't read it. A udev rule granting access (or spacenavd) is needed.");
            return;
        }

        State = States.Connected;
        Description = name;
        Log.Debug($"SpaceMouse connected: {name} ({path})");
    }

    private static void PollLinuxDevice()
    {
        if (_fd < 0)
        {
            if (Environment.TickCount64 - _lastScanTicksMs > ScanIntervalMs)
                Rescan();

            return;
        }

        const int eventSize = 24;
        const int bufferEvents = 64;
        var buffer = stackalloc byte[eventSize * bufferEvents];
        while (true)
        {
            var bytesRead = read(_fd, buffer, eventSize * bufferEvents);
            if (bytesRead < 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error is EAGAIN or EINTR)
                    break;

                // ENODEV: unplugged
                CloseLinuxDevice();
                return;
            }

            if (bytesRead == 0)
                break;

            for (var offset = 0; offset + eventSize <= bytesRead; offset += eventSize)
            {
                var type = *(ushort*)(buffer + offset + 16);
                var code = *(ushort*)(buffer + offset + 18);
                var value = *(int*)(buffer + offset + 20);
                HandleEvent(type, code, value);
            }

            if (bytesRead < eventSize * bufferEvents)
                break;
        }
    }

    private static void HandleEvent(ushort type, ushort code, int value)
    {
        switch (type)
        {
            case EV_SYN when code == SYN_REPORT:
                CompleteReport();
                break;

            case EV_ABS when code < AxisCount:
                _reportAxes[code] = value;
                _isRelative = false;
                break;

            case EV_REL when code < AxisCount:
                _reportAxes[code] = value;
                _reportedAxes |= 1 << code;
                _isRelative = true;
                break;

            case EV_KEY when code == BTN_0:
                IsButton0Down = value != 0;
                break;

            case EV_KEY when code == BTN_1:
                IsButton1Down = value != 0;
                break;
        }
    }

    /// <summary>
    /// A relative report only lists axes that aren't zero, so within a group it mentions the others are zero. A group
    /// it doesn't mention keeps its value. Absolute axes always keep their last value.
    /// </summary>
    private static void CompleteReport()
    {
        var now = Environment.TickCount64;
        var hasTranslation = !_isRelative || (_reportedAxes & TranslationAxesMask) != 0;
        var hasRotation = !_isRelative || (_reportedAxes & RotationAxesMask) != 0;
        if (_isRelative)
        {
            for (var axis = 0; axis < AxisCount; axis++)
            {
                if ((_reportedAxes & (1 << axis)) == 0)
                    _reportAxes[axis] = 0;
            }
        }

        if (hasTranslation)
        {
            Translation = new Vector3(_reportAxes[0], _reportAxes[1], _reportAxes[2]);
            _lastTranslationReportTicksMs = now;
        }

        if (hasRotation)
        {
            Rotation = new Vector3(_reportAxes[3], _reportAxes[4], _reportAxes[5]);
            _lastRotationReportTicksMs = now;
        }

        _reportedAxes = 0;
        LastReportTicksMs = now;
    }

    private static void CloseLinuxDevice()
    {
        if (_fd >= 0)
            close(_fd);

        _fd = -1;
        Array.Clear(_reportAxes);
        _reportedAxes = 0;
        Translation = Vector3.Zero;
        Rotation = Vector3.Zero;
        IsButton0Down = false;
        IsButton1Down = false;
        if (State == States.Connected)
            State = States.NotFound;
    }

    /// <summary>
    /// Finds the first input device of 3Dconnexion (vendor 256f, or Logitech's 046d:c6xx range they used before)
    /// that has motion axes. Wireless receivers also expose devices without axes, which are skipped.
    /// </summary>
    private static bool TryFindLinuxDevice(out string path, out string name)
    {
        path = string.Empty;
        name = string.Empty;
        string text;
        try
        {
            text = File.ReadAllText("/proc/bus/input/devices");
        }
        catch (Exception e)
        {
            Log.Debug("Can't list input devices: " + e.Message);
            return false;
        }

        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var isSpaceMouse = false;
            var hasAxes = false;
            var blockName = string.Empty;
            var eventName = string.Empty;
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("I:"))
                {
                    var vendor = GetField(line, "Vendor=");
                    var product = GetField(line, "Product=");
                    isSpaceMouse |= vendor == "256f" || vendor == "046d" && product.StartsWith("c6");
                }
                else if (line.StartsWith("N:"))
                {
                    blockName = line[(line.IndexOf('=') + 1)..].Trim('"');
                    isSpaceMouse |= blockName.Contains("3Dconnexion", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.StartsWith("H:"))
                {
                    foreach (var handler in line[(line.IndexOf('=') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (handler.StartsWith("event"))
                            eventName = handler;
                    }
                }
                else if (line.StartsWith("B: ABS=") || line.StartsWith("B: REL="))
                {
                    hasAxes |= line.Trim() is not ("B: ABS=0" or "B: REL=0");
                }
            }

            if (!isSpaceMouse || !hasAxes || eventName.Length == 0)
                continue;

            path = "/dev/input/" + eventName;
            name = blockName;
            return true;
        }

        return false;
    }

    private static string GetField(string line, string key)
    {
        var start = line.IndexOf(key, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        start += key.Length;
        var end = line.IndexOf(' ', start);
        return (end < 0 ? line[start..] : line[start..end]).ToLowerInvariant();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte* buffer, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    private const int AxisCount = 6;
    private const long ScanIntervalMs = 2000;

    /** Several report intervals without a report of a group means that part of the cap was released. */
    private const long RestTimeoutMs = 120;

    private const int TranslationAxesMask = 0b000111;
    private const int RotationAxesMask = 0b111000;

    private const int O_RDONLY = 0;
    private const int O_NONBLOCK = 0x800;
    private const int O_CLOEXEC = 0x80000;
    private const int EAGAIN = 11;
    private const int EINTR = 4;

    private const ushort EV_SYN = 0;
    private const ushort EV_KEY = 1;
    private const ushort EV_REL = 2;
    private const ushort EV_ABS = 3;
    private const ushort SYN_REPORT = 0;
    private const ushort BTN_0 = 0x100;
    private const ushort BTN_1 = 0x101;

    private static int _fd = -1;
    /** The tick count is milliseconds since boot, so 0 lets the first update scan right away. */
    private static long _lastScanTicksMs;
    private static bool _isRelative;
    private static int _reportedAxes;
    private static long _lastTranslationReportTicksMs;
    private static long _lastRotationReportTicksMs;
    private static readonly int[] _reportAxes = new int[AxisCount];
}
