#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using T3.Core.Logging;
using static T3.IoServices.Midi.CoreMidiInterop;

namespace T3.IoServices.Midi;

/// <summary>macOS MIDI through CoreMIDI.</summary>
/// <remarks>
/// Like the ALSA sequencer, CoreMIDI gives one client with a single input port that connects to every source
/// it wants, so incoming packets are told apart by the reference passed when connecting. The client is kept for
/// the whole session: Apple advises against creating clients repeatedly, and a rescan only needs new
/// connections.
/// </remarks>
internal sealed class CoreMidiDeviceProvider : MidiDeviceProvider
{
    public override string Name => "CoreMIDI";

    public override bool IsAvailable
    {
        get
        {
            _availability ??= TryCreateClient();
            return _availability.Value;
        }
    }

    public override void OpenDevices(Func<string, bool> shouldCapture,
                                     List<MidiInputDevice> inputs,
                                     List<MidiOutputDevice> outputs)
    {
        if (!IsAvailable)
            return;

        var sourceCount = (int)MIDIGetNumberOfSources();
        for (var index = 0; index < sourceCount; index++)
        {
            var source = MIDIGetSource((UIntPtr)index);
            var productName = GetDisplayName(source);
            if (source == 0 || !shouldCapture(productName))
                continue;

            inputs.Add(new CoreMidiInputDevice(this, source, new MidiDeviceInfo(productName)));
        }

        var destinationCount = (int)MIDIGetNumberOfDestinations();
        for (var index = 0; index < destinationCount; index++)
        {
            var destination = MIDIGetDestination((UIntPtr)index);
            var productName = GetDisplayName(destination);
            if (destination == 0 || !shouldCapture(productName))
                continue;

            outputs.Add(new CoreMidiOutputDevice(this, destination, new MidiDeviceInfo(productName)));
        }
    }

    /// <summary>The devices disconnect themselves when closed; the client stays for the next scan.</summary>
    public override void Shutdown()
    {
    }

