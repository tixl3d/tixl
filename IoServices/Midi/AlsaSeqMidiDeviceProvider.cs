#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using T3.Core.Logging;
using static T3.IoServices.Midi.AlsaSeqInterop;

namespace T3.IoServices.Midi;

/// <summary>Linux MIDI through the ALSA sequencer.</summary>
/// <remarks>
/// Unlike WinMM, where each port is opened on its own, ALSA gives one client that owns a single input
/// and output port and subscribes them to the ports it wants. So the connection, the parser and the
/// reading thread are shared here, and incoming events are dispatched by their source address.
/// </remarks>
internal sealed class AlsaSeqMidiDeviceProvider : MidiDeviceProvider
{
    /// <summary>A decoded short message waiting for the consumers, keyed by the port it came from.</summary>
    private readonly record struct PendingMessage(AlsaMidiInputDevice Device, int RawMessage, int Timestamp);

    public override string Name => "ALSA sequencer";

    public override bool IsAvailable
    {
        get
        {
            if (_availability == null)
            {
                _availability = TryOpenClient();
            }

            return _availability.Value;
        }
    }

    public override void OpenDevices(Func<string, bool> shouldCapture,
                                     List<MidiInputDevice> inputs,
                                     List<MidiOutputDevice> outputs)
    {
        if (!IsAvailable)
        {
            return;
        }

        snd_seq_client_info_malloc(out var clientInfo);
        snd_seq_port_info_malloc(out var portInfo);
        try
        {
            snd_seq_client_info_set_client(clientInfo, -1);
            while (snd_seq_query_next_client(_seq, clientInfo) >= 0)
            {
                var client = snd_seq_client_info_get_client(clientInfo);

                // Our own ports would echo back, and the System client only carries the timer and the
                // port-change announcements.
                if (client == _clientId || client == SystemClientId)
                {
                    continue;
                }

                var clientName = ReadString(snd_seq_client_info_get_name(clientInfo));
                snd_seq_port_info_set_client(portInfo, client);
                snd_seq_port_info_set_port(portInfo, -1);

                while (snd_seq_query_next_port(_seq, portInfo) >= 0)
                {
                    var capability = snd_seq_port_info_get_capability(portInfo);
                    var port = snd_seq_port_info_get_port(portInfo);
                    var productName = BuildProductName(clientName, ReadString(snd_seq_port_info_get_name(portInfo)));

                    if (!shouldCapture(productName))
                    {
                        continue;
                    }

                    // "Readable" is from the port's side: it is a source we can subscribe to, i.e. an input.
                    if ((capability & (CapRead | CapSubsRead)) == (CapRead | CapSubsRead))
                    {
                        inputs.Add(new AlsaMidiInputDevice(this, client, port, new MidiDeviceInfo(productName)));
                    }

                    if ((capability & (CapWrite | CapSubsWrite)) == (CapWrite | CapSubsWrite))
                    {
                        outputs.Add(new AlsaMidiOutputDevice(this, client, port, new MidiDeviceInfo(productName)));
                    }
                }
            }
        }
        finally
        {
            snd_seq_port_info_free(portInfo);
            snd_seq_client_info_free(clientInfo);
        }
    }

    public override void Shutdown()
    {
        if (_seq == IntPtr.Zero)
        {
            return;
        }

        _isShuttingDown = true;
        WakeReader();
        _readerThread?.Join(TimeSpan.FromSeconds(1));
        _readerThread = null;

        _pendingMessages?.CompleteAdding();
        _dispatchThread?.Join(TimeSpan.FromSeconds(1));
        _dispatchThread = null;
        _pendingMessages?.Dispose();
        _pendingMessages = null;

        if (_encoder != IntPtr.Zero)
        {
            snd_midi_event_free(_encoder);
            _encoder = IntPtr.Zero;
        }

        if (_decoder != IntPtr.Zero)
        {
            snd_midi_event_free(_decoder);
            _decoder = IntPtr.Zero;
        }

        snd_seq_close(_seq);
        _seq = IntPtr.Zero;
        _availability = null;
        _isShuttingDown = false;
    }

    /// <summary>
    /// ALSA names a port for its device; only prefix the client when that is not already the case.
    /// The kernel's default port is "&lt;client&gt; MIDI 1", which is reported as the bare client name so it
    /// matches the name WinMM reports - compatible controller classes and projects saved on Windows rely on it.
    /// </summary>
    private static string BuildProductName(string clientName, string portName)
    {
        if (string.IsNullOrEmpty(portName) || portName == clientName + DefaultPortSuffix)
        {
            return clientName;
        }

        return portName.Contains(clientName, StringComparison.Ordinal) ? portName : $"{clientName} {portName}";
    }

