#nullable enable
using System;
using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using T3.Core.DataTypes;
using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Characterization tests for the showNET protocol module (Core/Output/ShowNetProtocol.cs) —
/// the wire obfuscation, packet framing, frame encodings, and reply parsing. No hardware needed:
/// the vectors below are computed from the reference implementation's semantics, and the frame
/// decoder mirrors what the device decodes, so encode→decode roundtrips pin the bitstream format.
/// </summary>
public class ShowNetProtocolTests
{
    private static ShowNetSessionInfo Gen1() => new()
                                                    {
                                                        Generation = 1,
                                                        FramePointSize = 7,
                                                        TimeInterval = 24_000_000,
                                                        ColorFeatures = 0,
                                                    };

    private static ShowNetSessionInfo Gen2() => new()
                                                    {
                                                        Generation = 2,
                                                        FramePointSize = 9,
                                                        TimeInterval = 28_000_000,
                                                        ColorFeatures = 0,
                                                    };

    // ------------------------------------------------------------------ obfuscation

    [Fact]
    public void Crypt_RoundTrip()
    {
        // The cipher runs in whole 8-byte blocks; the reference client only ever crypts
        // block-aligned regions (8/16-byte payloads, 16-byte headers).
        var data = new byte[40];
        Random.Shared.NextBytes(data);
        var original = (byte[])data.Clone();

        ShowNetCrypt.Crypt(data, 0, data.Length);
        Assert.NotEqual(original, data);

        ShowNetCrypt.Decrypt(data, 0, data.Length);
        Assert.Equal(original, data);
    }

    [Fact]
    public void Scrambler_Involution()
    {
        var words = new uint[64];
        Random.Shared.NextBytes(MemoryMarshal.AsBytes(words.AsSpan()));

        var forward = new ShowNetScrambler();
        var scrambled = new uint[words.Length];
        for (var i = 0; i < words.Length; i++)
            scrambled[i] = forward.Scramble(words[i]);
        Assert.NotEqual(words, scrambled);

        var backward = new ShowNetScrambler();
        for (var i = 0; i < words.Length; i++)
            scrambled[i] = backward.Scramble(scrambled[i]);
        Assert.Equal(words, scrambled);
    }

    [Fact]
    public void PinnedConstants()
    {
        // The obfuscation keys and probe bytes are fixed protocol constants - pinned here so a
        // refactor cannot silently change the wire format.
        var cryptKey = PrivateKey();
        Assert.Equal(new uint[] { 0x92F5B6E1, 0x43DA778C, 0x2B5E7C49, 0xB7341A22 }, cryptKey);

        var expectedProbe = new byte[]
                                {
                                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                                    0x8D, 0, 0x02, 0, 0, 0, 0, 0, 0x80, 0xA3, 0, 0,
                                    0, 0, 0, 0, 0, 0,
                                };
        Assert.Equal(expectedProbe, ShowNetProtocol.DiscoveryProbe);
    }

    private static uint[] PrivateKey()
    {
        var field = typeof(ShowNetCrypt).GetField("Key", BindingFlags.NonPublic | BindingFlags.Static);
        return (uint[])field!.GetValue(null)!;
    }

    // ------------------------------------------------------------------ packet layout

