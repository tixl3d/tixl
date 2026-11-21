#nullable enable
using System;
using System.IO;
using System.Numerics;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.DataTypes.Volume.Tools;
using Xunit;

namespace Core.Tests.Volume;

public class VdbFileTests
{
    private static VdbVolume CreateTestVolume()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        grid.Name = "LS";
        return new VdbVolume(grid);
    }

    [Fact]
    public void RoundTripUncompressed()
    {
        var volume = CreateTestVolume();
        using var ms = new MemoryStream();
        VdbFile.Save(volume, ms);
        ms.Position = 0;

        var loaded = VdbFile.Load(ms);
        var original = volume.GetFloatGrid()!;
        var restored = loaded.GetFloatGrid()!;

        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Transform.VoxelSize, restored.Transform.VoxelSize, 5);
        Assert.Equal(original.ActiveVoxelCount, restored.ActiveVoxelCount);

        // Spot-check values across the band.
        foreach (var probe in new[] { Vector3.Zero, new Vector3(0.5f, 0, 0), new Vector3(0.6f, 0.1f, 0), new Vector3(1f, 0, 0) })
        {
            var expected = original.Tree.GetValue(original.Transform.WorldToIndex(probe));
            var actual = restored.Tree.GetValue(restored.Transform.WorldToIndex(probe));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void RoundTripZipCompressed()
    {
        var volume = CreateTestVolume();
        using var ms = new MemoryStream();
        VdbFile.Save(volume, ms, zipCompression: true);
        ms.Position = 0;

        var loaded = VdbFile.Load(ms);
        var original = volume.GetFloatGrid()!;
        var restored = loaded.GetFloatGrid()!;

        Assert.Equal(original.ActiveVoxelCount, restored.ActiveVoxelCount);
        var expected = original.Tree.GetValue(original.Transform.WorldToIndex(Vector3.Zero));
        var actual = restored.Tree.GetValue(restored.Transform.WorldToIndex(Vector3.Zero));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RoundTripVec3Grid()
    {
        var grid = new VdbGrid<Vector3>(Vector3.Zero, 0.1f, "vel", VdbGridClass.Staggered);
        for (var z = 0; z < 16; z++)
        {
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    grid.Tree.SetValueOn(new VdbCoord(x, y, z), new Vector3(x, y, z) * 0.25f);
                }
            }
        }

        using var ms = new MemoryStream();
        VdbFile.Save(new VdbVolume(grid), ms);
        ms.Position = 0;

        var loaded = VdbFile.Load(ms);
        var restored = loaded.GetVectorGrid("vel");
        Assert.NotNull(restored);

        var v = restored.Tree.GetValue(new VdbCoord(5, 9, 3));
        Assert.Equal(new Vector3(5, 9, 3) * 0.25f, v);
    }

    [Fact]
    public void RoundTripRootTiles()
    {
        var grid = new VdbGrid<float>(1f, 0.1f, "tiled");
        grid.Tree.Root.Tiles[new VdbCoord(0, 0, 0)] = new VdbRootTile<float> { Value = 5f, Active = true };
        grid.Tree.Root.Tiles[new VdbCoord(-4096, 0, 0)] = new VdbRootTile<float> { Value = 2f, Active = false };

        using var ms = new MemoryStream();
        VdbFile.Save(new VdbVolume(grid), ms);
        ms.Position = 0;

        var restored = VdbFile.Load(ms).GetFloatGrid()!;
        Assert.True(restored.Tree.IsValueOn(new VdbCoord(10, 10, 10)));
        Assert.Equal(5f, restored.Tree.GetValue(new VdbCoord(10, 10, 10)));
        Assert.False(restored.Tree.IsValueOn(new VdbCoord(-4000, 3, 7)));
        Assert.Equal(2f, restored.Tree.GetValue(new VdbCoord(-4000, 3, 7)));
    }

    [Fact]
    public void HeaderMatchesOpenVdbFormat()
    {
        var volume = CreateTestVolume();
        using var ms = new MemoryStream();
        VdbFile.Save(volume, ms);
        var bytes = ms.ToArray();

        // int64 magic: 0x20 42 44 56 00 00 00 00 (little endian "VDB ")
        Assert.Equal(0x20, bytes[0]);
        Assert.Equal(0x42, bytes[1]);
        Assert.Equal(0x44, bytes[2]);
        Assert.Equal(0x56, bytes[3]);
        Assert.Equal(0, bytes[4]);
        Assert.Equal(0, bytes[5]);
        Assert.Equal(0, bytes[6]);
        Assert.Equal(0, bytes[7]);

        // uint32 file version 225 (0xE1)
        Assert.Equal((byte)225, bytes[8]);
    }

    [Fact]
    public void ScanGridsListsGrids()
    {
        var volume = CreateTestVolume();
        var path = Path.Combine(Path.GetTempPath(), $"tixl_vdbtest_{Guid.NewGuid():N}.vdb");
        try
        {
            VdbFile.Save(volume, path);
            var grids = VdbFile.ScanGrids(path);
            Assert.Single(grids);
            Assert.Equal("LS", grids[0].Name);
            // Grids are identified by their serialized tree type, e.g. "Tree_float_5_4_3".
            Assert.Contains("float", grids[0].Type);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class NanoVdbTests
{
    [Fact]
    public void BufferIsAlignedAndSized()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var buffer = NanoVdbBuffer.FromGrid(grid);

        Assert.Equal(NanoVdbBuffer.GridDataSize, 672);
        Assert.True(buffer.Buffer.Length % 32 == 0, $"buffer size {buffer.Buffer.Length} not 32B aligned");

        // Magic "NanoVDB0" little-endian.
        Assert.Equal("NanoVDB0", System.Text.Encoding.ASCII.GetString(buffer.Buffer, 0, 8));

        // Grid size field (offset 32) must equal the buffer length.
        var gridSize = BitConverter.ToUInt64(buffer.Buffer, 32);
        Assert.Equal((ulong)buffer.Buffer.Length, gridSize);
    }

    [Fact]
    public void SummaryMatchesTree()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var buffer = NanoVdbBuffer.FromGrid(grid);

        var summary = buffer.ReadSummary();
        Assert.Equal(grid.Tree.LeafCount, summary.LeafNodes);
        Assert.Equal(grid.ActiveVoxelCount, summary.VoxelCount);
        Assert.Equal("LS", summary.Name);
        Assert.True(summary.UpperNodes > 0);
        Assert.True(summary.LowerNodes > 0);
    }

    [Fact]
    public void FileRoundTripPreservesBuffer()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var buffer = NanoVdbBuffer.FromGrid(grid);

        using var ms = new MemoryStream();
        buffer.Save(ms);
        ms.Position = 0;

        var loaded = NanoVdbBuffer.Load(ms);
        Assert.Equal(buffer.Buffer.Length, loaded.Buffer.Length);
        Assert.Equal(VdbValueType.Float, loaded.ValueType);
        Assert.Equal(buffer.Buffer, loaded.Buffer);
    }

    [Fact]
    public void LoadFileReadsAGridWithoutTheFileHeader()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var written = NanoVdbBuffer.FromGrid(grid);

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tixl-{Guid.NewGuid():N}.nvdb");
        try
        {
            written.Save(path);

            var loaded = NanoVdbBuffer.LoadFile(path);

            Assert.Equal(written.Buffer.Length, loaded.Buffer.Length);
            Assert.Equal(written.Buffer, loaded.Buffer);
            Assert.True(loaded.TryValidate(out var error), error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadInfoMatchesTheSourceGrid()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        grid.Name = "density";
        var buffer = NanoVdbBuffer.FromGrid(grid);

        var info = buffer.ReadInfo();

        Assert.Equal("density", info.Name);
        Assert.Equal(grid.Transform.VoxelSize, info.VoxelSize, 4);
        Assert.Equal(VdbValueType.Float, info.ValueType);
        Assert.Equal(buffer.ReadSummary().VoxelCount, info.VoxelCount);
        Assert.True(info.WorldMax.X > info.WorldMin.X);
    }

    [Fact]
    public void ValidationRejectsTreeOffsetsOutsideTheBuffer()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var buffer = NanoVdbBuffer.FromGrid(grid);
        Assert.True(buffer.TryValidate(out _));

        // Point the leaf section far past the end, the way a truncated or forged file would.
        var treeStart = NanoVdbBuffer.GridDataSize;
        BitConverter.TryWriteBytes(buffer.Buffer.AsSpan(treeStart), (ulong)(buffer.Buffer.Length * 4L));

        Assert.False(buffer.TryValidate(out var error));
        Assert.Contains("outside the buffer", error);
    }

    [Fact]
    public void RootKeyPackingMatchesNanoVdb()
    {
        // NanoVDB: key = ((x>>12)&0x1FFFFF)<<42 | ((y>>12)&0x1FFFFF)<<21 | ((z>>12)&0x1FFFFF).
        const ulong mask21 = (1UL << 21) - 1;
        var expected = ((1UL & mask21) << 42) | (((unchecked((ulong)(-4096 >> 12))) & mask21) << 21) | ((8192UL >> 12) & mask21);
        var key = NanoVdbBuffer.RootCoordToKey(new VdbCoord(4096, -4096, 8192));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void TreeDataOffsetsAreWithinBuffer()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var buffer = NanoVdbBuffer.FromGrid(grid);

        var treeStart = NanoVdbBuffer.GridDataSize;
        var leafOffset = BitConverter.ToUInt64(buffer.Buffer, treeStart);
        var lowerOffset = BitConverter.ToUInt64(buffer.Buffer, treeStart + 8);
        var upperOffset = BitConverter.ToUInt64(buffer.Buffer, treeStart + 16);
        var rootOffset = BitConverter.ToUInt64(buffer.Buffer, treeStart + 24);

        // Breadth-first: leaf section is last, root first.
        Assert.True(rootOffset < upperOffset);
        Assert.True(upperOffset < lowerOffset);
        Assert.True(lowerOffset < leafOffset);
        Assert.Equal((ulong)NanoVdbBuffer.TreeDataSize, rootOffset);
        Assert.True(leafOffset < (ulong)buffer.Buffer.Length);
    }

    [Fact]
    public void ShaderLeafLayoutMatchesTheWriter()
    {
        // The compute shaders read leaves through constants of their own (nanovdb-common.hlsl):
        // bbox at +0, value mask at +16, values at +96, and a stride of 96 + 512 * 4 bytes. Those
        // numbers are duplicated in HLSL, so a change to the writer has to break a test here
        // rather than silently scramble what the GPU reads.
        const int shaderLeafValuesOffset = 96;
        const int shaderLeafMaskOffset = 16;
        const int shaderLeafStride = 96 + 512 * 4;
        const int shaderLeafValueMaskWords = 16;   // 64 bytes of mask

        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var buffer = NanoVdbBuffer.FromGrid(grid);
        var summary = buffer.ReadSummary();
        Assert.True(summary.LeafNodes > 0, "the test grid should have leaves");

        var treeStart = NanoVdbBuffer.GridDataSize;
        var leafSection = treeStart + (long)BitConverter.ToUInt64(buffer.Buffer, treeStart);

        // Every leaf the shader would visit has to fit, with its values and mask inside it.
        Assert.True(leafSection + (long)summary.LeafNodes * shaderLeafStride <= buffer.Buffer.Length,
                    "shader leaf stride runs past the buffer the writer produced");

        // Walk the first few leaves the way the shader does and check each voxel value against the
        // tree. A mask or stride that disagreed with the writer would land on a different number.
        var leaves = CollectLeaves(grid.Tree);
        Assert.Equal(summary.LeafNodes, leaves.Count);

        for (var index = 0; index < Math.Min(leaves.Count, 8); index++)
        {
            var leaf = leaves[index];
            var address = (int)(leafSection + index * shaderLeafStride);

            Assert.Equal(leaf.Origin.X, BitConverter.ToInt32(buffer.Buffer, address));
            Assert.Equal(leaf.Origin.Y, BitConverter.ToInt32(buffer.Buffer, address + 4));
            Assert.Equal(leaf.Origin.Z, BitConverter.ToInt32(buffer.Buffer, address + 8));

            var maskOffset = address + shaderLeafMaskOffset;
            for (var slot = 0; slot < 512; slot++)
            {
                var word = BitConverter.ToUInt32(buffer.Buffer, maskOffset + (slot >> 5) * 4);
                var active = (word & (1u << (slot & 31))) != 0;
                if (active != leaf.ValueMask.IsOn(slot))
                    Assert.Fail($"leaf {index} slot {slot}: shader mask says {active}, tree says {leaf.ValueMask.IsOn(slot)}");
            }

            Assert.Equal(shaderLeafValueMaskWords, 64 / sizeof(uint));

            for (var slot = 0; slot < 512; slot++)
            {
                var value = BitConverter.ToSingle(buffer.Buffer, address + shaderLeafValuesOffset + slot * 4);
                if (value != leaf.Values[slot])
                    Assert.Fail($"leaf {index} slot {slot}: shader reads {value}, tree holds {leaf.Values[slot]}");
            }
        }
    }

    /// <summary>
    /// Leaves in the order the writer emits them - sorted by Z, then Y, then X, the same key the
    /// buffer builder uses - which is the order the shaders index them by.
    /// </summary>
    private static List<VdbLeafNode<float>> CollectLeaves(VdbTree<float> tree)
    {
        var leaves = new List<VdbLeafNode<float>>();
        foreach (var upper in tree.Root.Children.Values)
        {
            foreach (var lower in upper.Children)
            {
                if (lower == null)
                    continue;

                foreach (var leaf in lower.Children)
                {
                    if (leaf != null)
                        leaves.Add(leaf);
                }
            }
        }

        leaves.Sort((a, b) => a.Origin.Z != b.Origin.Z ? a.Origin.Z.CompareTo(b.Origin.Z)
                                  : a.Origin.Y != b.Origin.Y ? a.Origin.Y.CompareTo(b.Origin.Y)
                                      : a.Origin.X.CompareTo(b.Origin.X));
        return leaves;
    }
}

