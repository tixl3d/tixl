using System;
using System.Collections.Generic;
using NAudio.Midi;
using T3.Core.IO;
using T3.Core.Logging;

namespace T3.IoServices.Midi;

public static class MidiConnectionManager
{
    /// <summary>
    /// Tracks which MIDI devices are currently being controlled by compatible devices.
    /// When a device is in control mode, its messages should not be passed through to graph operators.
    /// </summary>
    private static readonly HashSet<string> _controlledDevices = new();
    
    /// <summary>
    /// Sets whether a MIDI device should block messages from being passed to graph operators.
    /// When true, the device is in "control mode" - messages are consumed by the compatible device.
    /// When false, the device is in "passthrough mode" - messages are passed to graph operators.
    /// </summary>
    public static void SetDeviceControlMode(string productName, bool controlMode)
    {
        if (controlMode)
        {
            if (_controlledDevices.Add(productName))
            {
                Log.Debug($"MIDI Device '{productName}' set to CONTROL mode (blocking passthrough)");
            }
        }
        else
        {
            if (_controlledDevices.Remove(productName))
            {
                Log.Debug($"MIDI Device '{productName}' set to PASSTHROUGH mode");
            }
        }
    }
    
    /// <summary>
    /// Checks if a MIDI device is currently being controlled and should block passthrough.
    /// </summary>
    public static bool IsDeviceInControlMode(string productName)
    {
        return _controlledDevices.Contains(productName);
    }
    
    /// <summary>
    /// Checks if a MIDI device is currently being controlled and should block passthrough.
    /// </summary>
    public static bool IsDeviceInControlMode(MidiInputDevice midiIn)
    {
        if (_devicesByMidiIn.TryGetValue(midiIn, out var device))
        {
            return _controlledDevices.Contains(device.ProductName);
        }
        return false;
    }
    
    public static void RegisterConsumer(IMidiConsumer consumer)
    {
        if (!Initialized)
        {
            ScanAndRegisterToMidiDevices();
            Initialized = true;
        }

        if (_midiInputConsumers.Contains(consumer))
        {
            Log.Warning("MidiConsumer was already added " + consumer);
            return;
        }

        _midiInputConsumers.Add(consumer);

        foreach (var midiInputDevice in _devicesByMidiIn.Keys)
        {
            midiInputDevice.MessageReceived -= consumer.MessageReceivedHandler;
            midiInputDevice.ErrorReceived -= consumer.ErrorReceivedHandler;
            midiInputDevice.MessageReceived += consumer.MessageReceivedHandler;
            midiInputDevice.ErrorReceived += consumer.ErrorReceivedHandler;
        }
    }

    public static bool Initialized { get; private set; }

    public static void UnregisterConsumer(IMidiConsumer consumer)
    {
        if (!_midiInputConsumers.Contains(consumer))
            return;

        foreach (var midiIn in _devicesByMidiIn.Keys)
        {
            midiIn.MessageReceived -= consumer.MessageReceivedHandler;
            midiIn.ErrorReceived -= consumer.ErrorReceivedHandler;
        }

        _midiInputConsumers.Remove(consumer);
        if (_midiInputConsumers.Count == 0)
        {
            CloseMidiDevices();
        }
    }

    public static void Rescan()
    {
        CloseMidiDevices();
        ScanAndRegisterToMidiDevices(logInformation: true);

        // TODO: Clean up later
        foreach (var consumer in _midiInputConsumers)
        {
            foreach (var midiInputDevice in _devicesByMidiIn.Keys)
            {
                midiInputDevice.MessageReceived -= consumer.MessageReceivedHandler;
                midiInputDevice.ErrorReceived -= consumer.ErrorReceivedHandler;
                midiInputDevice.MessageReceived += consumer.MessageReceivedHandler;
                midiInputDevice.ErrorReceived += consumer.ErrorReceivedHandler;
            }

            consumer.OnSettingsChanged();
        }
    }


    public interface IMidiConsumer
    {
        void MessageReceivedHandler(object sender, MidiInMessageEventArgs msg);
        void ErrorReceivedHandler(object sender, MidiInMessageEventArgs msg);

        /// <summary>
        /// This will be called if the number of controllers or devices changed and the
        /// listener should update its status.
        /// </summary>
        void OnSettingsChanged();
    }

    // FIXME: remove

    public static MidiDeviceInfo GetDescriptionForMidiIn(MidiInputDevice midiIn)
    {
        _devicesByMidiIn.TryGetValue(midiIn, out var description);
        return description;
    }

