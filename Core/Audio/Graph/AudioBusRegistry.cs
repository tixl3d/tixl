#nullable enable
using System.Collections.Generic;
using T3.Core.Animation;
using T3.Core.Audio.Native;

namespace T3.Core.Audio.Graph;

/// <summary>
/// Liveness tracking for audio-graph buses. A bus ([AudioBus]) is pull-based — only its per-frame evaluation
/// applies routing and gains. When a bus stops being evaluated (unpinned, dropped from the render chain) it
/// would otherwise keep playing its last state: frozen, and deaf to parameter changes upstream. Instead, each
/// bus heartbeats here on update; <see cref="AudioEngine.CompleteFrame"/> sweeps the registry and stops buses
/// that missed their heartbeat. The next evaluation revives them.
/// </summary>
public static class AudioBusRegistry
{
    /// <summary>Called by a bus every update — declares "this bus is actively driven this frame".</summary>
    internal static void MarkAlive(AudioHandle bus)
    {
        lock (_lock)
        {
            if (!_buses.TryGetValue(bus, out var state))
                _buses[bus] = state = new BusState();

            state.LastAliveFrame = Playback.FrameCount;
        }
    }

    /// <summary>Called when a bus is released (op disposed).</summary>
    internal static void Unregister(AudioHandle bus)
    {
        lock (_lock)
        {
            _buses.Remove(bus);
        }
    }

    /// <summary>Once per frame: stop buses that weren't evaluated recently, restart revived ones.</summary>
    internal static void PauseStaleBuses()
    {
        var engine = AudioMixerManager.NativeEngine;
        if (engine == null)
            return;

        lock (_lock)
        {
            foreach (var (bus, state) in _buses)
            {
                var stale = Playback.FrameCount - state.LastAliveFrame > FrameSlack;
                if (stale == state.IsPaused)
                    continue;

                engine.SetBusPlaying(bus, !stale);
                state.IsPaused = stale;
            }
        }
    }

    // Mutable entry so the sweep can update state while enumerating without touching the dictionary itself.
    private sealed class BusState
    {
        public int LastAliveFrame;
        public bool IsPaused;
    }

    private static readonly Dictionary<AudioHandle, BusState> _buses = new();
    private static readonly object _lock = new();
    private const int FrameSlack = 2;
}