    private bool TryCreateClient()
    {
        try
        {
            var clientName = CreateCfString(ClientName);
            var inputName = CreateCfString(ClientName + " in");
            var outputName = CreateCfString(ClientName + " out");
            try
            {
                if (MIDIClientCreate(clientName, IntPtr.Zero, IntPtr.Zero, out _client) != 0)
                {
                    Log.Debug("Could not create a CoreMIDI client; MIDI devices will not be found.");
                    return false;
                }

                // CoreMIDI keeps calling this pointer for as long as the port exists, so the delegate behind it
                // must not be collected.
                _readProc = OnPacketsReceived;
                if (MIDIInputPortCreate(_client, inputName, Marshal.GetFunctionPointerForDelegate(_readProc), IntPtr.Zero, out _inputPort) != 0
                    || MIDIOutputPortCreate(_client, outputName, out _outputPort) != 0)
                {
                    Log.Debug("Could not create CoreMIDI ports; MIDI devices will not be found.");
                    MIDIClientDispose(_client);
                    return false;
                }
            }
            finally
            {
                CFRelease(clientName);
                CFRelease(inputName);
                CFRelease(outputName);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Debug($"CoreMIDI is not available: {e.Message}");
            return false;
        }

        return true;
    }

    private static string GetDisplayName(uint endpoint)
    {
        if (MIDIObjectGetStringProperty(endpoint, PropertyDisplayName, out var name) != 0 || name == IntPtr.Zero)
            return $"MIDI {endpoint}";

        try
        {
            return ReadCfString(name);
        }
        finally
        {
            CFRelease(name);
        }
    }

    private void Connect(CoreMidiInputDevice device)
    {
        // The reference CoreMIDI hands back with each packet: a key into the connected devices, not a pointer.
        var key = Interlocked.Increment(ref _lastConnectionKey);
        lock (_inputsByKey)
        {
            _inputsByKey[key] = device;
        }

        if (MIDIPortConnectSource(_inputPort, device.Source, key) != 0)
        {
            Log.Warning($"Could not connect to MIDI source {device.Info.ProductName}.");
            lock (_inputsByKey)
            {
                _inputsByKey.Remove(key);
            }

            return;
        }

        device.ConnectionKey = key;
    }

    private void Disconnect(CoreMidiInputDevice device)
    {
        MIDIPortDisconnectSource(_inputPort, device.Source);
        lock (_inputsByKey)
        {
            _inputsByKey.Remove(device.ConnectionKey);
        }

        device.ConnectionKey = 0;
    }

    /// <summary>Runs on CoreMIDI's thread. A packet list can hold several packets, each with several messages.</summary>
    private void OnPacketsReceived(IntPtr packetList, IntPtr readProcRefCon, IntPtr sourceConnectionRefCon)
    {
        CoreMidiInputDevice? device;
        lock (_inputsByKey)
        {
            if (!_inputsByKey.TryGetValue((int)sourceConnectionRefCon, out device))
                return;
        }

        var packetCount = Marshal.ReadInt32(packetList);
        var packet = packetList + PacketListHeaderSize;
        for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
        {
            var length = (ushort)Marshal.ReadInt16(packet, PacketLengthOffset);
            for (var byteIndex = 0; byteIndex < length; byteIndex++)
            {
                device.Parse(Marshal.ReadByte(packet, PacketDataOffset + byteIndex));
            }

            // Packets follow each other at 4-byte boundaries on Apple Silicon (and back to back on Intel, where
            // CoreMIDI accepts the aligned form as well).
            packet = (IntPtr)(((long)packet + PacketDataOffset + length + 3) & ~3L);
        }
    }

    private void SendTo(uint destination, byte[] message, int length)
    {
        if (length <= 0 || length > ushort.MaxValue)
            return;

        lock (_outputLock)
        {
            var requiredSize = PacketListHeaderSize + PacketDataOffset + length;
            if (_packetBuffer == IntPtr.Zero || _packetBufferSize < requiredSize)
            {
                if (_packetBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(_packetBuffer);

                _packetBufferSize = Math.Max(requiredSize, 256);
                _packetBuffer = Marshal.AllocHGlobal(_packetBufferSize);
            }

            var packet = _packetBuffer + PacketListHeaderSize;
            Marshal.WriteInt32(_packetBuffer, 1);
            Marshal.WriteInt64(packet, PacketTimeStampOffset, 0); // 0: send now
            Marshal.WriteInt16(packet, PacketLengthOffset, (short)length);
            Marshal.Copy(message, 0, packet + PacketDataOffset, length);
            MIDISend(_outputPort, destination, _packetBuffer);
        }
    }

    private sealed class CoreMidiInputDevice : MidiInputDevice
    {
        public CoreMidiInputDevice(CoreMidiDeviceProvider provider, uint source, MidiDeviceInfo info)
        {
            _provider = provider;
            Source = source;
            Info = info;
        }

        public override MidiDeviceInfo Info { get; }
        internal uint Source { get; }
        internal int ConnectionKey { get; set; }

        public override void Start()
        {
            if (ConnectionKey == 0)
                _provider.Connect(this);
        }

        public override void Stop()
        {
            if (ConnectionKey != 0)
                _provider.Disconnect(this);
        }

        public override void Close() => Stop();
        public override void Dispose() => Stop();

        /// <summary>
        /// Collects bytes into short messages and dispatches them, matching what the WinMM path delivers. Keeps
        /// running status, which some devices use, and skips sysex, which nothing consumes as input. Only
        /// CoreMIDI's thread calls this.
        /// </summary>
        internal void Parse(byte value)
        {
            if (value >= 0xF8)
            {
                // Real-time messages (clock, start, stop) are one byte and may appear anywhere, even inside sysex.
                RaiseMessageReceived(value, Environment.TickCount);
                return;
            }

            if (value >= 0x80)
            {
                _isInSysex = value == 0xF0;
                _dataCount = 0;
                _expectedDataCount = GetDataLength(value);

                if (value < 0xF0)
                {
                    _runningStatus = value;
                    _pendingStatus = value;
                    return;
                }

                // System messages cancel running status. Sysex start and end carry nothing to dispatch.
                _runningStatus = 0;
                _pendingStatus = 0;
                if (value is 0xF0 or 0xF7)
                    return;

                if (_expectedDataCount == 0)
                {
                    RaiseMessageReceived(value, Environment.TickCount);
                    return;
                }

                _pendingStatus = value;
                return;
            }

            if (_isInSysex)
                return;

            if (_pendingStatus == 0)
            {
                // A data byte without a status byte reuses the last channel status.
                if (_runningStatus == 0)
                    return;

                _pendingStatus = _runningStatus;
                _expectedDataCount = GetDataLength(_runningStatus);
            }

            _data[_dataCount++] = value;
            if (_dataCount < _expectedDataCount)
                return;

            var rawMessage = _pendingStatus | (_data[0] << 8) | (_expectedDataCount > 1 ? _data[1] << 16 : 0);
            RaiseMessageReceived(rawMessage, Environment.TickCount);
            _dataCount = 0;
            _pendingStatus = _runningStatus;
        }

        private static int GetDataLength(byte status)
        {
            return status switch
                       {
                           < 0xC0 => 2,  // note off/on, poly pressure, control change
                           < 0xE0 => 1,  // program change, channel pressure
                           < 0xF0 => 2,  // pitch bend
                           0xF1 or 0xF3 => 1,
                           0xF2 => 2,
                           _ => 0,
                       };
        }

        private readonly CoreMidiDeviceProvider _provider;
        private readonly byte[] _data = new byte[2];
        private byte _runningStatus;
        private byte _pendingStatus;
        private int _dataCount;
        private int _expectedDataCount;
        private bool _isInSysex;
    }

    private sealed class CoreMidiOutputDevice : MidiOutputDevice
    {
        public CoreMidiOutputDevice(CoreMidiDeviceProvider provider, uint destination, MidiDeviceInfo info)
        {
            _provider = provider;
            _destination = destination;
            Info = info;
        }

        public override MidiDeviceInfo Info { get; }

        public override void Send(int shortMessage)
        {
            var status = (byte)(shortMessage & 0xFF);
            _shortMessageBuffer[0] = status;
            _shortMessageBuffer[1] = (byte)((shortMessage >> 8) & 0x7F);
            _shortMessageBuffer[2] = (byte)((shortMessage >> 16) & 0x7F);
            _provider.SendTo(_destination, _shortMessageBuffer, GetMessageLength(status));
        }

        public override void SendBuffer(byte[] message)
        {
            _provider.SendTo(_destination, message, message.Length);
        }

        public override void Close()
        {
        }

        public override void Dispose()
        {
        }

        /// <summary>Program change and channel pressure carry one data byte; every other voice message carries two.</summary>
        private static int GetMessageLength(byte status)
        {
            var kind = status & 0xF0;
            return kind is 0xC0 or 0xD0 ? 2 : 3;
        }

        private readonly CoreMidiDeviceProvider _provider;
        private readonly uint _destination;
        private readonly byte[] _shortMessageBuffer = new byte[3];
    }

    /** How the editor shows up in other applications' port lists. */
    private const string ClientName = "TiXL";

    private uint _client;
    private uint _inputPort;
    private uint _outputPort;
    private bool? _availability;
    private MidiReadProc? _readProc;
    private int _lastConnectionKey;
    private IntPtr _packetBuffer;
    private int _packetBufferSize;
    private readonly object _outputLock = new();
    private readonly Dictionary<int, CoreMidiInputDevice> _inputsByKey = new();
}
