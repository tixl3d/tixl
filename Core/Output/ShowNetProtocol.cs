#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using T3.Core.DataTypes;

namespace T3.Core.Output;

/// <summary>
/// The showNET wire obfuscation: a TEA-like 32-round cipher (delta 0x9E3779B9, fixed key), applied
/// in 8-byte blocks to packet header and/or payload. Not a security mechanism — the key is fixed
/// in all clients and device firmware. Byte-exact port of the reference
/// <c>com.unknown.net.shownet.Crypt</c>.
/// </summary>
internal static class ShowNetCrypt
{
    private static readonly uint[] Key = [0x92F5B6E1, 0x43DA778C, 0x2B5E7C49, 0xB7341A22];

    /// <summary>Encrypts length bytes from start in place, rounded up to whole 8-byte blocks (a
    /// tail past length participates in the last block, exactly as the reference does).</summary>
    public static void Crypt(byte[] data, int start, int length)
    {
        Crypt(data, Key, start, length);
    }

    /// <summary>Decrypts in place — the inverse of <see cref="Crypt"/>, running the same rounds
    /// with the round constant starting at 0xC6EF3720 (= -32 · delta) and stepping backward.</summary>
    public static void Decrypt(byte[] data, int start, int length)
    {
        Decrypt(data, Key, start, length);
    }

    private static void Crypt(byte[] data, uint[] key, int start, int length)
    {
        var iterations = (length + 7) / 8;
        for (var i = 0; i < iterations; i++)
        {
            var offset = start + i * 8;
            var data0 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
            var data1 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
            uint k = 0;
            for (var round = 0; round < 32; round++)
            {
                data0 += (k + key[k & 3]) ^ (data1 + ((data1 >> 5) ^ (data1 << 4)));
                k += 0x9E3779B9;
                data1 += (k + key[(k >> 11) & 3]) ^ (data0 + ((data0 >> 5) ^ (data0 << 4)));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), data0);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 4, 4), data1);
        }
    }

    private static void Decrypt(byte[] data, uint[] key, int start, int length)
    {
        var iterations = (length + 7) / 8;
        for (var i = 0; i < iterations; i++)
        {
            var offset = start + i * 8;
            var data0 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
            var data1 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
            uint k = 0xC6EF3720;
            for (var round = 0; round < 32; round++)
            {
                data1 -= (k + key[(k >> 11) & 3]) ^ (data0 + ((data0 >> 5) ^ (data0 << 4)));
                k -= 0x9E3779B9;
                data0 -= (k + key[k & 3]) ^ (data1 + ((data1 >> 5) ^ (data1 << 4)));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), data0);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 4, 4), data1);
        }
    }
}

/// <summary>
/// The showNET payload scrambler: a 4-word LFSR keystream XORed over the packet words. Every
/// packet and every response decode seeds a fresh scrambler from the same fixed key, so the
/// keystream is a pure function of byte position — scrambling with a fresh scrambler is an
/// involution. Byte-exact port of the reference <c>Scrambler</c>.
/// </summary>
internal sealed class ShowNetScrambler
{
    private static readonly uint[] Key = [0xB92FF3CC, 0x75B36AF8, 0x5DEE4E5A, 0xF11090AB];

    private readonly uint[] _lfsr = new uint[4];

    public ShowNetScrambler()
    {
        Key.AsSpan().CopyTo(_lfsr);
    }

    public uint Next()
    {
        var tmp = _lfsr[0] ^ (_lfsr[0] << 11);
        _lfsr[0] = _lfsr[1];
        _lfsr[1] = _lfsr[2];
        _lfsr[2] = _lfsr[3];
        _lfsr[3] ^= (_lfsr[3] >> 19) ^ tmp ^ (tmp >> 8);
        return _lfsr[3];
    }

    public uint Scramble(uint word)
    {
        return word ^ Next();
    }
}

/// <summary>Client-side send options for one showNET packet — the reference's <c>Laser.send</c> flags.</summary>
[Flags]
internal enum ShowNetSendFlags
{
    None = 0,

    /// <summary>Header words 1..4 scrambled.</summary>
    ScrambleHeader = 0x01,