    private bool TryOpenClient()
    {
        try
        {
            if (snd_seq_open(out _seq, "default", OpenDuplex, 0) < 0)
            {
                Log.Debug("No ALSA sequencer available; MIDI devices will not be found.");
                _seq = IntPtr.Zero;
                return false;
            }
        }
        catch (DllNotFoundException)
        {
            Log.Debug("libasound is not installed; MIDI devices will not be found.");
            return false;
        }

        snd_seq_set_client_name(_seq, ClientName);
        _clientId = snd_seq_client_id(_seq);

        // The default pool of 200 events overruns within a fraction of a second when a network peer floods
        // its port, e.g. with another editor's LED feedback relayed by rtpmidid.
        snd_seq_set_client_pool_input(_seq, (UIntPtr)MaxClientInputPoolSize);

        const uint portType = TypeMidiGeneric | TypeApplication;
        _inputPort = snd_seq_create_simple_port(_seq, ClientName + " in", CapWrite | CapSubsWrite, portType);
        _outputPort = snd_seq_create_simple_port(_seq, ClientName + " out", CapRead | CapSubsRead, portType);

        // Delivered to the reading thread like any input, so detecting device changes costs no extra thread.
        if (snd_seq_connect_from(_seq, _inputPort, SystemClientId, AnnouncePort) < 0)
        {
            Log.Debug("Could not subscribe to ALSA announcements; connected MIDI devices will not be detected.");
        }

        // Running status would carry decoder state between two devices sharing this parser.
        snd_midi_event_new((UIntPtr)MessageBufferSize, out _decoder);
        snd_midi_event_init(_decoder);
        snd_midi_event_no_status(_decoder, 1);

        snd_midi_event_new((UIntPtr)MessageBufferSize, out _encoder);
        snd_midi_event_init(_encoder);
        snd_midi_event_no_status(_encoder, 1);

        _pendingMessages = new BlockingCollection<PendingMessage>(PendingMessageCapacity);
        _dispatchThread = new Thread(DispatchPendingMessages) { IsBackground = true, Name = "ALSA MIDI dispatch" };
        _dispatchThread.Start();

        _readerThread = new Thread(ReadIncomingEvents) { IsBackground = true, Name = "ALSA MIDI in" };
        _readerThread.Start();
        return true;
    }

    /// <summary>
    /// Only drains the sequencer. A network peer can deliver thousands of events within milliseconds, and
    /// the kernel queue holds at most <see cref="MaxClientInputPoolSize"/>, so running the consumers here
    /// would overrun it; they get the messages from <see cref="DispatchPendingMessages"/> instead.
    /// </summary>

    private void ReadIncomingEvents()
    {
        var buffer = new byte[MessageBufferSize];
        var overrunCount = 0;
        while (!_isShuttingDown)
        {
            var result = snd_seq_event_input(_seq, out var eventPointer);
            if (_isShuttingDown)
            {
                break;
            }

            if (result < 0)
            {
                if (result == ErrorNoSpace)
                {
                    // The kernel reports an input overrun once, after dropping the queued events; reading continues.
                    if (overrunCount++ == 0)
                    {
                        Log.Warning("MIDI input overrun: events arrived faster than they were consumed and were dropped.");
                    }

                    continue;
                }

                if (result is ErrorInterrupted or ErrorTryAgain)
                {
                    continue;
                }

                Log.Warning($"MIDI input stopped: ALSA sequencer error {result}. Rescan the devices to restart it.");
                break;
            }

            var sequencerEvent = Marshal.PtrToStructure<SeqEvent>(eventPointer);
            if (sequencerEvent.SourceClient == SystemClientId)
            {
                if (IsDeviceChange(sequencerEvent))
                {
                    ReportDeviceChange();
                }

                continue;
            }

            AlsaMidiInputDevice? device;
            lock (_inputsByAddress)
            {
                if (!_inputsByAddress.TryGetValue(ToAddressKey(sequencerEvent.SourceClient, sequencerEvent.SourcePort), out device))
                {
                    continue;
                }
            }

            int byteCount;
            lock (_decoderLock)
            {
                byteCount = (int)snd_midi_event_decode(_decoder, buffer, buffer.Length, eventPointer);
            }

            // Short messages only, matching what the WinMM path delivers; nothing consumes sysex input.
            if (byteCount is < 1 or > 3)
            {
                continue;
            }

            var rawMessage = buffer[0]
                             | (byteCount > 1 ? buffer[1] << 8 : 0)
                             | (byteCount > 2 ? buffer[2] << 16 : 0);
            QueuePendingMessage(new PendingMessage(device, rawMessage, Environment.TickCount));
        }
    }

