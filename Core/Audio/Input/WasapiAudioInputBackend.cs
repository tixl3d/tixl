#nullable enable
using System;
using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Wasapi;
using T3.Core.Logging;

namespace T3.Core.Audio.Input;

/// <summary>Windows capture through WASAPI, which also reaches loopback devices.</summary>
internal sealed class WasapiAudioInputBackend : AudioInputBackend
{
    public override string Name => "WASAPI";
    public override bool IsAvailable => BassLibrary.IsWasapiAvailable;
    public override int ActiveChannelCount => _activeChannelCount;

    public override void EnumerateDevices(List<AudioInputDevice> devices)
    {
        // WASAPI needs a live BASS device before it will enumerate.
        AudioMixerManager.Initialize();

        var deviceCount = BassWasapi.DeviceCount;
        for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
        {
            var deviceInfo = BassWasapi.GetDeviceInfo(deviceIndex);
            if (!deviceInfo.IsEnabled || !(deviceInfo.IsLoopback || deviceInfo.IsInput))
            {
                continue;
            }

            Log.Debug($"Found Wasapi input ID:{devices.Count} {deviceInfo.Name} "
                      + $"LoopBack:{deviceInfo.IsLoopback} IsInput:{deviceInfo.IsInput} (at {deviceIndex})");

            devices.Add(new AudioInputDevice
                            {
                                Name = deviceInfo.Name,
                                SampleRate = deviceInfo.MixFrequency,
                                IsLoopback = deviceInfo.IsLoopback,
                                BackendIndex = deviceIndex,
                                MinimumUpdatePeriod = deviceInfo.MinimumUpdatePeriod,
                            });
        }
    }

    public override bool TryStartCapture(AudioInputDevice device, AudioCaptureCallback onData)
    {
        AudioMixerManager.Initialize();

        // Held in a field so the delegate handed to native code is not collected while capture runs.
        _procedure = (buffer, length, _) =>
                     {
                         onData(buffer, length);
                         return length;
                     };

        BassWasapi.Stop();
        BassWasapi.Free();

        if (!BassWasapi.Init(device.BackendIndex,
                             Frequency: device.SampleRate,
                             Channels: 0,
                             Flags: WasapiInitFlags.Buffer,
                             Buffer: (float)device.MinimumUpdatePeriod * 4,
                             Period: (float)device.MinimumUpdatePeriod,
                             Procedure: _procedure,
                             User: IntPtr.Zero))
        {
            Log.Error("Can't initialize WASAPI:" + Bass.LastError);
            return false;
        }

        var info = BassWasapi.Info;
        _activeChannelCount = info.Channels > 0 ? info.Channels : 2;

        BassWasapi.Start();
        return true;
    }

    public override void StopCapture()
    {
        BassWasapi.Stop();
        BassWasapi.Free();
        _procedure = null;
    }

    public override int GetData(float[] buffer, int lengthOrFlags)
    {
        return BassWasapi.GetData(buffer, lengthOrFlags);
    }

    public override int GetLevel()
    {
        return BassWasapi.GetLevel();
    }

    private WasapiProcedure? _procedure;
    private int _activeChannelCount;
}
