#nullable enable
using System;
using System.IO;
using System.IO.Compression;

namespace T3.Core.DataTypes.Volume.IO;

/// <summary>
/// LZ4 block-format decoder (RFC-free reference implementation of the block wire format used
/// inside Blosc), plus a store-only encoder used by tests to build valid LZ4 streams without
/// native dependencies. Cross-platform, no P/Invoke.
/// </summary>
public static class Lz4Block
{
    /// <summary>Decodes one LZ4 block. <paramref name="outputSize"/> must be the exact decompressed length.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> input, int outputSize)
    {
        var output = new byte[outputSize];
        var src = 0;
        var dst = 0;

        while (src < input.Length)
        {
            var token = input[src++];

            // Literal run
            var literalLength = token >> 4;
            if (literalLength == 15)
            {
                byte more;
                do
                {
                    more = input[src++];
                    literalLength += more;
                } while (more == 255);
            }

            if (literalLength > 0)
            {
                input.Slice(src, literalLength).CopyTo(output.AsSpan(dst));
                src += literalLength;
                dst += literalLength;
            }

            if (src >= input.Length)
                break;

            // Match
            var offset = input[src] | (input[src + 1] << 8);
            src += 2;
            if (offset == 0)
                throw new InvalidDataException("LZ4: invalid offset 0");

            var matchLength = token & 0x0F;
            if (matchLength == 15)
            {
                byte more;
                do
                {
                    more = input[src++];
                    matchLength += more;
                } while (more == 255);
            }

            matchLength += 4; // minimum match length

            var matchPos = dst - offset;
            if (matchPos < 0)
                throw new InvalidDataException("LZ4: offset before start");

            // Byte-by-byte copy handles overlapping matches correctly.
            for (var i = 0; i < matchLength; i++)
                output[dst++] = output[matchPos + i];
        }

        if (dst != outputSize)
            throw new InvalidDataException($"LZ4: decompressed {dst} bytes, expected {outputSize}");
        return output;
    }

    /// <summary>
    /// Encodes as a sequence of literal-only LZ4 instructions (always valid LZ4, no compression
    /// win). Good enough for tests and tiny payloads.
    /// </summary>
    /// <summary>
    /// Encodes input that consists of a repeating block (the test helper's data shape) as one
    /// literal run followed by one long match - always-valid LZ4 that exercises the match path.
    /// </summary>
    public static byte[] CompressRepeating(ReadOnlySpan<byte> input)
    {
        // Find the smallest period p with input[i] == input[i - p] for all i >= p.
        var period = 0;
        for (var p = 1; p <= input.Length / 2; p++)
        {
            var ok = true;
            for (var i = p; i < input.Length; i++)
            {
                if (input[i] != input[i - p])
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                period = p;
                break;
            }
        }

        if (period == 0)
            period = input.Length; // no repetition: one literal run (valid only as final run)

        using var ms = new MemoryStream(input.Length / 255 + 16);
        var literalCount = Math.Min(period, input.Length);
        var matchLength = input.Length - literalCount;
        var encodedMatch = matchLength > 0 ? Math.Min(matchLength - 4, 15) : 0;

        // The token carries both nibbles: literal length and (match length - 4).
        ms.WriteByte((byte)((Math.Min(literalCount, 15) << 4) | encodedMatch));

        if (literalCount >= 15)
        {
            var extra = literalCount - 15;
            while (extra >= 255)
            {
                ms.WriteByte(255);
                extra -= 255;
            }

            ms.WriteByte((byte)extra);
        }

        ms.Write(input.Slice(0, literalCount));

        if (matchLength > 0)
        {
            var offset = period;
            ms.WriteByte((byte)(offset & 0xFF));
            ms.WriteByte((byte)(offset >> 8));

            var extra = matchLength - 4 - encodedMatch;
            while (extra >= 255)
            {
                ms.WriteByte(255);
                extra -= 255;
            }

            if (extra > 0)
                ms.WriteByte((byte)extra);
        }

        return ms.ToArray();
    }
}

