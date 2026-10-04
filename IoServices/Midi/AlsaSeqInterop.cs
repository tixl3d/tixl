#nullable enable
using System;
using System.Runtime.InteropServices;

namespace T3.IoServices.Midi;

/// <summary>
/// The slice of the ALSA sequencer API needed to reach MIDI ports on Linux.
/// </summary>
/// <remarks>
/// libasound is part of any Linux install that has sound at all, so nothing has to be shipped with it.
/// The sequencer (rather than rawmidi) is used because it also reaches virtual and application ports -
/// DAWs, bridges and <c>snd-virmidi</c> - not just hardware.
/// </remarks>
internal static class AlsaSeqInterop
{
    /// <summary>
    /// One sequencer event. Layout matches <c>snd_seq_event_t</c>: 28 bytes, packed, data union at 16.
    /// Only the header fields and the sysex payload pointer are mapped; everything else inside the union
    /// is produced and consumed by <c>snd_midi_event_*</c>.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 28, Pack = 1)]
    internal struct SeqEvent
    {
        [FieldOffset(0)]  public byte Type;
        [FieldOffset(1)]  public byte Flags;
        [FieldOffset(2)]  public byte Tag;
        [FieldOffset(3)]  public byte Queue;
        [FieldOffset(12)] public byte SourceClient;
        [FieldOffset(13)] public byte SourcePort;
        [FieldOffset(14)] public byte DestClient;
        [FieldOffset(15)] public byte DestPort;
        [FieldOffset(16)] public uint ExtLen;
        [FieldOffset(20)] public IntPtr ExtPtr;
    }

    internal const int OpenDuplex = 3;
    internal const uint CapRead = 1, CapWrite = 2, CapSubsRead = 32, CapSubsWrite = 64;
    internal const uint TypeMidiGeneric = 2, TypeApplication = 1048576;
    internal const byte EventSysex = 130, QueueDirect = 253;

    /// <summary>ALSA's own client, which owns the Timer and Announce ports rather than any instrument.</summary>
    internal const int SystemClientId = 0;

    [DllImport(Lib)] internal static extern int snd_seq_open(out IntPtr handle, string name, int streams, int mode);
    [DllImport(Lib)] internal static extern int snd_seq_close(IntPtr handle);
    [DllImport(Lib)] internal static extern int snd_seq_set_client_name(IntPtr handle, string name);
    [DllImport(Lib)] internal static extern int snd_seq_client_id(IntPtr handle);
    [DllImport(Lib)] internal static extern int snd_seq_create_simple_port(IntPtr handle, string name, uint caps, uint type);
    [DllImport(Lib)] internal static extern int snd_seq_delete_simple_port(IntPtr handle, int port);
    [DllImport(Lib)] internal static extern int snd_seq_connect_to(IntPtr handle, int myPort, int destClient, int destPort);
    [DllImport(Lib)] internal static extern int snd_seq_connect_from(IntPtr handle, int myPort, int srcClient, int srcPort);
    [DllImport(Lib)] internal static extern int snd_seq_disconnect_from(IntPtr handle, int myPort, int srcClient, int srcPort);
    [DllImport(Lib)] internal static extern int snd_seq_event_input(IntPtr handle, out IntPtr ev);
    [DllImport(Lib)] internal static extern int snd_seq_event_output_direct(IntPtr handle, ref SeqEvent ev);
    [DllImport(Lib)] internal static extern int snd_seq_drain_output(IntPtr handle);

    [DllImport(Lib)] internal static extern int snd_midi_event_new(UIntPtr bufferSize, out IntPtr parser);
    [DllImport(Lib)] internal static extern void snd_midi_event_free(IntPtr parser);
    [DllImport(Lib)] internal static extern void snd_midi_event_init(IntPtr parser);
    [DllImport(Lib)] internal static extern void snd_midi_event_no_status(IntPtr parser, int enable);
    [DllImport(Lib)] internal static extern long snd_midi_event_encode(IntPtr parser, byte[] buffer, long count, ref SeqEvent ev);
    [DllImport(Lib)] internal static extern long snd_midi_event_decode(IntPtr parser, byte[] buffer, long count, IntPtr ev);

    [DllImport(Lib)] internal static extern int snd_seq_client_info_malloc(out IntPtr info);
    [DllImport(Lib)] internal static extern void snd_seq_client_info_free(IntPtr info);
    [DllImport(Lib)] internal static extern void snd_seq_client_info_set_client(IntPtr info, int client);
    [DllImport(Lib)] internal static extern int snd_seq_client_info_get_client(IntPtr info);
    [DllImport(Lib)] internal static extern IntPtr snd_seq_client_info_get_name(IntPtr info);
    [DllImport(Lib)] internal static extern int snd_seq_query_next_client(IntPtr handle, IntPtr info);

    [DllImport(Lib)] internal static extern int snd_seq_port_info_malloc(out IntPtr info);
    [DllImport(Lib)] internal static extern void snd_seq_port_info_free(IntPtr info);
    [DllImport(Lib)] internal static extern void snd_seq_port_info_set_client(IntPtr info, int client);
    [DllImport(Lib)] internal static extern void snd_seq_port_info_set_port(IntPtr info, int port);
    [DllImport(Lib)] internal static extern int snd_seq_port_info_get_port(IntPtr info);
    [DllImport(Lib)] internal static extern IntPtr snd_seq_port_info_get_name(IntPtr info);
    [DllImport(Lib)] internal static extern uint snd_seq_port_info_get_capability(IntPtr info);
    [DllImport(Lib)] internal static extern int snd_seq_query_next_port(IntPtr handle, IntPtr info);

    internal static string ReadString(IntPtr nativeString)
    {
        return Marshal.PtrToStringAnsi(nativeString) ?? string.Empty;
    }

    /** The ALSA runtime; versioned soname because the unversioned link exists only with the -dev package. */
    private const string Lib = "libasound.so.2";
}
