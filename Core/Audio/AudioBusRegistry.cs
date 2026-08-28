#nullable enable
using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Animation;

namespace T3.Core.Audio;

/// <summary>
/// Liveness tracking for audio-graph bus submixes. A bus ([AudioBus]) is pull-based — only its per-frame
/// evaluation applies routing and gains. When a bus stops being evaluated (unpinned, dropped from the render
/// chain) its submix would otherwise keep playing its last state: frozen, and deaf to parameter changes
/// upstream. Instead, each bus heartbeats here on update; <see cref="AudioEngine.CompleteFrame"/> sweeps the
/// registry and pauses submixes that missed their heartbeat. The next evaluation revives them.
/// </summary>
public static class AudioBusRegistry
{
    /// <summary>Called by a bus every update — declares "this submix is actively driven this frame".</summary>
    public static void MarkAlive(int submixHandle)
    {
        if (submixHandle == 0)
            return;

        lock (_lock)
        {
            if (!_buses.TryGetValue(submixHandle, out var state))
                _buses[submixHandle] = state = new BusState();

            state.LastAliveFrame = Playback.FrameCount;
        }
    }

    /// <summary>Called when a bus frees its submix (op disposed).</summary>
    public static void Unregister(int submixHandle)
    {
        lock (_lock)
        {
            _buses.Remove(submixHandle);
        }
    }

    /// <summary>Once per frame: pause submixes whose bus wasn't evaluated recently, unpause revived ones.</summary>
    internal static void PauseStaleBuses()
    {
        lock (_lock)
        {
            foreach (var (submix, state) in _buses)
            {
                var stale = Playback.FrameCount - state.LastAliveFrame > FrameSlack;
                if (stale == state.IsPaused)
                    continue;

                if (stale)
                    BassMix.ChannelFlags(submix, BassFlags.MixerChanPause, BassFlags.MixerChanPause);
                else
                    BassMix.ChannelFlags(submix, 0, BassFlags.MixerChanPause);

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

    private static readonly Dictionary<int, BusState> _buses = new();
    private static readonly object _lock = new();
    private const int FrameSlack = 2;
}