    /// <summary>Header bytes 4..20 encrypted.</summary>
    CryptHeader = 0x02,

    /// <summary>Payload from byte 20 scrambled word-wise (4-byte units).</summary>
    ScramblePayload = 0x04,

    /// <summary>Payload from byte 20 encrypted block-wise (8-byte units).</summary>
    CryptPayload = 0x08,

    /// <summary>The device's response arrives scrambled (from byte 4).</summary>
    ScrambleResponse = 0x10,

    /// <summary>The device's response arrives encrypted (from byte 4).</summary>
    CryptResponse = 0x20,

    /// <summary>Sets the wire "keys" marker (handshake only).</summary>
    Keys = 0x80,

    /// <summary>No response will be waited for (frames, NOPs, configure).</summary>
    IgnoreResponse = 0x100,
}

/// <summary>
/// One discovered showNET laser: the device's reply to the 30-byte UDP probe. Field offsets and
/// the 0xD49A3433 XOR decode follow the reference <c>LaserInfo</c>.
/// </summary>
internal sealed class ShowNetLaserInfo
{
    public required IPAddress Address { get; init; }

    /// <summary>XOR-decoded firmware and interface identity.</summary>
    public uint Bootloader;
    public uint Firmware;
    public readonly uint[] InterfaceId = new uint[3];

    /// <summary>Raw hardware id from the reply.</summary>
    public uint HardwareId;

    /// <summary>The 8 16-bit license tokens the device reports (/Open, /Phoenix, …).</summary>
    public readonly ushort[] License = new ushort[8];

    /// <summary>Environment.TickCount64 of the last announcing reply.</summary>
    public long Timestamp;

    /// <summary>The reference's "decoded ID string": interface bytes as ASCII ("." for
    /// non-printables), formatted "ID:XX:ASCII" — the device picker's display name.</summary>
    public string DisplayName
    {
        get
        {
            Span<byte> byte4 = stackalloc byte[4];
            Span<char> chars = stackalloc char[8];
            for (var i = 0; i < 2; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(byte4, InterfaceId[i + 1]);
                for (var j = 0; j < 4; j++)
                {
                    var b = byte4[j];
                    chars[i * 4 + j] = b is >= 0x20 and < 0x7F ? (char)b : '.';
                }
            }

            return $"{InterfaceId[0]:X8}:{InterfaceId[1] & 0xFF:X2}:{new string(chars[1..])}";
        }
    }

    /// <summary>The reference's license-token names for the known 16-bit ids.</summary>
    public string LicenseString()
    {
        var tokens = new List<string>();
        for (var i = 0; i < License.Length; i++)
        {
            switch (License[i])
            {
                case 0xF3CC: tokens.Add("/Open"); break;
                case 0x000A: tokens.Add("/Phoenix"); break;
                case 0x16BD: tokens.Add("/HE-Laserscan"); break;
                case 0x3D69: tokens.Add("/Pango"); break;
                case 0x1730: tokens.Add("/Showeditor"); break;
                case 0x70B8: tokens.Add("/Showcontroller"); break;
            }
        }

        return string.Join(" ", tokens);
    }
}

/// <summary>
/// showNET packet framing and parsing shared by the transport and discovery. The builders return
/// freshly allocated packets whose command-specific fields are already filled; the transport
/// writes the obfuscation header (words 0..4) via <see cref="PrepareSend"/> right before sending.
/// </summary>
internal static class ShowNetProtocol
{
    /// <summary>The device's data/handshake UDP port (the reference Laser.PORT).</summary>
    public const int LaserPort = 41855;

    /// <summary>The discovery socket's local port (the reference discoverySocket); the probe is
    /// broadcast to LaserPort from a socket bound here, and replies arrive back on it.</summary>
    public const int DiscoveryPort = 41856;

    /// <summary>The reference's 30-byte broadcast probe, sent to 255.255.255.255:LaserPort.</summary>
    public static readonly byte[] DiscoveryProbe =
        [
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x8D, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0xA3, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];

