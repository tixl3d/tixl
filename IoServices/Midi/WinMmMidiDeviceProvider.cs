#nullable enable
using System;
using System.Collections.Generic;
using NAudio;
using NAudio.Midi;
using T3.Core.Logging;

namespace T3.IoServices.Midi;

/// <summary>Windows MIDI through NAudio, which wraps WinMM.</summary>
internal sealed class WinMmMidiDeviceProvider : MidiDeviceProvider
{
    public override string Name => "WinMM";
    public override bool IsAvailable => true;

    public override void OpenDevices(Func<string, bool> shouldCapture,
                                     List<MidiInputDevice> inputs,
                                     List<MidiOutputDevice> outputs)
    {
        for (var index = 0; index < MidiIn.NumberOfDevices; index++)
        {
            var capabilities = MidiIn.DeviceInfo(index);
            var productName = capabilities.ProductName;
            if (!shouldCapture(productName))
            {
                continue;
            }

            try
            {
                inputs.Add(new WinMmMidiInputDevice(new MidiIn(index), new MidiDeviceInfo(productName, capabilities.ProductId)));
            }
            catch (MmException e)
            {
                Log.Error(e.Message == "MemoryAllocationError"
                              ? $" > '{productName}' is already being used by another application."
                              : $" > {e.Message} {productName}");
            }
        }

        for (var index = 0; index < MidiOut.NumberOfDevices; index++)
        {
            var capabilities = MidiOut.DeviceInfo(index);
            var productName = capabilities.ProductName;
            if (!shouldCapture(productName))
            {
                continue;
            }

            try
            {
                outputs.Add(new WinMmMidiOutputDevice(new MidiOut(index), new MidiDeviceInfo(productName, capabilities.ProductId)));
            }
            catch (MmException e)
            {
                Log.Error($" > {e.Message} {productName}");
            }
        }
    }

    public override void Shutdown()
    {
    }

    private sealed class WinMmMidiInputDevice : MidiInputDevice
    {
        public WinMmMidiInputDevice(MidiIn midiIn, MidiDeviceInfo info)
        {
            _midiIn = midiIn;
            Info = info;
            _midiIn.MessageReceived += OnMessageReceived;
            _midiIn.ErrorReceived += OnErrorReceived;
        }

        public override MidiDeviceInfo Info { get; }

        public override void Start()
        {
            _midiIn.Start();
        }

        public override void Stop()
        {
            _midiIn.Stop();
        }

        public override void Close()
        {
            _midiIn.MessageReceived -= OnMessageReceived;
            _midiIn.ErrorReceived -= OnErrorReceived;
            _midiIn.Close();
        }

        public override void Dispose()
        {
            _midiIn.Dispose();
        }

        // Re-raised rather than forwarded so consumers see this wrapper as the sender and can map it
        // back to a device.
        private void OnMessageReceived(object? sender, MidiInMessageEventArgs e)
        {
            RaiseMessageReceived(e.RawMessage, e.Timestamp);
        }

        private void OnErrorReceived(object? sender, MidiInMessageEventArgs e)
        {
            RaiseErrorReceived(e.RawMessage, e.Timestamp);
        }

        private readonly MidiIn _midiIn;
    }

    private sealed class WinMmMidiOutputDevice : MidiOutputDevice
    {
        public WinMmMidiOutputDevice(MidiOut midiOut, MidiDeviceInfo info)
        {
            _midiOut = midiOut;
            Info = info;
        }

        public override MidiDeviceInfo Info { get; }

        public override void Send(int shortMessage)
        {
            _midiOut.Send(shortMessage);
        }

        public override void SendBuffer(byte[] message)
        {
            _midiOut.SendBuffer(message);
        }

        public override void Close()
        {
            _midiOut.Close();
        }

        public override void Dispose()
        {
            _midiOut.Dispose();
        }

        private readonly MidiOut _midiOut;
    }
}