/// <summary>
/// Blosc frame decoder covering the compressor/filter combinations OpenVDB produces
/// (LZ4/LZ4HC and ZLIB payloads, byte-shuffle and bit-shuffle filters, memcpy blocks).
/// Blosc frame: 16-byte header, then either the raw bytes (memcpy flag) or an int32 block-offset table followed by the blocks.
/// </summary>
public static class Blosc
{
    public const int HeaderLength = 16;

    /// <summary>Decompresses a blosc frame produced by c-blosc 1.x.</summary>
    /// <param name="maxOutputBytes">
    /// Upper bound for the declared output size. That size comes from the frame header, so a
    /// truncated or hostile file could otherwise request an arbitrarily large allocation. Callers
    /// know how many bytes their block holds and should pass it.
    /// </param>
    public static byte[] Decompress(ReadOnlySpan<byte> input, int maxOutputBytes = int.MaxValue)
    {
        if (input.Length < HeaderLength)
            throw new InvalidDataException("Blosc: frame too small");

        var flags = input[2];
        var typeSize = Math.Max((int)input[3], 1);
        var nbytes = (int)ReadUInt32(input, 4);
        var blockSize = (int)ReadUInt32(input, 8);
        var cbytes = (int)ReadUInt32(input, 12);

        if (nbytes <= 0 || blockSize <= 0 || cbytes <= 0)
            throw new InvalidDataException("Blosc: invalid header sizes");
        if (cbytes > input.Length)
            throw new InvalidDataException("Blosc: header size exceeds input");
        if (nbytes > maxOutputBytes)
            throw new InvalidDataException($"Blosc: declared output of {nbytes} bytes exceeds the expected {maxOutputBytes}");

        var output = new byte[nbytes];

        // Incompressible data is stored verbatim (and unshuffled) right after the header.
        if ((flags & 0x2) != 0)
        {
            if (HeaderLength + nbytes > input.Length)
                throw new InvalidDataException("Blosc: truncated memcpy frame");

            input.Slice(HeaderLength, nbytes).CopyTo(output);
            return output;
        }

        var doShuffle = (flags & 0x1) != 0;
        var doBitShuffle = (flags & 0x4) != 0;
        var dontSplit = (flags & 0x10) != 0;
        var compressor = (flags >> 5) & 0x7;

        var blockCount = (nbytes + blockSize - 1) / blockSize;
        if (HeaderLength + 4 * blockCount > input.Length)
            throw new InvalidDataException("Blosc: truncated block table");

        for (var block = 0; block < blockCount; block++)
        {
            var blockStart = (int)ReadUInt32(input, HeaderLength + 4 * block);
            var blockEnd = block + 1 < blockCount ? (int)ReadUInt32(input, HeaderLength + 4 * (block + 1)) : cbytes;
            var dstOffset = block * blockSize;
            var bsize = Math.Min(blockSize, nbytes - dstOffset);
            var isLeftover = bsize != blockSize;

            if (blockStart < HeaderLength || blockEnd > input.Length || blockStart >= blockEnd)
                throw new InvalidDataException("Blosc: invalid block offsets");

            var decoded = DecodeBlock(input[blockStart..blockEnd], compressor, bsize, typeSize, dontSplit, isLeftover);
            var destination = output.AsSpan(dstOffset, bsize);
            if (doBitShuffle)
            {
                decoded.CopyTo(destination);
                BitUnshuffleImpl(destination, typeSize);
            }
            else if (doShuffle && typeSize > 1)
            {
                ByteUnshuffle(decoded, destination, typeSize);
            }
            else
            {
                decoded.CopyTo(destination);
            }
        }

        return output;
    }