    /// <summary>Marker bits in header word 0 (bits 16+): the reference's PKT_FLAG_* constants.</summary>
    private const uint MarkerKeys = 0x010000;
    private const uint MarkerScrambleHeader = 0x200000;
    private const uint MarkerCryptHeader = 0x400000;

    /// <summary>
    /// Writes header words 0..4 (markers and payload word counts, response cipher, sequence id)
    /// and applies the requested obfuscation in place — the reference's <c>Laser.send</c>. Must be
    /// called after the command fields are filled and immediately before the socket send.
    /// </summary>
    public static void PrepareSend(byte[] buf, int length, ShowNetSendFlags flags, int seq)
    {
        var words = (length - 20) / 8;
        var cryptWords = (flags & ShowNetSendFlags.CryptPayload) != 0 ? words : 0;
        var scrambleWords = (flags & ShowNetSendFlags.ScramblePayload) != 0 ? words : 0;
        var responseCipher = ((flags & ShowNetSendFlags.ScrambleResponse) != 0 ? 1 : 0)
                           | ((flags & ShowNetSendFlags.CryptResponse) != 0 ? 2 : 0);
        var markers = ((flags & ShowNetSendFlags.Keys) != 0 ? MarkerKeys : 0u)
                    | ((flags & ShowNetSendFlags.ScrambleHeader) != 0 ? MarkerScrambleHeader : 0u)
                    | ((flags & ShowNetSendFlags.CryptHeader) != 0 ? MarkerCryptHeader : 0u);

        // The reference writes the scramble count unmasked into bits 0..15 (a frame with more
        // than 255 scramble blocks spills into the crypt-count byte) — replicated so the device
        // sees exactly the header the reference client puts on the wire.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4),
                                                 markers | (uint)cryptWords << 8 | (uint)scrambleWords);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), (uint)responseCipher << 24);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), (uint)seq);

        if ((flags & ShowNetSendFlags.CryptHeader) != 0)
            ShowNetCrypt.Crypt(buf, 4, 16);

        // One scrambler per packet, running continuously from the header into the payload — the
        // reference's send() creates it once, and the device decodes with the same single stream.
        var scrambler = new ShowNetScrambler();
        if ((flags & ShowNetSendFlags.ScrambleHeader) != 0)
        {
            for (var i = 1; i <= 4; i++)
            {
                var offset = i * 4;
                var word = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(offset, 4));
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), scrambler.Scramble(word));
            }
        }

        for (var i = 0; i < cryptWords; i++)
            ShowNetCrypt.Crypt(buf, 20 + i * 8, 8);

        if (scrambleWords > 0)
        {
            for (var i = 0; i < 2 * scrambleWords; i++)
            {
                var offset = 20 + i * 4;
                var word = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(offset, 4));
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), scrambler.Scramble(word));
            }
        }
    }

    /// <summary>The sequence id a reply carries in header word 0, bits 0..30 — read before any
    /// decoding, the device echoes it in the clear.</summary>
    public static int ResponseSeq(ReadOnlySpan<byte> packet)
    {
        return (int)(BinaryPrimitives.ReadUInt32LittleEndian(packet) & 0x7FFFFFFF);
    }

    /// <summary>
    /// Decodes a reply in place per the request's response cipher and reads the device status
    /// nibble from header word 5 (byte 20) after decoding — the reference's <c>Laser.receive</c>.
    /// The whole region from byte 4 participates, block- or word-wise.
    /// </summary>
    public static int DecodeResponse(byte[] data, int length, bool scrambled, bool crypted)
    {
        if (crypted)
        {
            var blocks = (length - 4) / 8;
            for (var i = 0; i < blocks; i++)
                ShowNetCrypt.Decrypt(data, 4 + i * 8, 8);
        }

        if (scrambled)
        {
            var scrambler = new ShowNetScrambler();
            var words = (length - 4) / 4;
            for (var i = 0; i < words; i++)
            {
                var offset = 4 + i * 4;
                var word = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), scrambler.Scramble(word));
            }
        }

        return (int)(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20, 4)) >> 28);
    }

    /// <summary>
    /// The handshake request (command 0x04): announces a random client key and asks for the
    /// device's capabilities. Sent with Keys | Crypt/Scramble Header+Payload+Response.
    /// </summary>
    public static byte[] BuildHandshake(uint clientKey)
    {
        var buf = new byte[32];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(12, 2), 1);
        buf[14] = 0x04;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(22, 4), clientKey);
        buf[26] = 0x0A;
        return buf;
    }

    /// <summary>
    /// The configure request (command 0x12): sets up the device's frame memory layout around the
    /// memory address the handshake reported. Sent once per session; the response is ignored.
    /// </summary>
    public static byte[] BuildConfigure(bool generation2, int frameMemorySize, int memoryAddress)
    {
        var buf = new byte[500];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(12, 2), 1);
        buf[14] = 0x12;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4), (uint)(520 + 2 * frameMemorySize + 64 + memoryAddress));
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(28, 4), (uint)(2 * frameMemorySize + 64 + memoryAddress));
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(32, 2), 516);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(34, 2), 513);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(36, 2), (ushort)(generation2 ? 254 : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(42, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(44, 2), 468);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(52, 4), 0x8B);
        return buf;
    }

    /// <summary>
    /// The keepalive packet (command 0x1A without a frame): tells the device the session is alive
    /// while nothing else flows. The reference's <c>sendNop</c> — fragment count 1, sequence 0.
    /// </summary>
    public static byte[] BuildNop(int packetId)
    {
        var buf = new byte[36];
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(12, 2), 9);
        buf[14] = 0x1A;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20, 4),
                                                 (1u << 16) | (0u << 11) | (uint)(packetId & 0x7FF));
        return buf;
    }

    /// <summary>The per-datagram bitstream budget: a fragment datagram stays at 1436+20 bytes,
    /// under both the device's receive buffer (1600) and the Ethernet MTU.</summary>
    public const int FragmentPayloadMax = 1400;

    /// <summary>The largest frame the transport can send: 32 fragments (the fragment-index field
    /// is 5 bits) of <see cref="FragmentPayloadMax"/> each.</summary>
    public const int MaxFrameBytes = 32 * FragmentPayloadMax;

    /// <summary>
    /// Splits an encoded frame into the datagrams the device reassembles: fragment 0 carries the
    /// 8-byte frame header (frame id, compressed flag, time divisor, point count) ahead of its
    /// bitstream slice; later fragments carry only bitstream bytes, addressed by their
    /// fragmentOffset. Fragment sizes are multiples of 4 (zero-padded) so the obfuscation stays
    /// word-aligned.
    /// </summary>
    public static byte[][] BuildFrame(int packetId, int frameId, bool compressed, int ticksPerPoint,
                                      int pointCount, byte[] bitstream, int bitstreamLength)
    {
        var fragmentCount = (bitstreamLength + FragmentPayloadMax - 1) / FragmentPayloadMax;
        var fragments = new byte[fragmentCount][];
        for (var k = 0; k < fragmentCount; k++)
        {
            var bitstreamOffset = k * FragmentPayloadMax;
            var chunk = Math.Min(FragmentPayloadMax, bitstreamLength - bitstreamOffset);
            var padded = (chunk + 3) & ~3;
            var size = k == 0 ? 8 + padded : padded;
            var buf = new byte[36 + size];
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(12, 2), 9);
            buf[14] = 0x1A;
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(20, 4),
                                                     (uint)fragmentCount << 16
                                                     | (uint)(k & 0x1F) << 11
                                                     | (uint)(packetId & 0x7FF));
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4),
                                                     (uint)size << 20 | (uint)(bitstreamOffset & 0xFFFF));
            var writeOffset = 36;
            if (k == 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(36, 4),
                                                         (uint)((frameId & 0x7FFFFF) << 8
                                                                | (compressed ? 0x10 : 0)));
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(40, 2), (ushort)ticksPerPoint);
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(42, 2), (ushort)pointCount);
                writeOffset = 44;
            }

            Array.Copy(bitstream, bitstreamOffset, buf, writeOffset, chunk);
            fragments[k] = buf;
        }

        return fragments;
    }

    /// <summary>
    /// Parses the handshake response into session info. The reply comes in three sizes (124/132/232
    /// bytes) with a size-dependent payload offset (the reference's <c>init()</c>). Call after
    /// <see cref="DecodeResponse"/> has decoded the reply in place.
    /// </summary>
    public static bool TryParseHandshakeResponse(byte[] rx, int length, out ShowNetSessionInfo info)
    {
        info = new ShowNetSessionInfo();
        var offset = length switch
                         {
                             124 => 24,
                             132 or 232 => 32,
                             _ => -1,
                         };
        if (offset < 0 || length < offset + 95)
            return false;

        var revision = BinaryPrimitives.ReadUInt32LittleEndian(rx.AsSpan(offset + 8, 4));
        info.Revision = revision;
        info.Firmware = revision == 11111
                            ? BinaryPrimitives.ReadUInt32LittleEndian(rx.AsSpan(offset, 4))
                            : BinaryPrimitives.ReadUInt32LittleEndian(rx.AsSpan(offset + 4, 4));
        info.Generation = length == 232 ? rx[offset + 52] : 1;
        info.TimeInterval = info.Generation2 ? 28_000_000 : 24_000_000;
        info.ColorFeatures = info.Firmware > 2014082201u
                                 ? (int)BinaryPrimitives.ReadUInt32LittleEndian(rx.AsSpan(offset + 90, 4))
                                 : -1;
        info.ColorCount = info.Firmware < 2018091702u
                              ? (info.ColorFeatures == 4 ? 3 : 6)
                              : rx[offset + 94];
        info.FramePointSize = info.Generation2 || info.ColorFeatures == 5 ? 9 : 7;
        info.MemoryAddress = (int)BinaryPrimitives.ReadUInt32LittleEndian(rx.AsSpan(offset + 36, 4));
        for (var i = 0; i < 3; i++)
            info.InterfaceId[i] = BinaryPrimitives.ReadUInt32LittleEndian(rx.AsSpan(offset + 24 + i * 4, 4));

        return true;
    }

    /// <summary>
    /// Parses the reply to the broadcast probe — the reference's <c>LaserInfo</c> constructor.
    /// The reply is plaintext; the identity words arrive XOR-decoded with 0xD49A3433.
    /// </summary>
    public static ShowNetLaserInfo? TryParseDiscoveryReply(IPAddress address, byte[] packet, int length)
    {
        if (length < 104)
            return null;

        var info = new ShowNetLaserInfo { Address = address };
        info.Bootloader = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(32, 4)) ^ 0xD49A3433;
        info.Firmware = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(36, 4)) ^ 0xD49A3433;
        for (var i = 0; i < 3; i++)
            info.InterfaceId[i] = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(40 + i * 4, 4)) ^ 0xD49A3433;

        info.HardwareId = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(52, 4));
        for (var i = 0; i < 8; i++)
            info.License[i] = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(56 + i * 2, 2));
        return info;
    }
}

