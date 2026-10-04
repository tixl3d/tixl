#nullable enable
using System;
using System.Collections.Generic;
using ManagedBass;
using T3.Core.Logging;

namespace T3.Core.Audio.Input;

/// <summary>
/// Capture through BASS's recording API, used wherever WASAPI does not exist.
/// </summary>
/// <remarks>
/// On Linux this reaches microphones and line inputs. It cannot reach loopback: BASS enumerates ALSA
/// capture devices, and a PipeWire or PulseAudio monitor source is not one - it reports every device as
/// non-loopback, and pointing the ALSA plugin at a monitor with <c>PULSE_SOURCE</c> does not redirect it
/// either. Reacting to the project's own audio does not need this path; <c>AudioSources.ProjectSoundTrack</c>
/// analyses the mixer directly and already works everywhere.
/// </remarks>
internal sealed class BassRecordAudioInputBackend : AudioInputBackend
{
    public override string Name => "BASS recording";
    public override bool IsAvailable => BassLibrary.IsAvailable;
    public override int ActiveChannelCount => _activeChannelCount;

    public override void EnumerateDevices(List<AudioInputDevice> devices)
    {
        if (!BassLibrary.IsAvailable)
        {
            return;
        }

        for (var deviceIndex = 0; Bass.RecordGetDeviceInfo(deviceIndex, out var deviceInfo); deviceIndex++)
        {
            if (!deviceInfo.IsEnabled)
            {
                continue;
            }

            // The driverless "Default" entry resolves to ALSA's dsnoop, which fails to open on a machine
            // whose card is already claimed by a sound server. The real devices behind it still enumerate.
            if (string.IsNullOrEmpty(deviceInfo.Driver) || deviceInfo.Driver == "default")
            {
                continue;
            }

            Log.Debug($"Found audio input ID:{devices.Count} {deviceInfo.Name} (driver {deviceInfo.Driver})");
            devices.Add(new AudioInputDevice
                            {
                                Name = deviceInfo.Name,
                                SampleRate = AudioConfig.MixerFrequency > 0 ? AudioConfig.MixerFrequency : DefaultSampleRate,
                                IsLoopback = deviceInfo.IsLoopback,
                                BackendIndex = deviceIndex,
                            });
        }
    }

    public override bool TryStartCapture(AudioInputDevice device, AudioCaptureCallback onData)
    {
        StopCapture();

        if (!Bass.RecordInit(device.BackendIndex))
        {
            Log.Error($"Can't open audio input '{device.Name}': {Bass.LastError}");
            return false;
        }

        // Held in a field so the delegate handed to native code is not collected while capture runs.
        _procedure = (_, buffer, length, _) =>
                     {
                         onData(buffer, length);
                         return true;
                     };

        var sampleRate = device.SampleRate > 0 ? device.SampleRate : DefaultSampleRate;
        _activeChannelCount = CaptureChannelCount;
        _recordingHandle = Bass.RecordStart(sampleRate, CaptureChannelCount, BassFlags.Float, _procedure);
        if (_recordingHandle == 0)
        {
            Log.Error($"Can't start capturing from '{device.Name}': {Bass.LastError}");
            Bass.RecordFree();
            _procedure = null;
            return false;
        }

        return true;
    }

    public override void StopCapture()
    {
        if (_recordingHandle == 0)
        {
            return;
        }

        Bass.ChannelStop(_recordingHandle);
        Bass.RecordFree();
        _recordingHandle = 0;
        _procedure = null;
    }

    public override int GetData(float[] buffer, int lengthOrFlags)
    {
        return _recordingHandle == 0 ? -1 : Bass.ChannelGetData(_recordingHandle, buffer, lengthOrFlags);
    }

    public override int GetLevel()
    {
        return _recordingHandle == 0 ? -1 : Bass.ChannelGetLevel(_recordingHandle);
    }

    /** Matches what the analysis and WAV writer expect, and what every input device provides. */
    private const int CaptureChannelCount = 2;

    private const int DefaultSampleRate = 48000;

    private RecordProcedure? _procedure;
    private int _recordingHandle;
    private int _activeChannelCount;
}
