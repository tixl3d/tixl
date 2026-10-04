#nullable enable
using System;
using System.Collections.Generic;

namespace T3.IoServices.Midi;

/// <summary>
/// Opens the machine's MIDI ports through whichever API the platform offers.
/// </summary>
/// <remarks>
/// MIDI has no portable library worth depending on, so each platform talks to its own system API:
/// WinMM through NAudio on Windows, the ALSA sequencer on Linux. Both are present wherever the OS has
/// sound, so nothing is bundled.
/// </remarks>
public abstract class MidiDeviceProvider
{
    public static MidiDeviceProvider Current { get; } = CreateForPlatform();

    /// <summary>Named in logs so an unexpected set of devices can be traced to the backend that found them.</summary>
    public abstract string Name { get; }

    /// <summary>False when the platform has no MIDI support at all; callers then skip MIDI silently.</summary>
    public abstract bool IsAvailable { get; }

    /// <summary>
    /// Opens every port the user has not excluded, appending to the given lists.
    /// </summary>
    /// <param name="shouldCapture">
    /// Decides per product name whether to open the port. TiXL grabs devices exclusively on some
    /// platforms, so users can keep a controller free for another application.
    /// </param>
    public abstract void OpenDevices(Func<string, bool> shouldCapture,
                                     List<MidiInputDevice> inputs,
                                     List<MidiOutputDevice> outputs);

    /// <summary>Releases backend-wide state after the last device was closed.</summary>
    public abstract void Shutdown();

    private static MidiDeviceProvider CreateForPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WinMmMidiDeviceProvider();
        }

        if (OperatingSystem.IsLinux())
        {
            return new AlsaSeqMidiDeviceProvider();
        }

        return new UnsupportedMidiDeviceProvider();
    }

    private sealed class UnsupportedMidiDeviceProvider : MidiDeviceProvider
    {
        public override string Name => "none";
        public override bool IsAvailable => false;

        public override void OpenDevices(Func<string, bool> shouldCapture,
                                         List<MidiInputDevice> inputs,
                                         List<MidiOutputDevice> outputs)
        {
        }

        public override void Shutdown()
        {
        }
    }
}
