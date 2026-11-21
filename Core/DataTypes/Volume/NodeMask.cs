#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace T3.Core.DataTypes.Volume;

/// <summary>
/// Fixed-size bitset over a node's voxel/child slots, matching OpenVDB's openvdb::util::NodeMask.
/// Bit n lives in word n &gt;&gt; 6 at bit n &amp; 63; words serialize as little-endian uint64,
/// which is the on-disk .vdb encoding.
/// </summary>
public sealed class NodeMask
{
    public readonly ulong[] Words;

    public NodeMask(int wordCount)
    {
        Words = new ulong[wordCount];
    }

    private NodeMask(ulong[] words)
    {
        Words = words;
    }

    public NodeMask Clone() => new((ulong[])Words.Clone());

    /// <summary>Copies another mask's words (sizes must match).</summary>
    public void FillFrom(NodeMask other)
    {
        var src = other.Words;
        var dst = Words;
        var count = Math.Min(src.Length, dst.Length);
        for (var i = 0; i < count; i++)
            dst[i] = src[i];
    }

    public int WordCount => Words.Length;
    public int Size => Words.Length << 6;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetOn(int bit) => Words[bit >> 6] |= 1ul << (bit & 63);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetOff(int bit) => Words[bit >> 6] &= ~(1ul << (bit & 63));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int bit, bool on)
    {
        if (on)
            SetOn(bit);
        else
            SetOff(bit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsOn(int bit) => (Words[bit >> 6] & (1ul << (bit & 63))) != 0;

    public void Clear() => Array.Clear(Words);

    public void SetAll() => Fill(ulong.MaxValue);

    public void Fill(ulong pattern)
    {
        var words = Words;
        for (var i = 0; i < words.Length; i++)
            words[i] = pattern;
    }

    public int CountOn()
    {
        var count = 0;
        foreach (var w in Words)
            count += System.Numerics.BitOperations.PopCount(w);
        return count;
    }

    public int CountOff() => Size - CountOn();

    public bool IsEmpty()
    {
        foreach (var w in Words)
        {
            if (w != 0)
                return false;
        }

        return true;
    }

    /// <summary>True when every bit in the final word that belongs to the mask is set.</summary>
    public bool IsFull()
    {
        var words = Words;
        var lastBits = Size & 63;
        for (var i = 0; i < words.Length; i++)
        {
            if (i == words.Length - 1 && lastBits != 0)
            {
                var valid = (1ul << lastBits) - 1;
                if ((words[i] & valid) != valid)
                    return false;
            }
            else if (words[i] != ulong.MaxValue)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Serializes to little-endian uint64 words (the .vdb/.nvdb on-disk encoding).</summary>
    public void Save(System.IO.BinaryWriter writer)
    {
        var words = Words;
        for (var i = 0; i < words.Length; i++)
            writer.Write(words[i]);
    }

    public void Save(System.IO.Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        var words = Words;
        for (var i = 0; i < words.Length; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buffer, words[i]);
            stream.Write(buffer);
        }
    }

    public static NodeMask Load(System.IO.Stream stream, int wordCount)
    {
        var mask = new NodeMask(wordCount);
        Span<byte> buffer = stackalloc byte[8];
        for (var i = 0; i < wordCount; i++)
        {
            stream.ReadExactly(buffer);
            mask.Words[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        }

        return mask;
    }

    /// <summary>Enumerates offsets of CLEAR bits below <paramref name="size"/> in ascending order.</summary>
    public IEnumerable<int> OffBits(int size)
    {
        for (var i = 0; i < size; i++)
        {
            if (!IsOn(i))
                yield return i;
        }
    }

    /// <summary>Enumerates offsets of set bits in ascending order.</summary>
    public IEnumerable<int> OnBits()
    {
        var words = Words;
        for (var w = 0; w < words.Length; w++)
        {
            var word = words[w];
            while (word != 0)
            {
                var bit = System.Numerics.BitOperations.TrailingZeroCount(word);
                yield return (w << 6) + bit;
                word &= word - 1;
            }
        }
    }
}
