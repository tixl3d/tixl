using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Round-trips the PONK UDP wire format used by the PONKOutput/PONKInput operators
/// (Operators/Lib/laser). The 52-byte header layout is fixed by the PONK spec
/// (MadMapper PonkDefs.h; cross-checked against the ModulaserApp ponk-protocol codec:
/// "Each datagram has a 52-byte header", chunk quantum 1472 - 52 = 1420 bytes):
///   0-7 magic "PONK-UDP" | 8 version | 9-12 sender id (LE i32) | 13-44 sender name
///   45 frame number | 46 chunk count | 47 chunk number | 48-51 data crc (LE u32,
///   byte-sum of all frame data - not a polynomial CRC). Payload starts at offset 52.
///
/// A 48-byte header would place the crc where the payload begins: the sender would
/// overwrite it and every receiver-side checksum would fail. These tests pin the
/// correct offsets, exercise multi-chunk reassembly (out-of-order delivery) and the
/// empty trailing chunk canonical senders emit for exact-multiple frames.
/// </summary>
public class PonkUdpWireFormatTests
{
    private const string Magic = "PONK-UDP";
    private const int HeaderSize = 52;
    private const int MaxChunkPayload = 1472 - HeaderSize; // 1420

    private static uint Checksum(byte[] data)
    {
        uint sum = 0;
        for (var i = 0; i < data.Length; i++)
            sum += data[i];
        return sum;
    }

