#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using T3.Core.DataTypes;
using T3.Core.Logging;

namespace T3.Core.Video;

/// <summary>
/// A video input operator that can be driven by a boygroup master. On the master it exposes the frame it
/// captured; on a render node the bus hands it the frame received over the network instead.
/// </summary>
public interface IRemoteVideoInput
{
    /// <summary>Stable identity of this instance across master and render node (package id + instance path).</summary>
    string RelayKey { get; }

    /// <summary>Human-readable source name, used when naming the master's stream.</summary>
    string RelayDisplayName { get; }

    /// <summary>The locally captured frame on the master; the relay publishes it.</summary>
    Texture2D? RelayTexture { get; }

    /// <summary>Human-readable state of the local input (camera error, starting, ...) for the relay's status.</summary>
    string RelayStatus { get; }

    /// <summary>
    /// Called once per frame while this instance has a remote source. <paramref name="texture"/> is null
    /// until the first frame arrives (or while the stream is unavailable); the operator must keep its
    /// local device closed in that state and report <paramref name="status"/>.
    /// </summary>
    void ApplyRemoteFrame(Texture2D? texture, int frameCounter, string status);

    /// <summary>Called once when the remote source is gone and the operator should resume local capture.</summary>
    void EndRemoteFrames();
}

/// <summary>
/// A transport that can receive an <see cref="IRemoteVideoInput"/> stream (implemented by the OMT package).
/// Registered by type when the operator package loads, so the input bus does not reference the package.
/// </summary>
public interface IRemoteVideoInputProvider
{
    /// <summary>Stable transport name, e.g. "OMT".</summary>
    string Kind { get; }

    bool IsAvailable { get; }

    /// <summary>Why the transport cannot run on this machine; null when it can.</summary>
    string? UnavailableMessage { get; }

    /// <summary>Starts receiving <paramref name="sourceName"/> (the address the master advertised).</summary>
    IRemoteVideoInputReceiver? CreateReceiver(string sourceName);
}

/// <summary>One live receiver. Frames are uploaded on the thread that calls <see cref="Update"/>.</summary>
public interface IRemoteVideoInputReceiver : IDisposable
{
    /// <summary>Uploads the newest received frame and returns it; null while no new frame is available.</summary>
    Texture2D? Update();

    bool IsConnected { get; }

    string? StatusMessage { get; }
}

/// <summary>The remote-video transports available on this machine, following package load/unload.</summary>
public static class RemoteVideoInputRegistry
{
    public static void Register(IRemoteVideoInputProvider provider)
    {
        lock (_providers)
        {
            if (_providers.Any(p => p.Kind == provider.Kind))
                return;

            _providers.Add(provider);
        }
    }

    public static void Unregister(IRemoteVideoInputProvider provider)
    {
        lock (_providers)
        {
            _providers.Remove(provider);
        }
    }

    /// <summary>The first provider for <paramref name="kind"/>, or null when no package offers it.</summary>
    public static IRemoteVideoInputProvider? FindProvider(string kind)
    {
        lock (_providers)
        {
            return _providers.FirstOrDefault(p => p.Kind == kind);
        }
    }

    private static readonly List<IRemoteVideoInputProvider> _providers = [];
}

/// <summary>One source the master announced for a key. An empty address means the master has no stream yet.</summary>
public readonly record struct RemoteVideoSource(string Key, string Address, string Status);

/// <summary>
/// Publishes the video inputs of a boygroup master and feeds the matching inputs on a render node.
/// <para>
/// Master side: registered inputs expose their captured frame (<see cref="IReadOnlyList{IRemoteVideoInput}"/>),
/// which <see cref="T3.Editor.Show.BoygroupManager"/> sends over OMT.
/// </para>
/// <para>
/// Render-node side: the master's source list is stored here; <see cref="Pump"/> updates the matching
/// receivers once per frame and hands the newest texture to the input. An operator with a source assigned
/// therefore never opens its own camera, even while the stream is still connecting.
/// </para>
/// </summary>
public static class RemoteVideoInputBus
{
    public const string PreferredTransportKind = "OMT";

    /// <summary>Bumped when an input registers or is disposed, so the master can rebuild its senders.</summary>
    public static int InputsVersion => _inputsVersion;

    public static IReadOnlyList<IRemoteVideoInput> Inputs => _inputsSnapshot;

