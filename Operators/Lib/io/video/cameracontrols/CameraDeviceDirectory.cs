#nullable enable
using DirectShowLib;

namespace Lib.io.video;

/// <summary>A camera as the control backends address it: the native name plus the native index
/// (DirectShow moniker order on Windows, /dev/videoN on Linux; macOS resolves by name).</summary>
internal sealed record WebcamWithIndex(string Name, int Index);

/// <summary>
/// Maps the camera name [WebcamInput] stores — SDL's name, with " (2)", " (3)" appended to identical
/// cameras — onto the device the native control backends talk to. Frames come through SDL, controls
/// through DirectShow / V4L2 / AVFoundation, so the two lists have to be joined by name.
/// <para>
/// Call from the evaluation thread only when the selection changes or on Reconnect: the Windows path
/// enumerates DirectShow monikers (a few milliseconds), and nothing here is cached between calls, so a
/// camera plugged in later is always found.
/// </para>
/// </summary>
internal static class CameraDeviceDirectory
{
    /// <summary>
    /// Resolves a stored camera name. An empty name means "the first camera", like SDL's open.
    /// </summary>
    public static bool TryResolve(string? cameraName, [NotNullWhen(true)] out WebcamWithIndex? device)
    {
        device = null;
        List<WebcamWithIndex> devices;
        try
        {
            devices = ListDevices();
        }
        catch (Exception e)
        {
            Log.Debug($"Camera controls: listing devices failed: {e.Message}");
            return false;
        }

        if (devices.Count == 0)
            return false;

        if (string.IsNullOrEmpty(cameraName))
        {
            device = devices[0];
            return true;
        }

        // Exact match first — this includes the de-duplicated " (2)" names, which ListDevices mints the
        // same way the SDL provider does.
        foreach (var candidate in devices)
        {
            if (string.Equals(candidate.Name, cameraName, StringComparison.Ordinal))
            {
                device = candidate;
                return true;
            }
        }

        // The platform APIs sometimes disagree on decoration (trailing spaces, a vendor suffix on one side).
        // A unique case-insensitive prefix match is still unambiguous.
        WebcamWithIndex? prefixMatch = null;
        foreach (var candidate in devices)
        {
            if (!candidate.Name.StartsWith(cameraName, StringComparison.OrdinalIgnoreCase)
                && !cameraName.StartsWith(candidate.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (prefixMatch != null)
                return false;

            prefixMatch = candidate;
        }

        device = prefixMatch;
        return device != null;
    }

    private static List<WebcamWithIndex> ListDevices()
    {
        var raw = new List<(string Name, int Index)>();
        if (OperatingSystem.IsWindows())
            ListDirectShow(raw);
        else if (OperatingSystem.IsLinux())
            ListV4L2(raw);
        else if (OperatingSystem.IsMacOS())
            ListAvFoundation(raw);

        // Same de-duplication as SdlCameraProvider: identical cameras become "Name", "Name (2)", ...
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<WebcamWithIndex>(raw.Count);
        foreach (var (name, index) in raw)
        {
            seen.TryGetValue(name, out var count);
            seen[name] = ++count;
            result.Add(new WebcamWithIndex(count == 1 ? name : $"{name} ({count})", index));
        }

        return result;
    }

    private static void ListDirectShow(List<(string Name, int Index)> devices)
    {
        // The backend binds devices[index] of this very enumeration, so the index is the moniker position.
        var monikers = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        for (var i = 0; i < monikers.Length; i++)
        {
            devices.Add((monikers[i].Name, i));
            monikers[i].Dispose();
        }
    }

    private static void ListV4L2(List<(string Name, int Index)> devices)
    {
        // A physical camera usually exposes several nodes (capture, metadata); the capture node is the one
        // whose sysfs `index` is 0. Its `name` is the card name SDL reports for the camera.
        const string sysfsRoot = "/sys/class/video4linux";
        if (!Directory.Exists(sysfsRoot))
            return;

        var nodes = new List<(int Index, string Path)>();
        foreach (var node in Directory.GetDirectories(sysfsRoot))
        {
            var nodeName = Path.GetFileName(node);
            if (nodeName.StartsWith("video", StringComparison.Ordinal)
                && int.TryParse(nodeName.AsSpan("video".Length), out var index))
                nodes.Add((index, node));
        }

        nodes.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (var (index, node) in nodes)
        {
            try
            {
                var indexFile = Path.Combine(node, "index");
                if (File.Exists(indexFile)
                    && int.TryParse(File.ReadAllText(indexFile).Trim(), out var nodeIndex)
                    && nodeIndex != 0)
                    continue;

                var nameFile = Path.Combine(node, "name");
                var name = File.Exists(nameFile) ? File.ReadAllText(nameFile).Trim() : Path.GetFileName(node);
                devices.Add((name, index));
            }
            catch (Exception e)
            {
                Log.Debug($"Camera controls: cannot inspect {node}: {e.Message}");
            }
        }
    }

    private static void ListAvFoundation(List<(string Name, int Index)> devices)
    {
        // The AVFoundation backend resolves by localizedName itself; the index only keys the worker's
        // per-device bookkeeping. Listing goes through the backend's own interop.
        var names = CameraControlsAvFoundation.ListDeviceNames();
        for (var i = 0; i < names.Count; i++)
            devices.Add((names[i], i));
    }
}