    [Fact]
    public void HandshakePacket_Layout()
    {
        var packet = ShowNetProtocol.BuildHandshake(0xDEADBEEF);
        Assert.Equal(32, packet.Length);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(12, 2)));
        Assert.Equal(0x04, packet[14]);
        Assert.Equal(0xDEADBEEF, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(22, 4)));
        Assert.Equal(0x0A, packet[26]);
    }

    [Fact]
    public void ConfigurePacket_Layout()
    {
        var packet = ShowNetProtocol.BuildConfigure(generation2: false, frameMemorySize: 0x8000, memoryAddress: 0x123456);
        Assert.Equal(500, packet.Length);
        Assert.Equal(0x12, packet[14]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(22, 2)));
        Assert.Equal((uint)(520 + 2 * 0x8000 + 64 + 0x123456),
                     BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(24, 4)));
        Assert.Equal((uint)(2 * 0x8000 + 64 + 0x123456),
                     BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(28, 4)));
        Assert.Equal(516, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(32, 2)));
        Assert.Equal(513, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(34, 2)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(36, 2)));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(42, 2)));
        Assert.Equal(468, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(44, 2)));
        Assert.Equal(0x8Bu, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(52, 4)));
    }

    [Fact]
    public void NopPacket_Layout()
    {
        var packet = ShowNetProtocol.BuildNop(0x2A);
        Assert.Equal(36, packet.Length);
        Assert.Equal(0x1A, packet[14]);
        Assert.Equal((1u << 16) | 0x2A, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(20, 4)));
    }

    [Fact]
    public void PrepareSend_HeaderLayout()
    {
        // Payload-only obfuscation keeps header words 1..4 plaintext and assertable.
        var packet = ShowNetProtocol.BuildNop(1);
        ShowNetProtocol.PrepareSend(packet, packet.Length,
                                    ShowNetSendFlags.ScramblePayload | ShowNetSendFlags.ScrambleResponse,
                                    0x123456);
        var word0 = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0, 4));
        Assert.Equal(0x02u, word0);       // no markers, 0 crypt words, 2 scramble words
        var word1 = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4));
        Assert.Equal(0x01000000u, word1); // response cipher = 1 (scrambled)
        var word2 = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4));
        Assert.Equal(0x123456u, word2);

        // With header obfuscation the markers become visible in word 0.
        var packet2 = ShowNetProtocol.BuildNop(2);
        ShowNetProtocol.PrepareSend(packet2, packet2.Length,
                                    ShowNetSendFlags.ScrambleHeader | ShowNetSendFlags.CryptHeader
                                    | ShowNetSendFlags.ScramblePayload,
                                    5);
        Assert.Equal(0x600002u, BinaryPrimitives.ReadUInt32LittleEndian(packet2.AsSpan(0, 4)));
    }

    [Fact]
    public void PrepareSend_PayloadRoundTrip()
    {
        var packet = new byte[20 + 16];
        Random.Shared.NextBytes(packet.AsSpan(20));
        var payload = packet[20..].ToArray();

        ShowNetProtocol.PrepareSend(packet, packet.Length,
                                    ShowNetSendFlags.ScramblePayload | ShowNetSendFlags.CryptPayload, 7);
        Assert.NotEqual(payload, packet[20..].ToArray());

        // The device decodes: unscramble the words, then decrypt the blocks - the inverse order.
        var scrambler = new ShowNetScrambler();
        for (var i = 0; i < 4; i++)
        {
            var offset = 20 + i * 4;
            var word = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(offset, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(offset, 4), scrambler.Scramble(word));
        }

        ShowNetCrypt.Decrypt(packet, 20, 16);
        Assert.Equal(payload, packet[20..].ToArray());
    }

    [Fact]
    public void PrepareSend_ScrambleContinuity()
    {
        // The reference client runs ONE scrambler per packet, continuously from the header into
        // the payload: when the header is scrambled it consumes keystream words 1..4, so the
        // payload starts at keystream word 5. This pins that contract.
        var packet = new byte[20 + 16];
        Random.Shared.NextBytes(packet.AsSpan(20));
        var payload = packet[20..].ToArray();

        ShowNetProtocol.PrepareSend(packet, packet.Length,
                                    ShowNetSendFlags.ScrambleHeader | ShowNetSendFlags.ScramblePayload, 1);

        var seed = new ShowNetScrambler();
        var keystream = new uint[8];
        for (var i = 0; i < 8; i++)
            keystream[i] = seed.Next();

        for (var i = 0; i < 4; i++)
        {
            var expected = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(i * 4, 4)) ^ keystream[4 + i];
            Assert.Equal(expected, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(20 + i * 4, 4)));
        }
    }

    // ------------------------------------------------------------------ frame bitstream

    /// <summary>
    /// The device-side bitstream decoder, mirroring the reference device semantics: fixed 7/9-byte
    /// records, or the stateful delta form whose coordinate/color state starts at 0xFFFFFF (the
    /// seed the reference encoder assumes on the device).
    /// </summary>
    private static List<(int X12, int Y12, int R8, int G8, int B8, int I8)> DecodeBitstream(
        byte[] bitstream, int length, bool compressed, int framePointSize, bool generation2, bool colorFeatures5)
    {
        var points = new List<(int X12, int Y12, int R8, int G8, int B8, int I8)>();
        if (!compressed)
        {
            for (var offset = 0; offset < length; offset += framePointSize)
            {
                points.Add((
                    bitstream[offset] | ((bitstream[offset + 2] & 0x0F) << 8),
                    bitstream[offset + 1] | ((bitstream[offset + 2] >> 4) << 8),
                    bitstream[offset + 3],
                    bitstream[offset + 4],
                    bitstream[offset + 5],
                    bitstream[offset + 6]));
            }

            return points;
        }

        var x = 0xFFFFFF;
        var y = 0xFFFFFF;
        var r8 = 0xFFFFFF;
        var g8 = 0xFFFFFF;
        var b8 = 0xFFFFFF;
        var i8 = 0xFFFFFF;
        for (var offset = 0; offset < length; )
        {
            var flags = bitstream[offset++];
            switch (flags & 0x03)
            {
                case 0: x += (sbyte)bitstream[offset++]; break;
                case 1: y += (sbyte)bitstream[offset++]; break;
                case 2: x += (sbyte)bitstream[offset++]; y += (sbyte)bitstream[offset++]; break;
                case 3:
                    x = bitstream[offset] | ((bitstream[offset + 2] & 0x0F) << 8);
                    y = bitstream[offset + 1] | ((bitstream[offset + 2] >> 4) << 8);
                    offset += 3;
                    break;
            }

            if ((flags & 0x04) != 0)
                r8 = bitstream[offset++];
            if ((flags & 0x08) != 0)
                g8 = bitstream[offset++];
            if ((flags & 0x10) != 0)
                b8 = bitstream[offset++];
            if ((flags & 0x20) != 0)
                i8 = bitstream[offset++];
            points.Add((x, y, r8, g8, b8, i8));
        }

        return points;
    }

    [Fact]
    public void FrameEncoder_DeltaRoundTrip_Gen1()
    {
        var info = Gen1();
        var count = 64;
        var points = new LaserPoint[count];
        for (var i = 0; i < count; i++)
            points[i] = new LaserPoint(i * 1024, 32768, 65535, 65535, 65535, 65535);

        byte[] bitstream = [];
        var ok = ShowNetFrameEncoder.TryEncode(points, count, in info, 1f, false, ref bitstream, out var length, out var compressed);
        Assert.True(ok);
        Assert.True(compressed);   // 2 bytes/point against the fixed 7 — the delta form wins

        var decoded = DecodeBitstream(bitstream, length, compressed, info.FramePointSize, generation2: false, colorFeatures5: false);
        Assert.Equal(count, decoded.Count);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal((int)((0xFFFFu - (uint)(ushort)points[i].X) >> 4), decoded[i].X12);
            Assert.Equal((int)((0xFFFFu - (uint)(ushort)points[i].Y) >> 4), decoded[i].Y12);
            Assert.Equal(127, decoded[i].R8);   // 65535 / 512 (the generation-1 scale)
            Assert.Equal(127, decoded[i].G8);
            Assert.Equal(127, decoded[i].B8);
            Assert.Equal(127, decoded[i].I8);
        }
    }

    [Fact]
    public void FrameEncoder_FixedFallback_Gen1()
    {
        var info = Gen1();
        var count = 32;
        var points = new LaserPoint[count];
        for (var i = 0; i < count; i++)
        {
            var lit = i % 2 == 0;
            points[i] = new LaserPoint(lit ? 0 : 65535, lit ? 0 : 65535,
                                       lit ? 65535 : 0, lit ? 65535 : 0,
                                       lit ? 65535 : 0, lit ? 65535 : 0);
        }

        byte[] bitstream = [];
        var ok = ShowNetFrameEncoder.TryEncode(points, count, in info, 1f, false, ref bitstream, out var length, out var compressed);
        Assert.True(ok);
        Assert.False(compressed);  // every point changes all four colors and needs absolute xy - 8 B/pt beats 7
        Assert.Equal(count * 7, length);

        var decoded = DecodeBitstream(bitstream, length, compressed, info.FramePointSize, generation2: false, colorFeatures5: false);
        Assert.Equal(count, decoded.Count);
        for (var i = 0; i < count; i++)
        {
            var lit = i % 2 == 0;
            Assert.Equal(lit ? 4095 : 0, decoded[i].X12);
            Assert.Equal(lit ? 4095 : 0, decoded[i].Y12);
            Assert.Equal(lit ? 127 : 0, decoded[i].R8);
            Assert.Equal(lit ? 127 : 0, decoded[i].G8);
            Assert.Equal(lit ? 127 : 0, decoded[i].B8);
            Assert.Equal(lit ? 127 : 0, decoded[i].I8);
        }
    }

    [Fact]
    public void FrameEncoder_Gen2_NineByteRecords()
    {
        var info = Gen2();
        var count = 8;
        var points = new LaserPoint[count];
        for (var i = 0; i < count; i++)
            points[i] = new LaserPoint(0x8000, 0x8000, 65535, 0, 0, 32768);

        byte[] bitstream = [];
        var ok = ShowNetFrameEncoder.TryEncode(points, count, in info, 1f, false, ref bitstream, out var length, out var compressed);
        Assert.True(ok);
        Assert.True(compressed);   // identical points: 8 + 7·1 bytes against the fixed 72 — delta wins
        Assert.True(length <= count * info.FramePointSize);

        var decoded = DecodeBitstream(bitstream, length, compressed, info.FramePointSize, generation2: true, colorFeatures5: false);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(2047, decoded[i].X12);
            Assert.Equal(2047, decoded[i].Y12);
            Assert.Equal(255, decoded[i].R8);   // 65535 / 256 (the generation-2 scale)
            Assert.Equal(0, decoded[i].G8);
            Assert.Equal(0, decoded[i].B8);
            Assert.Equal(128, decoded[i].I8);   // 32768 / 256
        }
    }

    // ------------------------------------------------------------------ reply parsing

    [Fact]
    public void DiscoveryReply_Parse()
    {
        var packet = new byte[104];
        SetW(packet, 32, 0x1A2B3C4Du ^ 0xD49A3433);
        SetW(packet, 36, 0x02020202u ^ 0xD49A3433);
        for (var i = 0; i < 3; i++)
            SetW(packet, 40 + i * 4, (0x08004D01u + (uint)i) ^ 0xD49A3433);
        SetW(packet, 52, 0x00112233);
        for (var i = 0; i < 8; i++)
            SetW16(packet, 56 + i * 2, i switch
                                       {
                                           0 => (ushort)0xF3CC,
                                           1 => (ushort)0x000A,
                                           _ => 0,
                                       });

        var info = ShowNetProtocol.TryParseDiscoveryReply(IPAddress.Loopback, packet, packet.Length);
        Assert.NotNull(info);
        Assert.Equal(0x1A2B3C4Du, info!.Bootloader);
        Assert.Equal(0x02020202u, info.Firmware);
        Assert.Equal(0x08004D01u, info.InterfaceId[0]);
        Assert.Equal((ushort)0xF3CC, info.License[0]);
        Assert.Equal("/Open /Phoenix", info.LicenseString());
        Assert.Contains("M...M..", info.DisplayName);
    }

    [Fact]
    public void HandshakeResponse_Parse()
    {
        var rx = new byte[124];
        var offset = 24;
        SetW(rx, offset + 8, 11111);                       // revision marker → firmware at offset+0
        SetW(rx, offset, 2019010101);
        SetW(rx, offset + 24, 0xAAAAAAA);
        SetW(rx, offset + 28, 0xBBBBBBB);
        SetW(rx, offset + 32, 0xCCCCCCC);
        SetW(rx, offset + 36, 0x00800000);
        SetW16(rx, offset + 44, 7);
        SetW(rx, offset + 90, 4);                          // color features 4
        SetByte(rx, offset + 94, 5);

        var ok = ShowNetProtocol.TryParseHandshakeResponse(rx, rx.Length, out var info);
        Assert.True(ok);
        Assert.Equal(11111u, info.Revision);
        Assert.Equal(2019010101u, info.Firmware);
        Assert.Equal(1, info.Generation);                  // only the 232-byte reply carries the generation byte
        Assert.Equal(24_000_000, info.TimeInterval);
        Assert.Equal(4, info.ColorFeatures);
        Assert.Equal(5, info.ColorCount);                  // firmware ≥ 2018091702 → the byte at offset+94
        Assert.Equal(7, info.FramePointSize);              // gen 1, features ≠ 5 → 7-byte records
        Assert.Equal(0x00800000, info.MemoryAddress);
    }

    // ------------------------------------------------------------------ frame datagrams

    [Fact]
    public void BuildFrame_FragmentLayout()
    {
        var bitstream = new byte[3000];
        for (var i = 0; i < bitstream.Length; i++)
            bitstream[i] = (byte)(i & 0xFF);

        var fragments = ShowNetProtocol.BuildFrame(packetId: 5, frameId: 0x123456, compressed: true,
                                                   ticksPerPoint: 800, pointCount: 428, bitstream, 3000);
        Assert.Equal(3, fragments.Length);
        Assert.All(fragments, fragment => Assert.True(fragment.Length <= 1600));

        // Fragment 0 carries the 8-byte frame header; word@24 = size<<20 | offset.
        var f0 = fragments[0];
        Assert.Equal(36 + 8 + 1400, f0.Length);
        Assert.Equal((3u << 16) | 5u, BinaryPrimitives.ReadUInt32LittleEndian(f0.AsSpan(20, 4)));
        Assert.Equal((1408u << 20) | 0u, BinaryPrimitives.ReadUInt32LittleEndian(f0.AsSpan(24, 4)));
        Assert.Equal(0x12345600u | 0x10u, BinaryPrimitives.ReadUInt32LittleEndian(f0.AsSpan(36, 4)));
        Assert.Equal(800, BinaryPrimitives.ReadUInt16LittleEndian(f0.AsSpan(40, 2)));
        Assert.Equal(428, BinaryPrimitives.ReadUInt16LittleEndian(f0.AsSpan(42, 2)));
        Assert.Equal(bitstream[0..1400], f0[44..]);

        var f1 = fragments[1];
        Assert.Equal((3u << 16) | (1u << 11) | 5u, BinaryPrimitives.ReadUInt32LittleEndian(f1.AsSpan(20, 4)));
        Assert.Equal((1400u << 20) | 1400u, BinaryPrimitives.ReadUInt32LittleEndian(f1.AsSpan(24, 4)));
        Assert.Equal(bitstream[1400..2800], f1[36..]);

        var f2 = fragments[2];
        Assert.Equal((3u << 16) | (2u << 11) | 5u, BinaryPrimitives.ReadUInt32LittleEndian(f2.AsSpan(20, 4)));
        Assert.Equal((200u << 20) | 2800u, BinaryPrimitives.ReadUInt32LittleEndian(f2.AsSpan(24, 4)));
        Assert.Equal(bitstream[2800..3000], f2[36..]);
    }

    // ------------------------------------------------------------------ helpers

    private static void SetW(byte[] buffer, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);
    }

    private static void SetW16(byte[] buffer, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), value);
    }

    private static void SetByte(byte[] buffer, int offset, byte value)
    {
        buffer[offset] = value;
    }
}