    public static void RegisterInput(IRemoteVideoInput input)
    {
        lock (_lock)
        {
            if (_inputs.Contains(input))
                return;

            _inputs.Add(input);
            RebuildInputSnapshot();
        }
    }

    public static void UnregisterInput(IRemoteVideoInput input)
    {
        lock (_lock)
        {
            if (!_inputs.Remove(input))
                return;

            _activeInputs.Remove(input);
            RebuildInputSnapshot();
        }
    }

    /// <summary>Number of sources the master announced (including ones still starting up).</summary>
    public static int RemoteSourceCount
    {
        get
        {
            lock (_lock)
            {
                return _masterSources.Count;
            }
        }
    }

    /// <summary>Replaces the master's source list; receivers for sources that disappeared are released.</summary>
    public static void SetRemoteSources(IEnumerable<RemoteVideoSource> sources)
    {
        lock (_lock)
        {
            Fill(_masterSources, sources);
            ApplySources();
        }
    }

    /// <summary>
    /// Replaces the sources of inputs that selected a camera on another TiXL client. A master's source for the
    /// same input wins over it.
    /// </summary>
    public static void SetPeerSources(IEnumerable<RemoteVideoSource> sources)
    {
        lock (_lock)
        {
            Fill(_peerSources, sources);
            ApplySources();
        }
    }

    /// <summary>Drops the master's sources, e.g. when the master disconnects; peer camera selections stay.</summary>
    public static void ClearRemoteSources()
    {
        lock (_lock)
        {
            _masterSources.Clear();
            ApplySources();
        }
    }

    private static void Fill(Dictionary<string, RemoteVideoSource> target, IEnumerable<RemoteVideoSource> sources)
    {
        target.Clear();
        foreach (var source in sources)
        {
            if (!string.IsNullOrEmpty(source.Key))
                target[source.Key] = source;
        }
    }

    /// <summary>Merges both layers into the active list, releases receivers that lost their source and ends inputs that lost theirs.</summary>
    private static void ApplySources()
    {
        _sources.Clear();
        foreach (var (key, source) in _masterSources)
            _sources[key] = source;

        foreach (var (key, source) in _peerSources)
            _sources.TryAdd(key, source);

        foreach (var key in _states.Keys.ToArray())
        {
            if (_sources.TryGetValue(key, out var source) && _states[key].Address == source.Address)
                continue;

            _states[key].Dispose();
            _states.Remove(key);
        }

        if (_activeInputs.Count == 0)
            return;

        _endedScratch.Clear();
        foreach (var input in _activeInputs)
        {
            if (!_sources.ContainsKey(input.RelayKey))
                _endedScratch.Add(input);
        }

        foreach (var input in _endedScratch)
        {
            input.EndRemoteFrames();
            _activeInputs.Remove(input);
        }

        _endedScratch.Clear();
    }

    /// <summary>Updates every active receiver and feeds the matching inputs. Call once per frame on a render node.</summary>
    public static void Pump()
    {
        lock (_lock)
        {
            if (_sources.Count == 0)
            {
                if (_activeInputs.Count == 0)
                    return;

                foreach (var input in _activeInputs)
                    input.EndRemoteFrames();

                _activeInputs.Clear();
                return;
            }

            foreach (var state in _states.Values)
                state.Update();

            foreach (var input in _inputsSnapshot)
            {
                if (!_sources.TryGetValue(input.RelayKey, out var source))
                    continue;

                var created = false;
                if (!_states.TryGetValue(source.Key, out var state))
                {
                    state = new RemoteSourceState(source);
                    _states[source.Key] = state;
                    created = true;
                }

                // A state created this frame has not been updated by the loop above; do it now so the
                // receiver exists and the first status is the transport's, not the master's placeholder.
                if (created)
                    state.Update();

                try
                {
                    input.ApplyRemoteFrame(state.Texture, state.FrameCounter, state.Status);
                }
                catch (Exception e)
                {
                    // One misbehaving input must not starve the others of their frames.
                    Log.Debug($"Applying a remote video frame to '{input.RelayKey}' failed: {e.Message}");
                }

                _activeInputs.Add(input);
            }

            if (_activeInputs.Count > 0)
            {
                _endedScratch.Clear();
                foreach (var input in _activeInputs)
                {
                    if (!_sources.ContainsKey(input.RelayKey))
                        _endedScratch.Add(input);
                }

                foreach (var input in _endedScratch)
                {
                    input.EndRemoteFrames();
                    _activeInputs.Remove(input);
                }

                _endedScratch.Clear();
            }
        }
    }

