#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        ListenForDeviceChanges();

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

    /// <summary>The registration stays for the whole session, like the CoreMIDI client.</summary>
    public override void Shutdown()
    {
    }

    /// <summary>
    /// WinMM has no device-change notification, so this listens for Plug and Play arrivals and removals of
    /// audio interfaces, which USB MIDI devices register as.
    /// </summary>
    private void ListenForDeviceChanges()
    {
        if (_hasTriedListening)
            return;

        _hasTriedListening = true;
        var filter = new DeviceNotificationFilter
                         {
                             Size = (uint)Marshal.SizeOf<DeviceNotificationFilter>(),
                             FilterType = FilterTypeDeviceInterface,
                             InterfaceClass = AudioInterfaceClass,
                         };

        // Windows calls this pointer until the registration is removed, so the delegate must not be collected.
        _notificationCallback = OnDeviceNotification;
        try
        {
            if (CM_Register_Notification(ref filter, IntPtr.Zero, _notificationCallback, out _) != 0)
                Log.Debug("Could not register for device notifications; connected MIDI devices will not be detected.");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Debug($"Device notifications are not available: {e.Message}");
        }
    }

    /** Runs on a thread-pool thread. */
    private uint OnDeviceNotification(IntPtr notification, IntPtr context, int action, IntPtr eventData, uint eventDataSize)
    {
        if (action is ActionInterfaceArrival or ActionInterfaceRemoval)
            ReportDeviceChange();

        return 0;
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

    /// <summary><c>CM_NOTIFY_FILTER</c>; the union after the header is sized by its largest member, a device instance id.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 416)]
    private struct DeviceNotificationFilter
    {
        [FieldOffset(0)]  public uint Size;
        [FieldOffset(4)]  public uint Flags;
        [FieldOffset(8)]  public int FilterType;
        [FieldOffset(16)] public Guid InterfaceClass;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint DeviceNotificationCallback(IntPtr notification, IntPtr context, int action, IntPtr eventData, uint eventDataSize);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Register_Notification(ref DeviceNotificationFilter filter,
                                                        IntPtr context,
                                                        DeviceNotificationCallback callback,
                                                        out IntPtr notification);

    private const int FilterTypeDeviceInterface = 0;
    private const int ActionInterfaceArrival = 0, ActionInterfaceRemoval = 1;

    /** KSCATEGORY_AUDIO */
    private static readonly Guid AudioInterfaceClass = new("6994AD04-93EF-11D0-A3CC-00A0C9223196");

    private bool _hasTriedListening;
    private DeviceNotificationCallback? _notificationCallback;
}
