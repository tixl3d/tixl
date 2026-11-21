#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using T3.Core.DataTypes;

namespace T3.Core.Video;

/// <summary>
/// Offers this machine's cameras to the other TiXL clients of a session. Implemented by the package that can
/// capture (Video); discovered by type when the package loads.
/// </summary>
public interface IPeerCameraProvider
{
    IReadOnlyList<string> GetCameraNames();

    /// <summary>Opens <paramref name="cameraName"/> without any operator; null with <paramref name="error"/> when it cannot be opened.</summary>
    IPeerCameraCapture? Open(string cameraName, out string? error);
}

/// <summary>A camera opened for a remote client. Frames are uploaded on the thread that calls <see cref="Update"/>.</summary>
public interface IPeerCameraCapture : IDisposable
{
    /// <summary>Uploads the newest captured frame and returns it; null while no new frame is available.</summary>
    Texture2D? Update();

    string Status { get; }
}

/// <summary>The camera providers available on this machine, following package load/unload.</summary>
public static class PeerCameraRegistry
{
    public static void Register(IPeerCameraProvider provider)
    {
        lock (_providers)
        {
            if (!_providers.Contains(provider))
                _providers.Add(provider);
        }
    }

    public static void Unregister(IPeerCameraProvider provider)
    {
        lock (_providers)
        {
            _providers.Remove(provider);
        }
    }

    /// <summary>The first provider, or null when no loaded package can capture.</summary>
    public static IPeerCameraProvider? Provider
    {
        get
        {
            lock (_providers)
            {
                return _providers.Count > 0 ? _providers[0] : null;
            }
        }
    }

    private static readonly List<IPeerCameraProvider> _providers = [];
}

/// <summary>
/// The cameras other TiXL clients of the current session announced. A video input lists them next to its own
/// devices as "Camera @ Machine"; selecting one stores that text in the input's device name.
/// </summary>
public static class PeerCameraDirectory
{
    public const string Separator = " @ ";

    /// <summary>The name this machine goes by in references; a reference to it resolves to the local camera.</summary>
    public static string LocalMachine { get; set; } = Environment.MachineName;

    /// <summary>Bumped whenever the set of announced cameras changes.</summary>
    public static int Version => Volatile.Read(ref _version);

    public static string Format(string camera, string machine) => camera + Separator + machine;

    public static bool TryParse(string? reference, out string camera, out string machine)
    {
        camera = "";
        machine = "";
        if (string.IsNullOrEmpty(reference))
            return false;

        var index = reference.LastIndexOf(Separator, StringComparison.Ordinal);
        if (index <= 0 || index + Separator.Length >= reference.Length)
            return false;

        camera = reference[..index];
        machine = reference[(index + Separator.Length)..];
        return true;
    }

    /// <summary>Stores the cameras a machine announced; refreshes its last-seen time even when the list is unchanged.</summary>
    public static void SetMachine(string machine, IReadOnlyList<string> cameras)
    {
        if (string.IsNullOrEmpty(machine))
            return;

        lock (_lock)
        {
            _lastSeenTicks[machine] = Environment.TickCount64;
            if (_machines.TryGetValue(machine, out var existing) && existing.SequenceEqual(cameras))
                return;

            _machines[machine] = cameras.ToArray();
            _version++;
        }
    }

    public static bool IsMachineKnown(string machine)
    {
        lock (_lock)
        {
            return _machines.ContainsKey(machine);
        }
    }

    public static void RemoveMachine(string machine)
    {
        lock (_lock)
        {
            _lastSeenTicks.Remove(machine);
            if (_machines.Remove(machine))
                _version++;
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _lastSeenTicks.Clear();
            if (_machines.Count == 0)
                return;

            _machines.Clear();
            _version++;
        }
    }

    /// <summary>Forgets machines that have not announced for <paramref name="maxAgeMs"/>, e.g. after they left the session.</summary>
    public static void PruneOlderThan(long maxAgeMs)
    {
        var now = Environment.TickCount64;
        lock (_lock)
        {
            _staleScratch.Clear();
            foreach (var (machine, seen) in _lastSeenTicks)
            {
                if (now - seen > maxAgeMs)
                    _staleScratch.Add(machine);
            }

            foreach (var machine in _staleScratch)
            {
                _lastSeenTicks.Remove(machine);
                if (_machines.Remove(machine))
                    _version++;
            }
        }
    }

    /// <summary>Appends "Camera @ Machine" for every camera a connected peer announced, machines in name order.</summary>
    public static void AppendReferences(List<string> into)
    {
        lock (_lock)
        {
            _orderScratch.Clear();
            _orderScratch.AddRange(_machines.Keys);
            _orderScratch.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var machine in _orderScratch)
            {
                foreach (var camera in _machines[machine])
                    into.Add(Format(camera, machine));
            }
        }
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<string, string[]> _machines = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, long> _lastSeenTicks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> _staleScratch = [];
    private static readonly List<string> _orderScratch = [];
    private static int _version;
}

/// <summary>
/// The camera selections a video input made on another machine. The input refreshes its entry every frame it
/// evaluates; the session service requests the stream and feeds the answer back through
/// <see cref="RemoteVideoInputBus"/>. An entry that is no longer refreshed lapses, which ends the stream.
/// </summary>
public static class PeerCameraBus
{
    public readonly record struct Want(string Key, string Machine, string Camera);

    /// <summary>How long an entry survives without being refreshed by its input.</summary>
    public const int WantLifetimeMs = 3000;

    public static void SetWant(string key, string machine, string camera)
    {
        var now = Environment.TickCount64;
        lock (_lock)
        {
            if (_wants.TryGetValue(key, out var existing) && existing.Machine == machine && existing.Camera == camera)
            {
                existing.LastRefreshTicks = now;
                return;
            }

            _wants[key] = new Entry(machine, camera, now);
            _version++;
        }
    }

    public static void ClearWant(string key)
    {
        lock (_lock)
        {
            if (_wants.Remove(key))
                _version++;
        }
    }

    /// <summary>Bumped whenever a selection appears, changes or lapses.</summary>
    public static int Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    /// <summary>Copies the current selections into <paramref name="into"/>, dropping the ones that lapsed.</summary>
    public static void CollectWants(List<Want> into)
    {
        var now = Environment.TickCount64;
        lock (_lock)
        {
            _lapsedScratch.Clear();
            foreach (var (key, entry) in _wants)
            {
                if (now - entry.LastRefreshTicks > WantLifetimeMs)
                    _lapsedScratch.Add(key);
                else
                    into.Add(new Want(key, entry.Machine, entry.Camera));
            }

            foreach (var key in _lapsedScratch)
                _wants.Remove(key);

            if (_lapsedScratch.Count > 0)
                _version++;
        }
    }

    private sealed class Entry(string machine, string camera, long lastRefreshTicks)
    {
        public readonly string Machine = machine;
        public readonly string Camera = camera;
        public long LastRefreshTicks = lastRefreshTicks;
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<string, Entry> _wants = new(StringComparer.Ordinal);
    private static readonly List<string> _lapsedScratch = [];
    private static int _version;
}