    /// <summary>Short status for the settings panel and the debug bridge.</summary>
    public static string Summary
    {
        get
        {
            lock (_lock)
            {
                if (_sources.Count == 0)
                    return "No remote video source";

                var live = _states.Values.Count(s => s.FrameCounter > 0);
                return live > 0
                           ? $"{live} of {_sources.Count} video input{(live == 1 ? "" : "s")} receiving"
                           : $"Waiting for {_sources.Count} video input{(_sources.Count == 1 ? "" : "s")}";
            }
        }
    }

    private static void RebuildInputSnapshot()
    {
        _inputsSnapshot = _inputs.ToArray();
        _inputsVersion++;
    }

    private sealed class RemoteSourceState : IDisposable
    {
        public RemoteSourceState(RemoteVideoSource source)
        {
            _source = source;
            Address = source.Address;
            Status = string.IsNullOrEmpty(source.Status) ? "Waiting for the master's video stream" : source.Status;
        }

        public readonly string Address;
        public Texture2D? Texture;
        public int FrameCounter;
        public string Status;

        public void Update()
        {
            if (_receiver == null)
            {
                TryCreateReceiver();
                return;
            }

            try
            {
                var frame = _receiver.Update();
                if (frame != null)
                {
                    Texture = frame;
                    FrameCounter++;
                }

                Status = _receiver.IsConnected
                             ? FrameCounter > 0 ? "Live" : "Connected — waiting for frames"
                             : string.IsNullOrWhiteSpace(_receiver.StatusMessage) ? "Connecting…" : _receiver.StatusMessage!;
            }
            catch (Exception e)
            {
                Status = $"Video receive failed: {e.Message}";
                DisposeReceiver();
                _nextCreateTicks = Environment.TickCount64 + RetryIntervalMs;
            }
        }

        public void Dispose()
        {
            DisposeReceiver();
            Texture = null;
            FrameCounter = 0;
        }

        private void TryCreateReceiver()
        {
            if (string.IsNullOrEmpty(Address))
            {
                Status = string.IsNullOrEmpty(_source.Status) ? "Waiting for the master's video stream" : _source.Status;
                return;
            }

            var now = Environment.TickCount64;
            if (now < _nextCreateTicks)
                return;

            var provider = RemoteVideoInputRegistry.FindProvider(PreferredTransportKind);
            if (provider == null)
            {
                Status = $"Master relays video via {PreferredTransportKind}, but that package is not loaded here";
                _nextCreateTicks = now + RetryIntervalMs;
                return;
            }

            if (!provider.IsAvailable)
            {
                Status = provider.UnavailableMessage ?? $"{PreferredTransportKind} is not available on this machine";
                _nextCreateTicks = now + RetryIntervalMs;
                return;
            }

            try
            {
                _receiver = provider.CreateReceiver(Address);
                if (_receiver == null)
                {
                    Status = $"{PreferredTransportKind} could not open {Address}";
                    _nextCreateTicks = now + RetryIntervalMs;
                    return;
                }

                Status = string.IsNullOrWhiteSpace(_receiver.StatusMessage) ? "Connecting…" : _receiver.StatusMessage!;
            }
            catch (Exception e)
            {
                Status = $"Could not receive {Address}: {e.Message}";
                _nextCreateTicks = now + RetryIntervalMs;
            }
        }

        private void DisposeReceiver()
        {
            try
            {
                _receiver?.Dispose();
            }
            catch (Exception e)
            {
                Log.Debug($"Disposing a remote video receiver failed: {e.Message}");
            }

            _receiver = null;
        }

        private const int RetryIntervalMs = 2000;
        private readonly RemoteVideoSource _source;
        private IRemoteVideoInputReceiver? _receiver;
        private long _nextCreateTicks;
    }

    private static readonly object _lock = new();
    private static readonly List<IRemoteVideoInput> _inputs = [];
    private static volatile IRemoteVideoInput[] _inputsSnapshot = [];
    private static readonly List<IRemoteVideoInput> _endedScratch = [];
    private static readonly HashSet<IRemoteVideoInput> _activeInputs = [];
    private static readonly Dictionary<string, RemoteVideoSource> _masterSources = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, RemoteVideoSource> _peerSources = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, RemoteVideoSource> _sources = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, RemoteSourceState> _states = new(StringComparer.Ordinal);
    private static int _inputsVersion;
}