/// <summary>
/// What the handshake established about a device: generation, color features, frame-point size,
/// timing. Snapshot the transport caches per session; drives the frame encoder.
/// </summary>
internal sealed class ShowNetSessionInfo
{
    /// <summary>Generation 1 (24 MHz point clock) or 2 (28 MHz).</summary>
    public int Generation;
    public bool Generation2 => Generation == 2;
    public int ColorFeatures;
    public int ColorCount;
    public int FramePointSize;
    public int TimeInterval;
    public uint Revision;
    public uint Firmware;
    public int MemoryAddress;
    public readonly uint[] InterfaceId = new uint[3];

    /// <summary>The device state nibble from the latest decoded response.</summary>
    public int Status;
}

/// <summary>
/// Encodes TiXL laser points into the showNET frame bitstream. The device decodes either the
/// fixed-size point records (7/9 bytes) or a stateful delta encoding; the encoder tries the delta
/// form first and falls back whenever it is not smaller — the reference <c>Frame</c> logic.
/// </summary>
internal static class ShowNetFrameEncoder
{
    /// <summary>
    /// Encodes count points from points. Returns false only when the frame is empty or exceeds
    /// <see cref="ShowNetProtocol.MaxFrameBytes"/> — both are transport-visible conditions.
    /// </summary>
    public static bool TryEncode(LaserPoint[] points, int count, in ShowNetSessionInfo info,
                                 float maxBrightness, bool swapColorChannels,
                                 ref byte[] bitstream, out int length, out bool compressed)
    {
        length = 0;
        compressed = false;
        if (points == null || count <= 0)
            return false;

        var fixedLength = count * info.FramePointSize;
        if (fixedLength > ShowNetProtocol.MaxFrameBytes)
            return false;

        var deltaLength = EncodeDelta(points, count, in info, maxBrightness, swapColorChannels, ref bitstream);
        if (deltaLength >= 0 && deltaLength <= fixedLength)
        {
            length = deltaLength;
            compressed = true;
            return true;
        }

        EnsureCapacity(ref bitstream, fixedLength);
        EncodeFixed(points, count, in info, maxBrightness, swapColorChannels, bitstream);
        length = fixedLength;
        return true;
    }