    private static byte[] BuildPathData((float X, float Y, byte R, byte G, byte B)[] points)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)1); // data format XY_F32_RGB_U8 (mandatory)
        w.Write((byte)2); // two meta entries
        WriteMeta(w, "PATHNUMB", 1);
        WriteMeta(w, "MAXSPEED", 1.0f);
        w.Write((ushort)points.Length);
        foreach (var p in points)
        {
            w.Write(p.X);
            w.Write(p.Y);
            w.Write(p.R);
            w.Write(p.G);
            w.Write(p.B);
        }

        w.Flush();
        return ms.ToArray();
    }

    private static void WriteMeta(BinaryWriter w, string key, float value)
    {
        var keyBytes = new byte[8];
        Encoding.ASCII.GetBytes(key, keyBytes);
        w.Write(keyBytes);
        w.Write(value);
    }

    /// <summary>Mirrors PONKOutput.SendFrame/WriteHeader: header-prefixed chunk slices.</summary>
    private static List<byte[]> EncodeDatagrams(byte[] frameData, int senderId, string senderName,
                                                 byte frameNumber, bool canonicalTrailingEmptyChunk = false)
    {
        var crc = Checksum(frameData);
        var chunkCount = (frameData.Length + MaxChunkPayload - 1) / MaxChunkPayload;
        if (canonicalTrailingEmptyChunk && frameData.Length > 0 && frameData.Length % MaxChunkPayload == 0)
            chunkCount++;

        var datagrams = new List<byte[]>();
        var written = 0;
        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            var size = Math.Min(MaxChunkPayload, frameData.Length - written);
            if (size < 0)
                size = 0;
            var dg = new byte[HeaderSize + size];
            Encoding.ASCII.GetBytes(Magic).CopyTo(dg, 0);
            dg[8] = 0; // protocol version
            dg[9] = (byte)senderId;
            dg[10] = (byte)(senderId >> 8);
            dg[11] = (byte)(senderId >> 16);
            dg[12] = (byte)(senderId >> 24);
            Encoding.ASCII.GetBytes(senderName).CopyTo(dg, 13);
            dg[45] = frameNumber;
            dg[46] = (byte)chunkCount;
            dg[47] = (byte)chunk;
            dg[48] = (byte)crc;
            dg[49] = (byte)(crc >> 8);
            dg[50] = (byte)(crc >> 16);
            dg[51] = (byte)(crc >> 24);
            if (size > 0)
                Array.Copy(frameData, written, dg, HeaderSize, size);
            datagrams.Add(dg);
            written += size;
        }

        return datagrams;
    }

    /// <summary>
    /// Mirrors PONKInput.ProcessPacket: validate header, reassemble per frame number
    /// (any order, zero-length chunks stored), verify the byte-sum crc.
    /// </summary>
    private static byte[]? Reassemble(IEnumerable<byte[]> datagrams, int expectedDatagramCount)
    {
        var chunks = new Dictionary<int, byte[]>();
        byte chunkCount = 0;
        uint crc = 0;
        var received = 0;

        foreach (var dg in datagrams)
        {
            received++;
            Assert.True(dg.Length >= HeaderSize, "datagram shorter than the 52-byte header");
            Assert.Equal("PONK-UDP", Encoding.ASCII.GetString(dg, 0, 8));
            Assert.Equal(0, dg[8]);
            Assert.True(dg[46] > 0, "chunk count must be positive");
            Assert.True(dg[47] < dg[46], "chunk number out of range");

            if (chunks.Count == 0)
            {
                chunkCount = dg[46];
                crc = (uint)(dg[48] | (dg[49] << 8) | (dg[50] << 16) | (dg[51] << 24));
            }

            var payloadLength = dg.Length - HeaderSize;
            var payload = new byte[payloadLength];
            Array.Copy(dg, HeaderSize, payload, 0, payloadLength);
            chunks[dg[47]] = payload;

            if (chunks.Count < chunkCount)
                continue;

            var frameData = chunks.OrderBy(kvp => kvp.Key).SelectMany(kvp => kvp.Value).ToArray();
            Assert.Equal(expectedDatagramCount, received);
            // A crc mismatch means the frame is dropped (PONKInput behavior): return null
            return Checksum(frameData) == crc ? frameData : null;
        }

        return null;
    }

    private static List<(float X, float Y, byte R, byte G, byte B)> ParsePoints(byte[] frameData)
    {
        var result = new List<(float, float, byte, byte, byte)>();
        var offset = 0;
        while (offset < frameData.Length)
        {
            var format = frameData[offset++];
            Assert.Equal(1, format);
            var metaCount = frameData[offset++];
            offset += metaCount * 12;
            var pointCount = frameData[offset] | (frameData[offset + 1] << 8);
            offset += 2;
            for (var i = 0; i < pointCount; i++)
            {
                var x = BitConverter.ToSingle(frameData, offset);
                var y = BitConverter.ToSingle(frameData, offset + 4);
                result.Add((x, y, frameData[offset + 8], frameData[offset + 9], frameData[offset + 10]));
                offset += 11;
            }
        }

        return result;
    }

    private static (float, float, byte, byte, byte)[] MakePoints(int count)
    {
        var pts = new (float, float, byte, byte, byte)[count];
        for (var i = 0; i < count; i++)
        {
            var t = i / (float)Math.Max(1, count - 1);
            var angle = t * (float)Math.PI * 2;
            pts[i] = ((float)Math.Sin(angle), (float)Math.Cos(angle),
                      (byte)(i * 7), (byte)(i * 13), (byte)(i * 29));
        }

        return pts;
    }

    [Fact]
    public void SenderPathMetadata_WritesTheFullEightByteKey()
    {
        // The metadata key field is exactly 8 bytes and the protocol's keys use all of them
        // ("PATHNUMB", "MAXSPEED"). The sender used to keep a terminator slot by truncating to 7
        // bytes, so receivers looking up the key saw "MAXSPEE" and ignored the render hint.
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        T3.Core.Output.LaserOutputStream.WriteMetaEntry(writer, "MAXSPEED", 1.5f);
        writer.Flush();

        var entry = ms.ToArray();
        Assert.Equal(12, entry.Length);
        Assert.Equal("MAXSPEED", Encoding.ASCII.GetString(entry, 0, 8));
        Assert.Equal(1.5f, BitConverter.ToSingle(entry, 8));

        // A shorter key is still zero-padded, and the float follows the 8-byte field.
        using var ms2 = new MemoryStream();
        using var writer2 = new BinaryWriter(ms2);
        T3.Core.Output.LaserOutputStream.WriteMetaEntry(writer2, "PATH", 2f);
        writer2.Flush();
        var shortEntry = ms2.ToArray();
        Assert.Equal("PATH\0\0\0\0", Encoding.ASCII.GetString(shortEntry, 0, 8));
    }

    [Fact]
    public void SingleChunkDatagram_MatchesSpecOffsets()
    {
        var points = MakePoints(40);
        var frameData = BuildPathData(points);
        var datagrams = EncodeDatagrams(frameData, senderId: 123456789, senderName: "TiXL", frameNumber: 7);

        var dg = Assert.Single(datagrams);
        // 52-byte header + 28 bytes path/point counts + 40 * 11-byte points
        Assert.Equal(HeaderSize + 28 + points.Length * 11, dg.Length);
        Assert.True(dg.Length <= 1472, "datagram must respect PONK_MAX_CHUNK_SIZE");

        Assert.Equal("PONK-UDP", Encoding.ASCII.GetString(dg, 0, 8));
        Assert.Equal(0, dg[8]); // protocol version
        Assert.Equal(123456789, dg[9] | (dg[10] << 8) | (dg[11] << 16) | (dg[12] << 24));
        Assert.Equal("TiXL", Encoding.ASCII.GetString(dg, 13, 4));
        Assert.All(dg[17..45], b => Assert.Equal(0, b)); // name null padding
        Assert.Equal(7, dg[45]); // frame number
        Assert.Equal(1, dg[46]); // chunk count
        Assert.Equal(0, dg[47]); // chunk number

        var expectedCrc = Checksum(frameData);
        Assert.Equal(expectedCrc, (uint)(dg[48] | (dg[49] << 8) | (dg[50] << 16) | (dg[51] << 24)));

        // The crc field occupies 48-51: payload bytes must start at 52, not 48
        Assert.Equal(frameData, dg[HeaderSize..]);
    }

    [Fact]
    public void MultiChunkFrame_ReassemblesAcrossRealUdp()
    {
        // 3000 points -> 33028 bytes of frame data -> 24 chunks
        var points = MakePoints(3000);
        var frameData = BuildPathData(points);
        var datagrams = EncodeDatagrams(frameData, senderId: 42, senderName: "TiXL", frameNumber: 1);
        Assert.True(datagrams.Count > 1, "frame must span multiple chunks for this test");
        Assert.All(datagrams, dg => Assert.True(dg.Length <= 1472));

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        udp.Client.ReceiveTimeout = 5000;
        var target = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)udp.Client.LocalEndPoint!).Port);

        // Reverse delivery order: reassembly must not depend on chunk arrival order
        foreach (var dg in datagrams.AsEnumerable().Reverse())
            Assert.Equal(dg.Length, udp.Send(dg, dg.Length, target));

        var received = new List<byte[]>();
        while (received.Count < datagrams.Count)
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            received.Add(udp.Receive(ref remote));
        }

        var reassembled = Reassemble(received, datagrams.Count);
        Assert.NotNull(reassembled);
        Assert.Equal(frameData, reassembled);
        Assert.Equal(points, ParsePoints(reassembled!));
    }

    [Fact]
    public void ExactMultipleFrame_WithEmptyTrailingChunk_Reassembles()
    {
        // Canonical senders append a zero-payload chunk when the frame data is an exact
        // multiple of the chunk quantum; the receiver must count it or the frame stalls.
        var frameData = new byte[MaxChunkPayload * 2];
        new Random(1234).NextBytes(frameData);
        var datagrams = EncodeDatagrams(frameData, senderId: 1, senderName: "S", frameNumber: 9,
                                        canonicalTrailingEmptyChunk: true);
        Assert.Equal(3, datagrams.Count);
        Assert.Equal(HeaderSize, datagrams[2].Length); // trailing chunk is header-only

        var reassembled = Reassemble(datagrams, 3);
        Assert.NotNull(reassembled);
        Assert.Equal(frameData, reassembled);
    }

    [Fact]
    public void CorruptedFrame_FailsChecksum()
    {
        var frameData = BuildPathData(MakePoints(200));
        var datagrams = EncodeDatagrams(frameData, senderId: 1, senderName: "TiXL", frameNumber: 2);
        datagrams[^1][^1] ^= 0xFF; // flip a payload bit in the last chunk

        // The receiver drops the frame when the byte-sum no longer matches the header crc
        var reassembled = Reassemble(datagrams, datagrams.Count);
        Assert.Null(reassembled);
    }
}