    /// <summary>
    /// Decodes one block: a sequence of [int32 compressed size][stream] splits, usually one per type-size byte
    /// plane. Different c-blosc versions disagree on when to split, so the layout that consumes the block
    /// span exactly is the one that was used.
    /// </summary>
    private static byte[] DecodeBlock(ReadOnlySpan<byte> block, int compressor, int bsize, int typeSize, bool dontSplit, bool isLeftover)
    {
        var splitFirst = !dontSplit && !isLeftover && typeSize > 1 && typeSize <= 16 && bsize % typeSize == 0;
        var candidates = splitFirst ? new[] { typeSize, 1 } : new[] { 1, typeSize };

        InvalidDataException? failure = null;
        foreach (var splits in candidates)
        {
            if (splits > 1 && (typeSize <= 1 || bsize % splits != 0))
                continue;

            try
            {
                return DecodeSplits(block, compressor, bsize, splits);
            }
            catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException)
            {
                failure = new InvalidDataException($"Blosc: block does not decode with {splits} split(s): {e.Message}");
            }
        }

        throw failure ?? new InvalidDataException("Blosc: block does not decode");
    }

    private static byte[] DecodeSplits(ReadOnlySpan<byte> block, int compressor, int bsize, int splits)
    {
        var output = new byte[bsize];
        var splitSize = bsize / splits;
        var src = 0;
        for (var split = 0; split < splits; split++)
        {
            var compressedSize = (int)ReadUInt32(block, src);
            src += 4;
            if (compressedSize < 0 || src + compressedSize > block.Length)
                throw new InvalidDataException("split exceeds block");

            var stream = block.Slice(src, compressedSize);
            src += compressedSize;
            var target = output.AsSpan(split * splitSize, splitSize);

            // A split that did not shrink is stored as is.
            if (compressedSize == splitSize)
            {
                stream.CopyTo(target);
                continue;
            }

            var decompressed = compressor switch
            {
                1 => Lz4Block.Decompress(stream, splitSize),
                3 => DecompressZlib(stream, splitSize),
                _ => throw new NotSupportedException($"Blosc compressor {compressor} not supported (need LZ4 or ZLIB)"),
            };
            decompressed.CopyTo(target);
        }

        if (src != block.Length)
            throw new InvalidDataException("splits do not consume the block");

        return output;
    }

    private static byte[] DecompressZlib(ReadOnlySpan<byte> input, int outputSize)
    {
        var output = new byte[outputSize];
        // The payload is raw zlib data; wrap in a seekable stream for ZLibStream.
        using var source = new MemoryStream(input.ToArray());
        using var zlib = new ZLibStream(source, CompressionMode.Decompress);
        var read = 0;
        while (read < outputSize)
        {
            var n = zlib.Read(output, read, outputSize - read);
            if (n <= 0)
                throw new InvalidDataException("Blosc: zlib stream ended early");
            read += n;
        }

        return output;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    /// <summary>Inverse of blosc byte-shuffle within one block: each byte plane is contiguous, trailing bytes are verbatim.</summary>
    private static void ByteUnshuffle(byte[] shuffled, Span<byte> destination, int typeSize)
    {
        var itemCount = shuffled.Length / typeSize;
        var bodyLength = itemCount * typeSize;
        for (var i = 0; i < itemCount; i++)
        {
            for (var j = 0; j < typeSize; j++)
                destination[i * typeSize + j] = shuffled[j * itemCount + i];
        }

        for (var i = bodyLength; i < shuffled.Length; i++)
            destination[i] = shuffled[i];
    }

    private static void BitUnshuffleImpl(Span<byte> data, int typeSize)
    {
        // bitshuffle transposes bits within blocks of 8 * typeSize bytes. Reversing it:
        // for each block, output bit i of element e comes from bit-plane layout.
        var blockBytes = 8 * typeSize;
        var fullBlocks = data.Length / blockBytes;

        for (var block = 0; block < fullBlocks; block++)
        {
            var srcOffset = block * blockBytes;
            var src = data.Slice(srcOffset, blockBytes).ToArray();
            var dst = data.Slice(srcOffset, blockBytes);

            // bitshuffle writes bit (element, bit) to position bit*8 + element within the block.
            for (var element = 0; element < 8 * typeSize; element++)
            {
                byte value = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var srcByte = src[bit * typeSize + element / 8];
                    var srcBit = (srcByte >> (7 - element % 8)) & 1;
                    value |= (byte)(srcBit << (7 - bit));
                }

                dst[element] = value;
            }
        }

        // Trailing partial block was stored unshuffled - nothing to do.
    }
}
