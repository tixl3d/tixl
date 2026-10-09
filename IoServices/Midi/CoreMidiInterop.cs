#nullable enable
using System;
using System.Runtime.InteropServices;

namespace T3.IoServices.Midi;

/// <summary>
/// The slice of CoreMIDI (and the CoreFoundation strings it names things with) needed to reach MIDI ports on
/// macOS. Both are system frameworks, so nothing has to be shipped.
/// </summary>
/// <remarks>
/// The packet-list functions used here are deprecated in favour of the MIDI 2.0 event-list API, but that one
/// delivers through Objective-C blocks, which .NET can't create without a binding. The packet-list API still
/// works and carries MIDI 1.0 messages unchanged.
/// </remarks>
internal static class CoreMidiInterop
{
    private const string CoreMidiLibrary = "/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI";
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary><c>MIDIReadProc</c>: called on CoreMIDI's own high-priority thread for each incoming packet list.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void MidiReadProc(IntPtr packetList, IntPtr readProcRefCon, IntPtr sourceConnectionRefCon);

    // MIDIPacketList and MIDIPacket are packed to 4 bytes: the list's packet count is followed directly by the
    // first packet, whose 8-byte timestamp is followed by a 16-bit length and the data. On Apple Silicon the
    // next packet starts at the following 4-byte boundary.
    internal const int PacketListHeaderSize = 4;
    internal const int PacketTimeStampOffset = 0;
    internal const int PacketLengthOffset = 8;
    internal const int PacketDataOffset = 10;

    [DllImport(CoreMidiLibrary)]
    internal static extern int MIDIClientCreate(IntPtr name, IntPtr notifyProc, IntPtr notifyRefCon, out uint client);

    [DllImport(CoreMidiLibrary)]
    internal static extern int MIDIClientDispose(uint client);

    [DllImport(CoreMidiLibrary)]
    internal static extern int MIDIInputPortCreate(uint client, IntPtr portName, IntPtr readProc, IntPtr refCon, out uint port);

    [DllImport(CoreMidiLibrary)]
    internal static extern int MIDIOutputPortCreate(uint client, IntPtr portName, out uint port);

    [DllImport(CoreMidiLibrary)]
    internal static extern UIntPtr MIDIGetNumberOfSources();

    [DllImport(CoreMidiLibrary)]
    internal static extern uint MIDIGetSource(UIntPtr sourceIndex);

    [DllImport(CoreMidiLibrary)]
    internal static extern UIntPtr MIDIGetNumberOfDestinations();

    [DllImport(CoreMidiLibrary)]
    internal static extern uint MIDIGetDestination(UIntPtr destinationIndex);

    [DllImport(CoreMidiLibrary)]
    internal static extern int MIDIObjectGetStringProperty(uint midiObject, IntPtr propertyId, out IntPtr value);

    [DllImport(CoreMidiLibrary)]
    internal static extern int MIDIPortConnectSource(uint port, uint source, IntPtr connectionRefCon);

    [DllImport(CoreMidiLibrary)]
    internal static extern int MIDIPortDisconnectSource(uint port, uint source);

    [DllImport(CoreMidiLibrary)]
    internal static extern int MIDISend(uint port, uint destination, IntPtr packetList);

    /// <summary>The name a device shows in other apps, "Device Port" where a device has several ports.</summary>
    internal static IntPtr PropertyDisplayName => _propertyDisplayName ??= ReadExportedPointer(CoreMidiLibrary, "kMIDIPropertyDisplayName");

    private static IntPtr? _propertyDisplayName;

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFStringGetLength(IntPtr text);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFStringGetMaximumSizeForEncoding(IntPtr length, uint encoding);

    [DllImport(CoreFoundationLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr text, byte[] buffer, IntPtr bufferSize, uint encoding);

    [DllImport(CoreFoundationLibrary)]
    internal static extern void CFRelease(IntPtr cfObject);

    private const uint Utf8Encoding = 0x08000100;

    /// <summary>A new CFString the caller has to release.</summary>
    internal static IntPtr CreateCfString(string text) => CFStringCreateWithCString(IntPtr.Zero, text, Utf8Encoding);

    internal static string ReadCfString(IntPtr text)
    {
        var size = (int)CFStringGetMaximumSizeForEncoding(CFStringGetLength(text), Utf8Encoding) + 1;
        var buffer = new byte[size];
        if (!CFStringGetCString(text, buffer, size, Utf8Encoding))
            return string.Empty;

        var length = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length < 0 ? size : length);
    }

    /// <summary>Reads a constant exported by a framework, such as a property key (a <c>CFStringRef</c>).</summary>
    private static IntPtr ReadExportedPointer(string library, string symbol)
    {
        var handle = NativeLibrary.Load(library);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(handle, symbol));
    }
}