public class Lz4BloscTests
{
    [Fact]
    public void Lz4RepeatingRoundTrip()
    {
        // A repeating block exercises literals, extended length codes and the match copy path.
        var data = new byte[1000];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(i % 16);

        var compressed = Lz4Block.CompressRepeating(data);
        var decompressed = Lz4Block.Decompress(compressed, data.Length);
        Assert.Equal(data, decompressed);
    }

    [Fact]
    public void BloscMemcpyFrameIsRawPayloadAfterHeader()
    {
        // First leaf of OpenVDB's bunny.vdb: two half floats, flags 0x33 (memcpy), type size 4, no block table.
        var frame = Convert.FromHexString("02013304040000000400000014000000b1306c30");
        Assert.Equal(Convert.FromHexString("b1306c30"), Blosc.Decompress(frame));
    }

    [Fact]
    public void BloscRejectsDeclaredOutputBeyondExpectation()
    {
        // A real 128-byte leaf block. Asking for fewer bytes than the frame declares must fail
        // instead of allocating whatever size the file chose.
        var frame = Convert.FromHexString(
            "0201310480000000800000006d0000001400000055000000ff129b714b31885d381f6e4827125333190a3921100925180d0918130f0f"
            + "1613141a3001000cff11865d3b2c7249291a5a361a0e422510092c190beb1d120b0a16110e121513161f3f0000132f1400503030303030");

        Assert.Throws<InvalidDataException>(() => Blosc.Decompress(frame, 64));
        Assert.Equal(128, Blosc.Decompress(frame, 128).Length);
    }