    private static void EnsureCapacity(ref byte[] buffer, int required)
    {
        if (buffer.Length < required)
            buffer = new byte[Math.Max(required, buffer.Length * 2)];
    }

    /// <summary>
    /// The fixed-size encoding: count records of info.FramePointSize bytes. Per point: x low byte,
    /// y low byte, packed high nibbles, R, G, B, then the color-features section (features 5 →
    /// user colors 1-3; otherwise intensity, plus two user colors on generation 2). TiXL intensity
    /// occupies the intensity/first-slot byte; the user colors TiXL does not produce stay zero.
    /// </summary>
    private static void EncodeFixed(LaserPoint[] points, int count, in ShowNetSessionInfo info,
                                    float maxBrightness, bool swapColorChannels, byte[] bitstream)
    {
        var scale = info.Generation2 ? 256 : 512;
        var colorFeatures5 = info.ColorFeatures == 5;
        var generation2 = info.Generation2;
        var ptr = 0;
        for (var i = 0; i < count; i++)
        {
            AuditPoint(points[i], maxBrightness, swapColorChannels, scale,
                       out var x12, out var y12, out var r8, out var g8, out var b8, out var i8);
            bitstream[ptr++] = (byte)x12;
            bitstream[ptr++] = (byte)y12;
            bitstream[ptr++] = (byte)(((y12 & 0xF00) >> 4) | ((x12 & 0xF00) >> 8));
            bitstream[ptr++] = r8;
            bitstream[ptr++] = g8;
            bitstream[ptr++] = b8;
            if (colorFeatures5)
            {
                bitstream[ptr++] = i8;
                bitstream[ptr++] = 0;
                bitstream[ptr++] = 0;
            }
            else
            {
                bitstream[ptr++] = i8;
                if (generation2)
                {
                    bitstream[ptr++] = 0;
                    bitstream[ptr++] = 0;
                }
            }
        }
    }

