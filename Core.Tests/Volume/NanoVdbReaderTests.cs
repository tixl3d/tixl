#nullable enable
using System;
using System.IO;
using System.Numerics;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using Xunit;

namespace Core.Tests.Volume;

/// <summary>
/// Walks the flattened NanoVDB buffer the way the HLSL accessor does (root key search, upper, lower, leaf)
/// and compares every voxel with the source tree, so the shader port starts from a verified layout.
/// </summary>
public class NanoVdbReaderTests
{
    [Fact]
    public void SphereLookupMatchesTree()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.05f, Vector3.Zero, 3);
        AssertAllVoxelsMatch(grid);
    }

    [Fact]
    public void OffsetBoxAcrossRootBoundariesMatchesTree()
    {
        // 4096-voxel root cells: a 0.01 voxel size and a large offset puts the box across several upper nodes.
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Box, new Vector3(0.4f, 0.3f, 0.2f), 0.01f, new Vector3(40.9f, -41.0f, 0.5f), 3);
        AssertAllVoxelsMatch(grid);
    }

    [Fact]
    public void LookupOutsideReturnsBackground()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var reader = new Reader(NanoVdbBuffer.FromGrid(grid).Buffer);

        Assert.Equal(grid.Background, reader.Read(new VdbCoord(100000, -100000, 50000)));
    }

    [Fact]
    public void MapAndRootBBoxAreReadable()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, new Vector3(0.25f, 0.5f, -0.75f), 3);
        var bytes = NanoVdbBuffer.FromGrid(grid).Buffer;

        // Map.matf diagonal and Map.vecf live at fixed GridData offsets the shader relies on.
        Assert.Equal(0.1f, BitConverter.ToSingle(bytes, Reader.MapMatfOffset), 6);
        Assert.Equal(1f / 0.1f, BitConverter.ToSingle(bytes, Reader.MapInvMatfOffset), 3);
        Assert.Equal(0.25f, BitConverter.ToSingle(bytes, Reader.MapVecfOffset), 6);
        Assert.Equal(0.5f, BitConverter.ToSingle(bytes, Reader.MapVecfOffset + 4), 6);
        Assert.Equal(-0.75f, BitConverter.ToSingle(bytes, Reader.MapVecfOffset + 8), 6);

        var rootOffset = NanoVdbBuffer.GridDataSize + (int)BitConverter.ToUInt64(bytes, NanoVdbBuffer.GridDataSize + 24);
        var bbox = grid.ActiveIndexBBox;
        Assert.Equal(bbox.Min.X, BitConverter.ToInt32(bytes, rootOffset));
        Assert.Equal(bbox.Max.Z, BitConverter.ToInt32(bytes, rootOffset + 20));
    }

    [Fact]
    public void Vec3GridExportIsRejected()
    {
        var grid = new VdbGrid<Vector3>(Vector3.Zero, 0.1f, "vel", VdbGridClass.Staggered);
        grid.Tree.SetValueOn(new VdbCoord(0, 0, 0), new Vector3(1f, 2f, 3f));

        Assert.Throws<NotSupportedException>(() => NanoVdbBuffer.FromGrid(grid));
    }

    [Fact]
    public void NonFloatGridTypesFailValidation()
    {
        var grid = VolumePrimitives.Create(VolumePrimitives.Shape.Sphere, new Vector3(1f, 1f, 1f), 0.1f, Vector3.Zero, 3);
        var buffer = NanoVdbBuffer.FromGrid(grid);
        using var ms = new MemoryStream();
        buffer.Save(ms);
        var bytes = ms.ToArray();
        bytes[636] = 2; // GridType::Double - the shaders only decode float and vec3-float layouts

        Assert.False(NanoVdbBuffer.LoadBytes(bytes).TryValidate(out var error));
        Assert.Contains("unsupported grid value type", error);
    }

    private static void AssertAllVoxelsMatch(VdbGrid<float> grid)
    {
        var reader = new Reader(NanoVdbBuffer.FromGrid(grid).Buffer);
        var checkedVoxels = 0;
        foreach (var leaf in grid.Tree.Leaves)
        {
            for (var i = 0; i < 512; i++)
            {
                var coord = leaf.Origin + VdbCoord.LocalFromOffset(i);
                Assert.Equal(grid.Tree.GetValue(coord), reader.Read(coord));
                checkedVoxels++;
            }
        }

        Assert.True(checkedVoxels > 0);

        // Probe a halo around the active box: inactive space resolves through lower/upper tiles to the background.
        var bbox = grid.ActiveIndexBBox;
        for (var x = bbox.Min.X - 20; x <= bbox.Max.X + 20; x += 5)
        {
            for (var y = bbox.Min.Y - 20; y <= bbox.Max.Y + 20; y += 5)
            {
                for (var z = bbox.Min.Z - 20; z <= bbox.Max.Z + 20; z += 5)
                {
                    var coord = new VdbCoord(x, y, z);
                    Assert.Equal(grid.Tree.GetValue(coord), reader.Read(coord));
                }
            }
        }
    }

    /// <summary>Reference implementation of the shader-side accessor over the raw buffer.</summary>
    internal sealed class Reader
    {
        public const int MapMatfOffset = 296;
        public const int MapInvMatfOffset = 296 + 36;
        public const int MapVecfOffset = 296 + 72;

        public Reader(byte[] buffer)
        {
            _buffer = buffer;
            var treeStart = NanoVdbBuffer.GridDataSize;
            _rootOffset = treeStart + (int)BitConverter.ToUInt64(buffer, treeStart + 24);
            _tileCount = (int)BitConverter.ToUInt32(buffer, _rootOffset + 24);
            _background = BitConverter.ToSingle(buffer, _rootOffset + 28);
            _firstTileOffset = _rootOffset + RootHeaderPadded;
        }

        public float Read(VdbCoord coord)
        {
            var key = NanoVdbBuffer.RootCoordToKey(new VdbCoord(coord.X & ~4095, coord.Y & ~4095, coord.Z & ~4095));
            for (var i = 0; i < _tileCount; i++)
            {
                var tile = _firstTileOffset + i * 32;
                if (BitConverter.ToUInt64(_buffer, tile) != key)
                    continue;

                var child = BitConverter.ToInt64(_buffer, tile + 8);
                if (child == 0)
                    return BitConverter.ToSingle(_buffer, tile + 20);

                return ReadUpper(_rootOffset + (int)child, coord);
            }

            return _background;
        }

        private float ReadUpper(int node, VdbCoord coord)
        {
            var slot = (((coord.X & 4095) >> 7) << 10) | (((coord.Y & 4095) >> 7) << 5) | ((coord.Z & 4095) >> 7);
            return ReadInternal(node, slot, UpperChildMaskOffset, UpperTableOffset, coord, ReadLower);
        }

        private float ReadLower(int node, VdbCoord coord)
        {
            var slot = (((coord.X & 127) >> 3) << 8) | (((coord.Y & 127) >> 3) << 4) | ((coord.Z & 127) >> 3);
            return ReadInternal(node, slot, LowerChildMaskOffset, LowerTableOffset, coord, ReadLeaf);
        }

        private float ReadInternal(int node, int slot, int childMaskOffset, int tableOffset, VdbCoord coord, Func<int, VdbCoord, float> readChild)
        {
            var maskWord = BitConverter.ToUInt32(_buffer, node + childMaskOffset + (slot >> 5) * 4);
            var entry = node + tableOffset + slot * 8;
            if ((maskWord & (1u << (slot & 31))) == 0)
                return BitConverter.ToSingle(_buffer, entry);

            return readChild(node + (int)BitConverter.ToInt64(_buffer, entry), coord);
        }

        private float ReadLeaf(int node, VdbCoord coord)
        {
            var offset = ((coord.X & 7) << 6) | ((coord.Y & 7) << 3) | (coord.Z & 7);
            return BitConverter.ToSingle(_buffer, node + LeafValuesOffset + offset * 4);
        }

        // Header sizes for float grids (see NanoVDB.h): node header, masks, 16 bytes of stats, 32-byte aligned.
        private const int RootHeaderPadded = 64;
        private const int UpperChildMaskOffset = 24 + 8 + 4096;
        private const int UpperTableOffset = 8256;
        private const int LowerChildMaskOffset = 24 + 8 + 512;
        private const int LowerTableOffset = 1088;
        private const int LeafValuesOffset = 96;

        private readonly byte[] _buffer;
        private readonly int _rootOffset;
        private readonly int _tileCount;
        private readonly int _firstTileOffset;
        private readonly float _background;
    }
}
