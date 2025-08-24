using System;
using System.Collections.Generic;
using System.Linq;
using ManagedBass;
using ManagedBass.Wasapi;
using T3.Core.Animation;
using T3.Core.Logging;
using T3.Core.Operator;

namespace T3.Core.Audio;

/// <summary>
/// Uses the windows Wasapi audio API to get audio reaction from devices like speakers and microphones
/// </summary>
public static class WasapiAudioInput
{
    /// <summary>
    /// Needs to be called once a frame.
    /// It switches audio input device if required
    /// </summary>
    public static void StartFrame(PlaybackSettings settings)
    {
        if (settings == null)
            return;
                    
        if (settings.AudioSource != PlaybackSettings.AudioSources.ExternalDevice)
        {
            if (!string.IsNullOrEmpty(ActiveInputDeviceName))
            {
                Stop();
            }
            return ;
        }

        var deviceName = settings.AudioInputDeviceName;
        if (ActiveInputDeviceName == deviceName)
        {
            // Try to restart capture
            if(!_failedToGetLastFffData)
                return;

            Log.Debug("Trying to restart WASAPI...");
            _failedToGetLastFffData = false;
        }
            
        if (string.IsNullOrEmpty(deviceName))
        {
            if (_complainedOnce)
                return ;
                
            Log.Warning("Can't switch to WASAPI device without a name");
            _complainedOnce = true;
            return ;
        }


        var device = InputDevices.FirstOrDefault(d => d.DeviceInfo.Name == deviceName);
        if (device == null)
        {
            Log.Warning($"Can't find input device {deviceName}");
            _complainedOnce = true;
            return ;
        }

        StartInputCapture(device);
        _complainedOnce = false;
    }


    public static List<WasapiInputDevice> InputDevices
    {
        get
        {
            if (_inputDevices == null)
                InitializeInputDeviceList();

            return _inputDevices;
        }
    }



    /// <summary>
    /// If device is null we will attempt default input index
    /// </summary>
    private static void StartInputCapture(WasapiInputDevice device)
    {
        var inputDeviceIndex = BassWasapi.DefaultInputDevice;

        if (device == null)
        {
            if (_inputDevices.Count == 0)
            {
                Log.Error("No wasapi input devices found");
                return;
            }

            Log.Error($"Attempting default input {BassWasapi.DefaultInputDevice}.");
            device = _inputDevices[0];
        }
        else
        {
            Log.Info($"Initializing WASAPI audio input for  {device.DeviceInfo.Name}... ");
            inputDeviceIndex = device.WasapiDeviceIndex;
        }

        SampleRate = device.DeviceInfo.MixFrequency;

        BassWasapi.Stop();
        BassWasapi.Free();
        if (!BassWasapi.Init(inputDeviceIndex,
                             Frequency: device.DeviceInfo.MixFrequency,
                             Channels: 0,
                             //Flags: WasapiInitFlags.Buffer | WasapiInitFlags.Exclusive,
                             Flags: WasapiInitFlags.Buffer,
                             Buffer: (float)device.DeviceInfo.MinimumUpdatePeriod*4,
                             Period: (float)device.DeviceInfo.MinimumUpdatePeriod,
                             Procedure: ProcessDataCallback,
                             User: IntPtr.Zero))
        {
            Log.Error("Can't initialize WASAPI:" + Bass.LastError);
            return;
        }

        ActiveInputDeviceName = device.DeviceInfo.Name;
        BassWasapi.Start();
    }
        
    private static void Stop()
    {
        //Log.Debug("Wasapi.Stop()");
        BassWasapi.Stop();
        BassWasapi.Free();
        ActiveInputDeviceName = null;
    }

    private static bool _complainedOnce;

        
    private static void InitializeInputDeviceList()
    {
        _inputDevices = new List<WasapiInputDevice>();

        // Keep in local variable to avoid double evaluation
        var deviceCount = BassWasapi.DeviceCount;

        for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
        {
            var deviceInfo = BassWasapi.GetDeviceInfo(deviceIndex);
            var isValidInputDevice = deviceInfo.IsEnabled && (deviceInfo.IsLoopback || deviceInfo.IsInput);

            if (!isValidInputDevice)
                continue;

            Log.Debug($"Found Wasapi input ID:{_inputDevices.Count} {deviceInfo.Name} LoopBack:{deviceInfo.IsLoopback} IsInput:{deviceInfo.IsInput} (at {deviceIndex})");
            _inputDevices.Add(new WasapiInputDevice()
                                  {
                                      WasapiDeviceIndex = deviceIndex,
                                      DeviceInfo = deviceInfo,
                                  });
        }
    }

    /// <summary>
    /// This is call async (potentially several times per frame) whenever new
    /// audio-data arrives
    /// </summary>
    private static int ProcessDataCallback(IntPtr buffer, int length, IntPtr user)
    {
        var time = Playback.RunTimeInSecs;  // Keep because timer is still running 
        TimeSinceLastUpdate = time - LastUpdateTime;
        LastUpdateTime = time;

        var resultCode = BassWasapi.GetData(AudioAnalysis.FftGainBuffer, (int)(AudioAnalysis.BassFlagForFftBufferSize | DataFlags.FFTRemoveDC));
        _failedToGetLastFffData = resultCode < 0;
        if (_failedToGetLastFffData)
        {
            Log.Debug($"Can't get Wasapi FFT-Data: {Bass.LastError}");
            return length;
        }
        
        var level = BassWasapi.GetLevel();
        _lastAudioLevel = (float)(level * 0.00001);

        var playbackSettings = Playback.Current?.Settings;
        if (playbackSettings == null) 
            return length;
        
        
        AudioAnalysis.ProcessUpdate(playbackSettings?.AudioGainFactor?? 1,
                                    playbackSettings?.AudioDecayFactor?? 0.9f);

        if (playbackSettings.EnableAudioBeatLocking)
        {
            BeatSynchronizer.UpdateBeatTimer();
        }
        
        return length;
    }

    private static bool _failedToGetLastFffData;

    public sealed class WasapiInputDevice
    {
        internal int WasapiDeviceIndex;
        public WasapiDeviceInfo DeviceInfo;
    }

    private static List<WasapiInputDevice> _inputDevices;
    private static readonly float[] _fftIntermediate = new float[AudioAnalysis.FftBufferSize];
    public static double TimeSinceLastUpdate;
    public static double LastUpdateTime;
    internal static long SampleRate = 48000;

    public static string ActiveInputDeviceName { get; private set; }
    private static float _lastAudioLevel;
    
    /// <summary>
    /// This is only used of the gain meter in the playback settings dialog.
    /// </summary>
    public static float DecayingAudioLevel => (float)(_lastAudioLevel / Math.Max(1, (Playback.RunTimeInSecs - LastUpdateTime) * 100));
}