    /// <summary>
    /// The stateful delta encoding — the reference's encodeCompressed. Coordinates travel as
    /// bounded deltas against the last 12-bit position (absolute bytes when a delta does not
    /// fit); colors travel absolute when they changed against the last quantized value. Returns
    /// the encoded length.
    /// </summary>
    private static int EncodeDelta(LaserPoint[] points, int count, in ShowNetSessionInfo info,
                                   float maxBrightness, bool swapColorChannels, ref byte[] bitstream)
    {
        var scale = info.Generation2 ? 256 : 512;
        var colorFeatures5 = info.ColorFeatures == 5;
        var generation2 = info.Generation2;
        EnsureCapacity(ref bitstream, count * 10 + 16);
        var ptr = 0;
        var lastX = 0xFFFFFF;
        var lastY = 0xFFFFFF;
        var lastRed = 0xFFFFFF;
        var lastGreen = 0xFFFFFF;
        var lastBlue = 0xFFFFFF;
        var lastColor1 = 0xFFFFFF;
        var lastColor2 = 0xFFFFFF;
        var lastColor3 = 0xFFFFFF;
        // One scratch record for the whole frame: a stackalloc inside the loop would grow the stack by
        // 10 bytes per point until the method returns (CA2014), which large frames could overflow.
        // Only the first n bytes of each record are written and copied, so reuse is safe.
        Span<byte> point = stackalloc byte[10];
        for (var i = 0; i < count; i++)
        {
            AuditPoint(points[i], maxBrightness, swapColorChannels, scale,
                       out var x12, out var y12, out var r8, out var g8, out var b8, out var i8);
            var n = 1;
            var flags = 0;
            var dx = x12 - lastX;
            var dy = y12 - lastY;
            if (dy != 0 || dx < -128 || dx > 127)
            {
                if (dx != 0 || dy < -128 || dy > 127)
                {
                    if (dx < -128 || dx > 127 || dy < -128 || dy > 127)
                    {
                        point[n++] = (byte)x12;
                        point[n++] = (byte)y12;
                        point[n++] = (byte)(((y12 & 0xF00) >> 4) | ((x12 & 0xF00) >> 8));
                        flags = 3;
                    }
                    else
                    {
                        point[n++] = (byte)dx;
                        point[n++] = (byte)dy;
                        flags = 2;
                    }
                }
                else
                {
                    point[n++] = (byte)dy;
                    flags = 1;
                }
            }
            else
            {
                point[n++] = (byte)dx;
            }

            if (lastRed != r8)
            {
                point[n++] = r8;
                flags |= 0x04;
            }

            if (lastGreen != g8)
            {
                point[n++] = g8;
                flags |= 0x08;
            }

            if (lastBlue != b8)
            {
                point[n++] = b8;
                flags |= 0x10;
            }

            if (colorFeatures5)
            {
                if (lastColor1 != i8)
                {
                    point[n++] = i8;
                    flags |= 0x20;
                }

                if (lastColor2 != 0)
                {
                    point[n++] = 0;
                    flags |= 0x40;
                }

                if (lastColor3 != 0)
                {
                    point[n++] = 0;
                    flags |= 0x80;
                }

                lastColor1 = i8;
                lastColor2 = 0;
                lastColor3 = 0;
            }
            else
            {
                if (lastColor1 != i8)
                {
                    point[n++] = i8;
                    flags |= 0x20;
                }

                if (generation2)
                {
                    if (lastColor2 != 0)
                    {
                        point[n++] = 0;
                        flags |= 0x40;
                    }

                    if (lastColor3 != 0)
                    {
                        point[n++] = 0;
                        flags |= 0x80;
                    }

                    lastColor2 = 0;
                    lastColor3 = 0;
                }
            }

            point[0] = (byte)flags;
            point[..n].CopyTo(bitstream.AsSpan(ptr));
            ptr += n;
            lastX = x12;
            lastY = y12;
            lastRed = r8;
            lastGreen = g8;
            lastBlue = b8;
        }

        return ptr;
    }

