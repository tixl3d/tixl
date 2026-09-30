#nullable enable
using System;
using System.Threading;
using ManagedBass;
using ManagedBass.Mix;
using ManagedBass.Wasapi;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Which of the native BASS libraries are actually there. Every path that P/Invokes one has to ask first,
/// because a missing library throws from the call itself — and the audio paths run from the render loop,
/// where an escaping exception takes the whole editor down.
/// </summary>
/// <remarks>
/// The components ship and port separately, so they are asked about separately: BASSWASAPI is Windows-only
/// and has no Linux or macOS build at all, while BASS and BASSmix do. Probing by calling each library rather
/// than by looking for a file keeps the answer honest — it tests the exact resolution ManagedBass performs,
/// including a library that is present but built for another architecture.
/// </remarks>
public static class BassLibrary
{
    /// <summary>True when BASS can be called: playback, decoding and offline analysis all need it.</summary>
    public static bool IsAvailable => _core.Value;

    /// <summary>True when BASSmix can be called. Without it there are no mixers, so no operator audio.</summary>
    public static bool IsMixAvailable => IsAvailable && _mix.Value;

    /// <summary>
    /// True when BASSWASAPI can be called, which means Windows. Live audio input goes through it, so
    /// elsewhere the external-device source stays unavailable until it is replaced by BASS's record API.
    /// </summary>
    public static bool IsWasapiAvailable => IsAvailable && _wasapi.Value;

    /// <summary>
    /// One component's answer, worked out once. Reading a version only queries the loaded library: it needs
    /// no device and changes nothing, which makes it the cheapest call that still proves the library loads.
    /// </summary>
    private sealed class Component(string name, Func<Version> readVersion)
    {
        public bool Value
        {
            get
            {
                if (_probed)
                    return _available;

                lock (_lock)
                {
                    if (!_probed)
                    {
                        _available = Probe();
                        _probed = true;
                    }
                }

                return _available;
            }
        }

        private bool Probe()
        {
            try
            {
                _ = readVersion();
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                // One line, not the loader's whole list of probed paths, which it repeats for every caller.
                Log.Warning($"{name} is unavailable on this platform; the audio features that need it are disabled.");
                return false;
            }
        }

        private readonly Lock _lock = new();
        private bool _probed;
        private bool _available;
    }

    private static readonly Component _core = new("BASS", () => Bass.Version);
    private static readonly Component _mix = new("BASSmix", () => BassMix.Version);
    private static readonly Component _wasapi = new("BASSWASAPI", () => BassWasapi.Version);
}