    public static bool TryGetMidiOut(string productName, out MidiOutputDevice midiOut)
    {
        midiOut = null;
        foreach (var (midi, device) in _midiOutsWithDevices)
        {
            if (device.ProductName != productName)
                continue;

            midiOut = midi;
            return true;
        }

        return false;
    }

    public static bool TryGetMidiIn(string productName, out MidiInputDevice midiIn)
    {
        midiIn = null;
        foreach (var (midiIn2, device) in _devicesByMidiIn)
        {
            if (device.ProductName != productName)
                continue;

            midiIn = midiIn2;
            return true;
        }

        return false;
    }

    /// <summary>
    /// For midi teaching, Tooll will capture all available midi devices.
    /// This will prevent other applications from capturing them. User's can
    /// prevent this in the settings.
    /// </summary>
    private static bool IsMidiDeviceCaptureEnabled(string deviceName)
    {
        var setting = CoreSettings.Config.LimitMidiDeviceCapture;
        if (string.IsNullOrEmpty(setting))
            return true;

        foreach (var s in setting.Split("\n"))
        {
            if (deviceName.Contains(s.Trim(), StringComparison.InvariantCultureIgnoreCase))
                return true;
        }

        return false;
    }

    private static void ScanAndRegisterToMidiDevices(bool logInformation = false)
    {
        var provider = MidiDeviceProvider.Current;
        if (!provider.IsAvailable)
        {
            Log.Debug("This system offers no MIDI devices.");
            return;
        }

        Log.Debug($"Capturing Midi devices via {provider.Name}...");
        if (!string.IsNullOrEmpty(CoreSettings.Config.LimitMidiDeviceCapture))
        {
            var settingsString = CoreSettings.Config.LimitMidiDeviceCapture.Replace("\n", "; ");
            Log.Debug($"NOTE: In settings Midi device capture is limited to '{settingsString}");
        }

        var openedInputs = new List<MidiInputDevice>();
        var openedOutputs = new List<MidiOutputDevice>();
        provider.OpenDevices(ShouldCapture, openedInputs, openedOutputs);

        foreach (var input in openedInputs)
        {
            if (logInformation)
            {
                Log.Debug($" listening to '{input.Info.ProductName}'...");
            }

            input.Start();
            _devicesByMidiIn[input] = input.Info;
        }

        foreach (var output in openedOutputs)
        {
            _midiOutsWithDevices[output] = output.Info;
        }

        bool ShouldCapture(string productName)
        {
            if (IsMidiDeviceCaptureEnabled(productName))
            {
                return true;
            }

            Log.Debug($" skipping '{productName}' (disabled in setting)");
            return false;
        }
    }

    private static void CloseMidiDevices()
    {
        // Midi Ins
        foreach (var midiInputDevice in _devicesByMidiIn.Keys)
        {
            foreach (var midiConsumer in _midiInputConsumers)
            {
                midiInputDevice.MessageReceived -= midiConsumer.MessageReceivedHandler;
                midiInputDevice.ErrorReceived -= midiConsumer.ErrorReceivedHandler;
            }

            try
            {
                midiInputDevice.Stop();
                midiInputDevice.Close();
                midiInputDevice.Dispose();
            }
            catch (Exception e)
            {
                Log.Debug("exception: " + e);
            }
        }

        _devicesByMidiIn.Clear();

        // Midi outs
        foreach (var midiOutputDevice in _midiOutsWithDevices.Keys)
        {
            try
            {
                midiOutputDevice.Close();
                midiOutputDevice.Dispose();
            }
            catch (Exception e)
            {
                Log.Debug("exception: " + e);
            }
        }

        _midiOutsWithDevices.Clear();

        // The ALSA backend keeps one sequencer client and a reading thread behind all of these; CoreMIDI keeps its client.
        MidiDeviceProvider.Current.Shutdown();
    }

    private static readonly List<IMidiConsumer> _midiInputConsumers = new();

    /// <summary>
    /// The opened input ports, keyed so a consumer can resolve the device an incoming message came from.
    /// </summary>
    public static IReadOnlyDictionary<MidiInputDevice, MidiDeviceInfo> MidiIns => _devicesByMidiIn;
    private static readonly Dictionary<MidiInputDevice, MidiDeviceInfo> _devicesByMidiIn = new();

    public static IReadOnlyDictionary<MidiOutputDevice, MidiDeviceInfo> MidiOutsWithDevices => _midiOutsWithDevices;
    private static readonly Dictionary<MidiOutputDevice, MidiDeviceInfo> _midiOutsWithDevices = new();
}