    /// <summary>
    /// The final per-point audit — the transport-level brightness ceiling and channel order — plus
    /// quantization to the device's 12-bit coordinates and 8-bit colors. Blank points (intensity
    /// ≤ 0) lose every color; showNET decodes inverted 12-bit coordinates.
    /// </summary>
    private static void AuditPoint(LaserPoint p, float maxBrightness, bool swapColorChannels, int scale,
                                   out int x12, out int y12, out byte r8, out byte g8, out byte b8, out byte i8)
    {
        x12 = (int)((0xFFFFu - (uint)(ushort)p.X) >> 4);
        y12 = (int)((0xFFFFu - (uint)(ushort)p.Y) >> 4);
        var blank = p.I <= 0;
        var red = swapColorChannels ? p.B : p.R;
        var blue = swapColorChannels ? p.R : p.B;
        r8 = Quantize(blank ? 0 : red, maxBrightness, scale);
        g8 = Quantize(blank ? 0 : p.G, maxBrightness, scale);
        b8 = Quantize(blank ? 0 : blue, maxBrightness, scale);
        i8 = Quantize(blank ? 0 : p.I, maxBrightness, scale);
    }

    private static byte Quantize(int value, float ceiling, int scale)
    {
        var scaled = (int)MathF.Round(MathF.Max(value, 0) * ceiling);
        var clamped = Math.Clamp(scaled, 0, ushort.MaxValue);
        return (byte)(clamped / scale);
    }
}