    [Fact]
    public void BloscDecodesRealOpenVdbFrame()
    {
        // 64 active half floats of an OpenVDB level-set leaf: byte shuffle, LZ4, type size 4, one block.
        var frame = Convert.FromHexString(
            "0201310480000000800000006d0000001400000055000000ff129b714b31885d381f6e4827125333190a3921100925180d0918130f0f"
            + "1613141a3001000cff11865d3b2c7249291a5a361a0e422510092c190beb1d120b0a16110e121513161f3f0000132f1400503030303030");

        var bytes = Blosc.Decompress(frame);

        Assert.Equal(128, bytes.Length);
        // The leaf stores signed distances within a 3 voxel (0.15) band.
        for (var i = 0; i < bytes.Length; i += 2)
        {
            var value = (float)BitConverter.ToHalf(bytes, i);
            Assert.InRange(value, -0.1501f, 0.1501f);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void BloscUnshufflesPerBlockAndHonorsSplits(int splits)
    {
        const int typeSize = 4;
        const int blockSize = 64;
        const int blockCount = 2;
        var original = new byte[blockSize * blockCount - 3];            // last block is a leftover with trailing bytes
        for (var i = 0; i < original.Length; i++)
            original[i] = (byte)(i * 7 + 1);

        var blocks = new System.Collections.Generic.List<byte[]>();
        for (var block = 0; block < blockCount; block++)
        {
            var source = original.AsSpan(block * blockSize, Math.Min(blockSize, original.Length - block * blockSize)).ToArray();
            var shuffled = new byte[source.Length];
            var itemCount = source.Length / typeSize;
            for (var i = 0; i < itemCount; i++)
            {
                for (var j = 0; j < typeSize; j++)
                    shuffled[j * itemCount + i] = source[i * typeSize + j];
            }

            for (var i = itemCount * typeSize; i < source.Length; i++)
                shuffled[i] = source[i];

            // Every split is stored as is, which a decoder recognizes by its size equalling the split length.
            var effectiveSplits = block == blockCount - 1 ? 1 : splits;
            var stream = new System.IO.MemoryStream();
            var splitSize = shuffled.Length / effectiveSplits;
            for (var split = 0; split < effectiveSplits; split++)
            {
                stream.Write(BitConverter.GetBytes(splitSize));
                stream.Write(shuffled, split * splitSize, splitSize);
            }

            blocks.Add(stream.ToArray());
        }

        var table = 16 + 4 * blockCount;
        var frame = new System.IO.MemoryStream();
        var header = new byte[16];
        header[0] = 2;
        header[1] = 1;
        header[2] = 0x1 | (1 << 5);                                     // byte shuffle, LZ4 codec, splitting allowed
        header[3] = typeSize;
        WriteU32(header, 4, (uint)original.Length);
        WriteU32(header, 8, blockSize);
        WriteU32(header, 12, (uint)(table + blocks[0].Length + blocks[1].Length));
        frame.Write(header);
        var offset = table;
        foreach (var block in blocks)
        {
            frame.Write(BitConverter.GetBytes(offset));
            offset += block.Length;
        }

        foreach (var block in blocks)
            frame.Write(block);

        Assert.Equal(original, Blosc.Decompress(frame.ToArray()));
    }

    private static void WriteU32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}