    private void QueuePendingMessage(PendingMessage message)
    {
        var pending = _pendingMessages;
        if (pending == null)
        {
            return;
        }

        try
        {
            if (!pending.TryAdd(message) && _droppedPendingMessageCount++ == 0)
            {
                Log.Warning("MIDI consumers are too slow: incoming messages are being dropped.");
            }
        }
        catch (InvalidOperationException)
        {
            // Shutdown completed the collection while this thread was still draining.
        }
    }

    private void DispatchPendingMessages()
    {
        var pending = _pendingMessages;
        if (pending == null)
        {
            return;
        }

        try
        {
            foreach (var message in pending.GetConsumingEnumerable())
            {
                message.Device.DispatchMessage(message.RawMessage, message.Timestamp);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Unblocks the reading thread, which is otherwise parked in <c>snd_seq_event_input</c>.</summary>
    private void WakeReader()
    {
        var wakeEvent = new SeqEvent
                            {
                                Type = 10, // SND_SEQ_EVENT_CONTROLLER - any deliverable event will do
                                Queue = QueueDirect,
                                SourcePort = (byte)_outputPort,
                                DestClient = (byte)_clientId,
                                DestPort = (byte)_inputPort,
                            };

        lock (_outputLock)
        {
            snd_seq_event_output_direct(_seq, ref wakeEvent);
            snd_seq_drain_output(_seq);
        }
    }

    /** TiXL's own client is left out, so reopening it on a rescan does not trigger the next rescan. */
    private bool IsDeviceChange(SeqEvent announcement)
    {
        return announcement.Type is >= EventClientStart and <= EventPortChange
               && announcement.AnnouncedClient != _clientId;
    }

    private static int ToAddressKey(byte client, byte port)
    {
        return (client << 8) | port;
    }

    private void Subscribe(AlsaMidiInputDevice device, int client, int port)
    {
        if (snd_seq_connect_from(_seq, _inputPort, client, port) < 0)
        {
            Log.Warning($"Could not subscribe to MIDI port {client}:{port} ({device.Info.ProductName}).");
            return;
        }

        lock (_inputsByAddress)
        {
            _inputsByAddress[ToAddressKey((byte)client, (byte)port)] = device;
        }
    }

    private void Unsubscribe(int client, int port)
    {
        lock (_inputsByAddress)
        {
            _inputsByAddress.Remove(ToAddressKey((byte)client, (byte)port));
        }

        snd_seq_disconnect_from(_seq, _inputPort, client, port);
    }

    /// <summary>
    /// The kernel's rawmidi bridge opens a hardware port's output stream only for subscribers; events
    /// addressed to an unsubscribed port are rejected with ENODEV, so sending requires this subscription.
    /// </summary>
    private bool SubscribeOutput(int client, int port, string productName)
    {
        if (snd_seq_connect_to(_seq, _outputPort, client, port) < 0)
        {
            Log.Warning($"Could not subscribe to MIDI output port {client}:{port} ({productName}).");
            return false;
        }

        return true;
    }

    private void UnsubscribeOutput(int client, int port)
    {
        if (_seq == IntPtr.Zero)
        {
            return;
        }

        snd_seq_disconnect_to(_seq, _outputPort, client, port);
    }

    private void SendTo(int client, int port, byte[] message, int length)
    {
        if (_seq == IntPtr.Zero || length <= 0)
        {
            return;
        }

        // A sysex event points at this buffer rather than copying it, so it has to stay put until sent.
        var pinned = GCHandle.Alloc(message, GCHandleType.Pinned);
        try
        {
            var sequencerEvent = new SeqEvent();
            lock (_outputLock)
            {
                if (snd_midi_event_encode(_encoder, message, length, ref sequencerEvent) <= 0)
                {
                    return;
                }

                sequencerEvent.SourcePort = (byte)_outputPort;
                sequencerEvent.DestClient = (byte)client;
                sequencerEvent.DestPort = (byte)port;
                sequencerEvent.Queue = QueueDirect;

                if (sequencerEvent.Type == EventSysex)
                {
                    sequencerEvent.ExtLen = (uint)length;
                    sequencerEvent.ExtPtr = pinned.AddrOfPinnedObject();
                }

                snd_seq_event_output_direct(_seq, ref sequencerEvent);
                snd_seq_drain_output(_seq);
            }
        }
        finally
        {
            pinned.Free();
        }
    }

    private sealed class AlsaMidiInputDevice : MidiInputDevice
    {
        public AlsaMidiInputDevice(AlsaSeqMidiDeviceProvider provider, int client, int port, MidiDeviceInfo info)
        {
            _provider = provider;
            _client = client;
            _port = port;
            Info = info;
        }

        public override MidiDeviceInfo Info { get; }

        public override void Start()
        {
            if (_isStarted)
            {
                return;
            }

            _provider.Subscribe(this, _client, _port);
            _isStarted = true;
        }

        public override void Stop()
        {
            if (!_isStarted)
            {
                return;
            }

            _provider.Unsubscribe(_client, _port);
            _isStarted = false;
        }

        public override void Close()
        {
            Stop();
        }

        public override void Dispose()
        {
            Stop();
        }

        internal void DispatchMessage(int rawMessage, int timestamp)
        {
            RaiseMessageReceived(rawMessage, timestamp);
        }

        private readonly AlsaSeqMidiDeviceProvider _provider;
        private readonly int _client;
        private readonly int _port;
        private bool _isStarted;
    }

    private sealed class AlsaMidiOutputDevice : MidiOutputDevice
    {
        public AlsaMidiOutputDevice(AlsaSeqMidiDeviceProvider provider, int client, int port, MidiDeviceInfo info)
        {
            _provider = provider;
            _client = client;
            _port = port;
            Info = info;
            _isSubscribed = provider.SubscribeOutput(client, port, info.ProductName);
        }

        public override MidiDeviceInfo Info { get; }

        public override void Send(int shortMessage)
        {
            var status = (byte)(shortMessage & 0xFF);
            _shortMessageBuffer[0] = status;
            _shortMessageBuffer[1] = (byte)((shortMessage >> 8) & 0x7F);
            _shortMessageBuffer[2] = (byte)((shortMessage >> 16) & 0x7F);
            _provider.SendTo(_client, _port, _shortMessageBuffer, GetMessageLength(status));
        }

        public override void SendBuffer(byte[] message)
        {
            _provider.SendTo(_client, _port, message, message.Length);
        }

        public override void Close()
        {
            if (!_isSubscribed)
            {
                return;
            }

            _provider.UnsubscribeOutput(_client, _port);
            _isSubscribed = false;
        }

        public override void Dispose()
        {
            Close();
        }

        /// <summary>Program change and channel pressure carry one data byte; every other voice message carries two.</summary>
        private static int GetMessageLength(byte status)
        {
            var kind = status & 0xF0;
            return kind is 0xC0 or 0xD0 ? 2 : 3;
        }

        private readonly AlsaSeqMidiDeviceProvider _provider;
        private readonly int _client;
        private readonly int _port;
        private readonly byte[] _shortMessageBuffer = new byte[3];
        private bool _isSubscribed;
    }

    /** How the editor shows up in aconnect and in other applications' port lists. */
    private const string ClientName = "TiXL";

    /** The kernel's port name for a USB MIDI device's first port, appended to the client name. */
    private const string DefaultPortSuffix = " MIDI 1";

    /** Large enough for the longest sysex a controller sends for its pad colours. */
    private const int MessageBufferSize = 1024;

    /** Tens of seconds of a flooding peer; beyond that the consumers cannot be keeping up at all. */
    private const int PendingMessageCapacity = 1 << 16;

    private IntPtr _seq;
    private IntPtr _decoder;
    private IntPtr _encoder;
    private int _clientId;
    private int _inputPort = -1;
    private int _outputPort = -1;
    private bool? _availability;
    private volatile bool _isShuttingDown;
    private Thread? _readerThread;
    private Thread? _dispatchThread;
    private BlockingCollection<PendingMessage>? _pendingMessages;
    private int _droppedPendingMessageCount;
    private readonly object _decoderLock = new();
    private readonly object _outputLock = new();
    private readonly Dictionary<int, AlsaMidiInputDevice> _inputsByAddress = new();
}
