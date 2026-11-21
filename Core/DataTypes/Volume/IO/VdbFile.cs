#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace T3.Core.DataTypes.Volume.IO;

/// <summary>
/// Reads and writes OpenVDB .vdb files (format versions 222-225, the modern node-mask layout)
/// in pure C#. The reader supports uncompressed, zlib ("zip") and blosc-compressed buffers as
/// produced by Houdini/Blender; the writer emits uncompressed grids, which every OpenVDB
/// consumer reads.
///
/// File layout (openvdb/io/Archive.cc): [int64 magic][uint32 fileVersion][uint32 libMajor]
/// [uint32 libMinor][char hasGridOffsets][36-char uuid][MetaMap][int32 gridCount]
/// then per grid: descriptor [string uniqueName][string gridType][string instanceName]
/// [int64 gridPos][int64 blockPos][int64 endPos] followed by the grid body at gridPos:
/// [uint32 compression][MetaMap][transform][tree topology][tree buffers].
/// </summary>
public static class VdbFile
{
    private const uint Magic = 0x56444220;              // "VDB " (little-endian low word)
    private const uint FileVersion = 225;
    private const uint LibMajor = 12;
    private const uint LibMinor = 0;

    public const uint CompressNone = 0;
    public const uint CompressZip = 0x1;
    public const uint CompressActiveMask = 0x2;
    public const uint CompressBlosc = 0x4;

    // Inactive-value reconstruction codes (openvdb/io/Compression.h).
    private const byte NoMaskOrInactiveVals = 0;
    private const byte NoMaskAndMinusBg = 1;
    private const byte NoMaskAndOneInactiveVal = 2;
    private const byte MaskAndNoInactiveVals = 3;
    private const byte MaskAndOneInactiveVal = 4;
    private const byte MaskAndTwoInactiveVals = 5;
    private const byte NoMaskAndAllVals = 6;

    // ---------------------------------------------------------------- writer

    public static void Save(VdbVolume volume, string path, bool zipCompression = false)
    {
        using var stream = File.Create(path);
        Save(volume, stream, zipCompression);
    }

    public static void Save(VdbVolume volume, Stream stream, bool zipCompression = false)
    {
        using var body = new MemoryStream();
        var w = new BinaryWriter(body);

        // Header
        w.Write((long)Magic);                            // int64 sign-extended magic
        w.Write(FileVersion);
        w.Write(LibMajor);
        w.Write(LibMinor);
        w.Write((byte)1);                                // hasGridOffsets
        var uuid = Encoding.ASCII.GetBytes(CreateUuid());
        w.Write(uuid);

        WriteMetaMap(w, new Dictionary<string, object>()); // file-level metadata: empty

        var grids = volume.Grids;
        w.Write(grids.Count);

        // Grid descriptors with placeholder offsets (patched after bodies are written).
        var offsetPositions = new List<long>[grids.Count];
        for (var i = 0; i < grids.Count; i++)
        {
            offsetPositions[i] = new List<long>(3);
            WriteString(w, UniqueName(grids[i].Name, i));
            WriteString(w, GridTypeName(grids[i]));
            WriteString(w, string.Empty);                // instance parent name
            for (var k = 0; k < 3; k++)
            {
                offsetPositions[i].Add(body.Position);
                w.Write(0L);
            }
        }

        // Grid bodies.
        var gridPos = body.Position;
        for (var i = 0; i < grids.Count; i++)
        {
            var gridBody = BuildGridBody(grids[i], zipCompression);
            body.Write(gridBody, 0, gridBody.Length);
            var endPos = body.Position;
            var blockPos = gridPos + GridBodyTopologyStart(gridBody);

            var offsets = offsetPositions[i];
            PatchLong(body, offsets[0], gridPos);
            PatchLong(body, offsets[1], blockPos);
            PatchLong(body, offsets[2], endPos);
            gridPos = endPos;
        }

        w.Flush();
        body.WriteTo(stream);
    }

    private static void PatchLong(MemoryStream stream, long position, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        var buffer = stream.GetBuffer();
        Array.Copy(bytes.ToArray(), 0, buffer, position, 8);
    }

    private static string UniqueName(string name, int index) => string.IsNullOrEmpty(name) ? $"grid{index}" : name;

    /// <summary>
    /// Serialized grid type strings. OpenVDB writes the tree instantiation ("Tree_float_5_4_3"),
    /// not the C++ grid class name, and appends "_HalfFloat" for half-precision payloads.
    /// </summary>
    private static string GridTypeName(IVdbGrid grid) => grid switch
    {
        VdbGrid<Vector3> => "Tree_vec3s_5_4_3",
        _ => "Tree_float_5_4_3",
    };

    private static string CreateUuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var s = Convert.ToHexString(bytes);
        return $"{s[..8]}-{s[8..12]}-{s[12..16]}-{s[16..20]}-{s[20..32]}";
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write((uint)bytes.Length);
        w.Write(bytes);
    }

    private static void WriteMetaMap(BinaryWriter w, Dictionary<string, object> meta)
    {
        w.Write((uint)meta.Count);
        foreach (var (name, value) in meta)
        {
            WriteString(w, name);
            switch (value)
            {
                case string s:
                    WriteString(w, "string");
                    WriteTypedValue(w, Encoding.UTF8.GetBytes(s));
                    break;
                case int i:
                    WriteString(w, "int32");
                    WriteTypedValue(w, BitConverter.GetBytes(i));
                    break;
                case long l:
                    WriteString(w, "int64");
                    WriteTypedValue(w, BitConverter.GetBytes(l));
                    break;
                case float f:
                    WriteString(w, "float");
                    WriteTypedValue(w, BitConverter.GetBytes(f));
                    break;
                case double d:
                    WriteString(w, "double");
                    WriteTypedValue(w, BitConverter.GetBytes(d));
                    break;
                case bool b:
                    WriteString(w, "bool");
                    WriteTypedValue(w, [(byte)(b ? 1 : 0)]);
                    break;
                case Vector3 v:
                    WriteString(w, "vec3s");
                    WriteTypedValue(w, PackVec3(v));
                    break;
                default:
                    WriteString(w, "string");
                    WriteTypedValue(w, []);
                    break;
            }
        }
    }

    private static void WriteTypedValue(BinaryWriter w, byte[] bytes)
    {
        w.Write((uint)bytes.Length);
        w.Write(bytes);
    }

    private static byte[] PackVec3(Vector3 v)
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, v.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), v.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), v.Z);
        return bytes;
    }

    private static byte[] BuildGridBody(IVdbGrid genericGrid, bool zipCompression)
    {
        var compressionFlags = zipCompression ? CompressZip : CompressNone;

        var meta = new MemoryStream();
        var metaMap = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(genericGrid.Name))
            metaMap["name"] = genericGrid.Name;
        metaMap["class"] = genericGrid.GridClass switch
        {
            VdbGridClass.LevelSet => "LEVEL_SET",
            VdbGridClass.FogVolume => "FOG_VOLUME",
            VdbGridClass.Staggered => "STAGGERED",
            _ => "UNKNOWN",
        };
        if (genericGrid is VdbGrid<Vector3>)
            metaMap["vector_type"] = "invariant";
        WriteMetaMap(new BinaryWriter(meta), metaMap);

        var transformStream = new MemoryStream();
        var tw = new BinaryWriter(transformStream);
        var (mapType, values) = genericGrid.Transform.ToOpenVdbMap();
        WriteString(tw, mapType);
        foreach (var v in values)
            tw.Write(v);

        var topologyStream = new MemoryStream();
        var bufferStream = new MemoryStream();
        if (genericGrid is VdbGrid<float> floatGrid)
        {
            WriteTopologyFloat(floatGrid, new BinaryWriter(topologyStream), compressionFlags);
            WriteBuffersFloat(floatGrid, new BinaryWriter(bufferStream), compressionFlags);
        }
        else if (genericGrid is VdbGrid<Vector3> vecGrid)
        {
            WriteTopologyVec3(vecGrid, new BinaryWriter(topologyStream), compressionFlags);
            WriteBuffersVec3(vecGrid, new BinaryWriter(bufferStream), compressionFlags);
        }
        else
        {
            throw new NotSupportedException($"Grid type {genericGrid.GetType().Name} cannot be saved");
        }

        var body = new MemoryStream();
        var bw = new BinaryWriter(body);
        bw.Write(compressionFlags);
        bw.Write(meta.ToArray());
        bw.Write(transformStream.ToArray());
        bw.Write(topologyStream.ToArray());
        bw.Write(bufferStream.ToArray());
        bw.Flush();
        return body.ToArray();
    }

    private static int CompareCoord(VdbCoord a, VdbCoord b)
        => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);

    private static IEnumerable<KeyValuePair<VdbCoord, VdbRootTile<T>>> SortedRootTiles<T>(VdbTree<T> tree)
    {
        var list = new List<KeyValuePair<VdbCoord, VdbRootTile<T>>>(tree.Root.Tiles);
        list.Sort((a, b) => CompareCoord(a.Key, b.Key));
        return list;
    }

    private static IEnumerable<KeyValuePair<VdbCoord, VdbInternalNode2<T>>> SortedRootChildren<T>(VdbTree<T> tree)
    {
        var list = new List<KeyValuePair<VdbCoord, VdbInternalNode2<T>>>(tree.Root.Children);
        list.Sort((a, b) => CompareCoord(a.Key, b.Key));
        return list;
    }

    private static void WriteTopologyFloat(VdbGrid<float> grid, BinaryWriter w, uint compressionFlags)
    {
        var tree = grid.Tree;
        w.Write(1);                                          // int32 buffer count (always 1)
        w.Write(tree.Root.Background);
        w.Write((uint)tree.Root.Tiles.Count);
        w.Write((uint)tree.Root.Children.Count);

        foreach (var (coord, tile) in SortedRootTiles(tree))
        {
            w.Write(coord.X);
            w.Write(coord.Y);
            w.Write(coord.Z);
            w.Write(tile.Value);
            w.Write(tile.Active);
        }

        foreach (var (_, node2) in SortedRootChildren(tree))
        {
            w.Write(node2.Origin.X);
            w.Write(node2.Origin.Y);
            w.Write(node2.Origin.Z);
            WriteTopology(node2, w, compressionFlags);
        }
    }

    private static void WriteTopology(VdbInternalNode2<float> node, BinaryWriter w, uint compressionFlags)
    {
        node.ChildMask.Save(w);
        node.ValueMask.Save(w);
        WriteValuesBlock(w, node.Values, compressionFlags);
        // Child origins are implicit: InternalNode::writeTopology stores no coords,
        // the child's position follows from its childMask bit index.
        foreach (var slot in node.ChildMask.OnBits())
            WriteTopology(node.Children[slot]!, w, compressionFlags);
    }

    private static void WriteTopology(VdbInternalNode1<float> node, BinaryWriter w, uint compressionFlags)
    {
        node.ChildMask.Save(w);
        node.ValueMask.Save(w);
        WriteValuesBlock(w, node.Values, compressionFlags);
        foreach (var slot in node.ChildMask.OnBits())
        {
            var leaf = node.Children[slot]!;
            leaf.ValueMask.Save(w);                              // leaf topology = value mask
        }
    }

    private static void WriteTopologyVec3(VdbGrid<Vector3> grid, BinaryWriter w, uint compressionFlags)
    {
        var tree = grid.Tree;
        w.Write(1);                                          // int32 buffer count (always 1)
        WriteValueVec3(w, tree.Root.Background);
        w.Write((uint)tree.Root.Tiles.Count);
        w.Write((uint)tree.Root.Children.Count);

        foreach (var (coord, tile) in SortedRootTiles(tree))
        {
            w.Write(coord.X);
            w.Write(coord.Y);
            w.Write(coord.Z);
            WriteValueVec3(w, tile.Value);
            w.Write(tile.Active);
        }

        foreach (var (_, node2) in SortedRootChildren(tree))
        {
            w.Write(node2.Origin.X);
            w.Write(node2.Origin.Y);
            w.Write(node2.Origin.Z);
            WriteTopologyVec(node2, w, compressionFlags);
        }
    }

    private static void WriteTopologyVec(VdbInternalNode2<Vector3> node, BinaryWriter w, uint compressionFlags)
    {
        node.ChildMask.Save(w);
        node.ValueMask.Save(w);
        WriteValuesBlock(w, Flatten(node.Values), compressionFlags);
        foreach (var slot in node.ChildMask.OnBits())
            WriteTopologyVec(node.Children[slot]!, w, compressionFlags);
    }

    private static void WriteTopologyVec(VdbInternalNode1<Vector3> node, BinaryWriter w, uint compressionFlags)
    {
        node.ChildMask.Save(w);
        node.ValueMask.Save(w);
        WriteValuesBlock(w, Flatten(node.Values), compressionFlags);
        foreach (var slot in node.ChildMask.OnBits())
        {
            var leaf = node.Children[slot]!;
            leaf.ValueMask.Save(w);                          // leaf topology = value mask
        }
    }

    private static float[] Flatten(Vector3[] values)
    {
        var flat = new float[values.Length * 3];
        for (var i = 0; i < values.Length; i++)
        {
            flat[i * 3] = values[i].X;
            flat[i * 3 + 1] = values[i].Y;
            flat[i * 3 + 2] = values[i].Z;
        }

        return flat;
    }

    private static void WriteBuffersFloat(VdbGrid<float> grid, BinaryWriter w, uint compressionFlags)
    {
        // Leaf buffers must follow the topology traversal exactly (sorted root children, then
        // ascending child slots); the reader consumes them in that order.
        foreach (var (_, node2) in SortedRootChildren(grid.Tree))
        {
            foreach (var node1 in node2.Children)
            {
                if (node1 == null)
                    continue;

                foreach (var slot in node1.ChildMask.OnBits())
                {
                    var leaf = node1.Children[slot]!;
                    // LeafNode::writeBuffers re-stores the value mask, then the compressed values.
                    // We always write code 6 (all values), optionally zip-compressing the data block.
                    leaf.ValueMask.Save(w);
                    w.Write((byte)NoMaskAndAllVals);
                    WriteDataBlock(w, leaf.Values, compressionFlags);
                }
            }
        }
    }

    private static void WriteValueVec3(BinaryWriter w, Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static void WriteBuffersVec3(VdbGrid<Vector3> grid, BinaryWriter w, uint compressionFlags)
    {
        foreach (var (_, node2) in SortedRootChildren(grid.Tree))
        {
            foreach (var node1 in node2.Children)
            {
                if (node1 == null)
                    continue;

                foreach (var slot in node1.ChildMask.OnBits())
                {
                    var leaf = node1.Children[slot]!;
                    leaf.ValueMask.Save(w);
                    w.Write((byte)NoMaskAndAllVals);
                    WriteDataBlock(w, Flatten(leaf.Values), compressionFlags);
                }
            }
        }
    }

    private static void WriteValuesBlock(BinaryWriter w, float[] values, uint compressionFlags)
    {
        w.Write((byte)NoMaskAndAllVals);
        WriteDataBlock(w, values, compressionFlags);
    }

    /// <summary>io::writeData: blosc > zip > raw, with the signed int64 byte-count prefix for zip.</summary>
    private static void WriteDataBlock(BinaryWriter w, float[] values, uint compressionFlags)
    {
        if ((compressionFlags & CompressZip) != 0)
        {
            var byteCount = values.Length * sizeof(float);
            var bytes = new byte[byteCount];
            for (var i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal))
            {
                zlib.Write(bytes, 0, bytes.Length);
            }

            var payload = compressed.ToArray();
            if (payload.Length < byteCount)
            {
                w.Write((long)payload.Length);
                w.Write(payload);
                return;
            }

            w.Write((long)-byteCount);
            w.Write(bytes);
            return;
        }

        foreach (var v in values)
            w.Write(v);
    }

    /// <summary>Byte offset from a grid body start to the topology section.</summary>
    private static int GridBodyTopologyStart(byte[] gridBody)
    {
        var pos = 4; // compression flags
        var metaCount = BinaryPrimitives.ReadUInt32LittleEndian(gridBody.AsSpan(pos));
        pos += 4;
        for (var i = 0; i < metaCount; i++)
        {
            pos += 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(gridBody.AsSpan(pos)); // name string
            pos += 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(gridBody.AsSpan(pos)); // type string
            pos += 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(gridBody.AsSpan(pos)); // value bytes
        }

        pos += 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(gridBody.AsSpan(pos));     // map type string
        pos += 18 * 8;                                                                     // ScaleTranslateMap payload
        return pos;
    }

    // ---------------------------------------------------------------- reader

    public static VdbVolume Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static VdbVolume Load(Stream stream)
    {
        var reader = new VdbReader(stream);
        return reader.ReadAll();
    }

    /// <summary>Quick scan: grid names, types and file offsets without deserializing trees.</summary>
    public static List<(string Name, string Type, long Offset, long End)> ScanGrids(string path)
    {
        using var stream = File.OpenRead(path);
        var reader = new VdbReader(stream);
        return reader.ScanGrids();
    }

    private sealed class VdbReader
    {
        private readonly byte[] _buffer;
        private readonly BinaryReader _r;
        private int _fileVersion;

        public VdbReader(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            _buffer = ms.ToArray();
            _r = new BinaryReader(new MemoryStream(_buffer));
        }

        public VdbVolume ReadAll()
        {
            ReadHeader();
            ReadMetaMap();
            var gridCount = ReadInt32();

            var volume = new VdbVolume();
            var gridsByName = new Dictionary<string, IVdbGrid>(StringComparer.Ordinal);
            var instances = new List<(string Name, string ParentName)>();
            for (var i = 0; i < gridCount; i++)
            {
                var (name, gridType, instanceParent, gridPos, endPos) = ReadGridDescriptor();

                if (gridPos < 0)
                {
                    // Instanced grid: its body was already written for the instance parent.
                    instances.Add((StripSuffix(name), StripSuffix(instanceParent)));
                    _r.BaseStream.Seek(endPos, SeekOrigin.Begin);
                    continue;
                }

                if (!IsSupportedType(gridType))
                {
                    volume.SkippedGrids.Add($"{StripSuffix(name)} ({gridType})");
                    _r.BaseStream.Seek(endPos, SeekOrigin.Begin);
                    continue;
                }

                _r.BaseStream.Seek(gridPos, SeekOrigin.Begin);
                IVdbGrid? grid;
                try
                {
                    grid = ReadGridBody(StripSuffix(name), gridType);
                }
                catch (NotSupportedException)
                {
                    // Unsupported transform or feature: skip just this grid, keep the rest.
                    volume.SkippedGrids.Add($"{StripSuffix(name)} ({gridType})");
                    grid = null;
                }

                _r.BaseStream.Seek(endPos, SeekOrigin.Begin);
                if (grid != null)
                {
                    volume.Grids.Add(grid);
                    gridsByName[StripSuffix(name)] = grid;
                }
            }

            foreach (var (name, parentName) in instances)
            {
                if (gridsByName.TryGetValue(parentName, out var parent))
                    volume.Grids.Add(parent);
            }

            return volume;
        }

        private (string Name, string GridType, string InstanceParent, long GridPos, long EndPos) ReadGridDescriptor()
        {
            var uniqueName = ReadString();
            var gridType = ReadString();
            var instanceParent = ReadString();
            var gridPos = ReadInt64();
            _ = ReadInt64();                             // blockPos
            var endPos = ReadInt64();
            return (uniqueName, gridType, instanceParent, gridPos, endPos);
        }

        public List<(string Name, string Type, long Offset, long End)> ScanGrids()
        {
            ReadHeader();
            ReadMetaMap();
            var gridCount = ReadInt32();
            var result = new List<(string, string, long, long)>();
            for (var i = 0; i < gridCount; i++)
            {
                var (name, gridType, _, _, endPos) = ReadGridDescriptor();
                result.Add((StripSuffix(name), gridType, endPos, endPos));
                // Grid bodies are interleaved with descriptors: seek to the end of
                // this grid's body, where the next descriptor starts.
                _r.BaseStream.Seek(endPos, SeekOrigin.Begin);
            }

            return result;
        }

        private void ReadHeader()
        {
            var magic = ReadInt64();
            if ((uint)(magic & 0xFFFFFFFF) != Magic)
                throw new InvalidDataException("Not a VDB file (bad magic)");

            _fileVersion = ReadInt32();
            if (_fileVersion >= 300)
                throw new InvalidDataException($"VDB file version {_fileVersion} is not supported (max 299)");
            if (_fileVersion < 222)
                throw new InvalidDataException($"VDB file version {_fileVersion} is too old (min 222 required)");

            _ = ReadInt32();                                 // library major
            _ = ReadInt32();                                 // library minor
            _ = _r.ReadByte();                               // hasGridOffsets
            if (_fileVersion < 222)
                _ = _r.ReadByte();                           // legacy compression flag byte

            _ = _r.ReadBytes(36);                            // uuid
        }

        /// <summary>Skips the metadata map and returns its string entries, which carry the grid class.</summary>
        private Dictionary<string, string> ReadMetaMap()
        {
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            var count = ReadUInt32();
            for (var i = 0; i < count; i++)
            {
                var name = ReadString();
                var typeName = ReadString();
                var size = ReadUInt32();
                if (typeName == "string")
                    strings[name] = Encoding.UTF8.GetString(ReadBytesChecked(size));
                else
                    _r.BaseStream.Seek(size, SeekOrigin.Current);
            }

            return strings;
        }

        private static VdbGridClass ParseGridClass(Dictionary<string, string> meta)
        {
            if (!meta.TryGetValue("class", out var value))
                return VdbGridClass.Unknown;

            return value switch
            {
                "level set" => VdbGridClass.LevelSet,
                "fog volume" => VdbGridClass.FogVolume,
                "staggered" => VdbGridClass.Staggered,
                _ => VdbGridClass.Unknown,
            };
        }

        /// <summary>Grid types are serialized as "Tree_&lt;valueType&gt;_&lt;dims&gt;", e.g. "Tree_float_5_4_3_HalfFloat".</summary>
        private static bool IsSupportedType(string gridType)
        {
            if (gridType.EndsWith("_HalfFloat", StringComparison.Ordinal))
                gridType = gridType[..^10];

            if (!gridType.StartsWith("Tree_", StringComparison.Ordinal))
                return false;

            var valueType = gridType[5..];
            if (valueType.StartsWith("ptdata", StringComparison.Ordinal))
                return false;                            // point-data grids are not supported yet

            return valueType.StartsWith("float", StringComparison.Ordinal)
                   || valueType.StartsWith("vec3s", StringComparison.Ordinal)
                   || valueType.StartsWith("vec3f", StringComparison.Ordinal);
        }

        private IVdbGrid? ReadGridBody(string name, string gridType)
        {
            var isHalf = gridType.EndsWith("_HalfFloat", StringComparison.Ordinal);
            if (isHalf)
                gridType = gridType[..^10];
            var isVec3 = gridType.Contains("vec3", StringComparison.OrdinalIgnoreCase);

            var compression = ReadUInt32();
            var gridClass = ParseGridClass(ReadMetaMap());
            var transform = ReadTransform();

            IVdbGrid grid = isVec3
                ? ReadTreeVec3(name, transform, compression, isHalf)
                : ReadTreeFloat(name, transform, compression, isHalf);
            grid.GridClass = gridClass;
            return grid;
        }

        private VdbTransform ReadTransform()
        {
            var mapType = ReadString();
            var count = MapDoubleCount(mapType);
            if (count < 0)
                throw new NotSupportedException($"Transform map '{mapType}' is not supported");

            var doubles = new double[count];
            for (var i = 0; i < doubles.Length; i++)
                doubles[i] = ReadDouble();
            return VdbTransform.FromOpenVdbMap(mapType, doubles);
        }

        /// <summary>
        /// Number of doubles each map type stores (openvdb/math/Maps.h write()).
        /// Each Vec3d is 3 doubles; AffineMap/UnitaryMap store a 4x4 matrix.
        /// </summary>
        private static int MapDoubleCount(string mapType) => mapType switch
        {
            "UniformScaleTranslateMap" => 18,   // translation + 5 x Vec3d
            "ScaleTranslateMap" => 18,          // translation + 5 x Vec3d
            "UniformScaleMap" => 15,            // 5 x Vec3d
            "ScaleMap" => 15,                   // 5 x Vec3d
            "TranslationMap" => 3,              // 1 x Vec3d
            "AffineMap" => 16,                  // Mat4d
            "UnitaryMap" => 16,                 // Mat4d
            _ => -1,                            // NonlinearFrustumMap (variable) and unknown types
        };

        private VdbGrid<float> ReadTreeFloat(string name, VdbTransform transform, uint compression, bool isHalf)
        {
            // Tree section: int32 buffer count (always 1 for modern files), then the root.
            var bufferCount = ReadInt32();
            if (bufferCount != 1)
                throw new InvalidDataException($"Multi-buffer trees are not supported (bufferCount={bufferCount})");

            // Root background and tile values are stored as full 4-byte floats even in
            // half-precision grids (openvdb/tree/RootNode.h writeTopology).
            var background = ReadFloat();
            var grid = new VdbGrid<float>(background, transform.VoxelSize, name, VdbGridClass.Unknown)
            {
                Transform = transform,
            };

            var tileCount = ReadUInt32();
            var childCount = ReadUInt32();

            for (var i = 0; i < tileCount; i++)
            {
                var coord = ReadCoord();
                var value = ReadFloat();
                var active = ReadBool();
                grid.Tree.Root.Tiles[coord] = new VdbRootTile<float> { Value = value, Active = active };
            }

            var rootChildrenInReadOrder = new List<VdbInternalNode2<float>>();
            for (var i = 0; i < childCount; i++)
            {
                var origin = ReadCoord();
                var node2 = new VdbInternalNode2<float>(origin);
                ReadInternalTopology(node2, background, isHalf, compression, isVec3: false);
                grid.Tree.Root.Children[origin] = node2;
                rootChildrenInReadOrder.Add(node2);
            }

            // Buffers follow the order in which the topology was written, not dictionary order.
            foreach (var node2 in rootChildrenInReadOrder)
                ReadInternalBuffers(node2, compression, isHalf, background);

            return grid;
        }

        private VdbGrid<Vector3> ReadTreeVec3(string name, VdbTransform transform, uint compression, bool isHalf)
        {
            var bufferCount = ReadInt32();
            if (bufferCount != 1)
                throw new InvalidDataException($"Multi-buffer trees are not supported (bufferCount={bufferCount})");

            var background = new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
            var grid = new VdbGrid<Vector3>(background, transform.VoxelSize, name, VdbGridClass.Unknown)
            {
                Transform = transform,
            };

            var tileCount = ReadUInt32();
            var childCount = ReadUInt32();

            for (var i = 0; i < tileCount; i++)
            {
                var coord = ReadCoord();
                var value = new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
                var active = ReadBool();
                grid.Tree.Root.Tiles[coord] = new VdbRootTile<Vector3> { Value = value, Active = active };
            }

            var rootChildrenInReadOrder = new List<VdbInternalNode2<Vector3>>();
            for (var i = 0; i < childCount; i++)
            {
                var origin = ReadCoord();
                var node2 = new VdbInternalNode2<Vector3>(origin);
                ReadInternalTopology(node2, background, isHalf, compression, isVec3: true);
                grid.Tree.Root.Children[origin] = node2;
                rootChildrenInReadOrder.Add(node2);
            }

            // Buffers follow the order in which the topology was written, not dictionary order.
            foreach (var node2 in rootChildrenInReadOrder)
                ReadInternalBuffers(node2, compression, isHalf, background);

            return grid;
        }

        private void ReadInternalTopology(VdbInternalNode2<float> node, float background, bool isHalf, uint compression, bool isVec3)
        {
            var childMask = NodeMask.Load(_r.BaseStream, 512);
            var valueMask = NodeMask.Load(_r.BaseStream, 512);
            node.ChildMask.FillFrom(childMask);
            node.ValueMask.FillFrom(valueMask);
            ReadTileValues(node.Values, valueMask, childMask, background, isHalf, compression);

            foreach (var slot in childMask.OnBits())
            {
                // InternalNode stores no child coords; the origin is implied by the slot index.
                var origin = node.ChildOrigin(slot);
                var node1 = new VdbInternalNode1<float>(origin);
                ReadInternalTopology(node1, background, isHalf, compression, isVec3);
                node.Children[slot] = node1;
            }
        }

        private void ReadInternalTopology(VdbInternalNode1<float> node, float background, bool isHalf, uint compression, bool isVec3)
        {
            var childMask = NodeMask.Load(_r.BaseStream, 64);
            var valueMask = NodeMask.Load(_r.BaseStream, 64);
            node.ChildMask.FillFrom(childMask);
            node.ValueMask.FillFrom(valueMask);
            ReadTileValues(node.Values, valueMask, childMask, background, isHalf, compression);

            foreach (var slot in childMask.OnBits())
            {
                var origin = node.LeafOrigin(slot);
                var leaf = new VdbLeafNode<float>(origin)
                {
                    Origin = origin,
                };
                leaf.ValueMask.FillFrom(NodeMask.Load(_r.BaseStream, 8));
                node.Children[slot] = leaf;
            }
        }

        private void ReadInternalTopology(VdbInternalNode2<Vector3> node, Vector3 background, bool isHalf, uint compression, bool isVec3)
        {
            var childMask = NodeMask.Load(_r.BaseStream, 512);
            var valueMask = NodeMask.Load(_r.BaseStream, 512);
            node.ChildMask.FillFrom(childMask);
            node.ValueMask.FillFrom(valueMask);
            ReadTileValuesVec3(node.Values, valueMask, childMask, background, isHalf, compression);

            foreach (var slot in childMask.OnBits())
            {
                var origin = node.ChildOrigin(slot);
                var node1 = new VdbInternalNode1<Vector3>(origin);
                ReadInternalTopology(node1, background, isHalf, compression, isVec3);
                node.Children[slot] = node1;
            }
        }

        private void ReadInternalTopology(VdbInternalNode1<Vector3> node, Vector3 background, bool isHalf, uint compression, bool isVec3)
        {
            var childMask = NodeMask.Load(_r.BaseStream, 64);
            var valueMask = NodeMask.Load(_r.BaseStream, 64);
            node.ChildMask.FillFrom(childMask);
            node.ValueMask.FillFrom(valueMask);
            ReadTileValuesVec3(node.Values, valueMask, childMask, background, isHalf, compression);

            foreach (var slot in childMask.OnBits())
            {
                var origin = node.LeafOrigin(slot);
                var leaf = new VdbLeafNode<Vector3>(origin)
                {
                    Origin = origin,
                };
                leaf.ValueMask.FillFrom(NodeMask.Load(_r.BaseStream, 8));
                node.Children[slot] = leaf;
            }
        }

        /// <summary>
        /// Internal-node tile tables: strict write order is metadata byte, [inactive values],
        /// [selection mask], [data]. Active-tile data reads in ascending slot order; child
        /// slots' stored placeholders are discarded (children fill them via recursion).
        /// </summary>
        private void ReadTileValues(float[] values, NodeMask valueMask, NodeMask childMask, float background, bool isHalf, uint compression)
        {
            var metadata = _r.ReadByte();
            float inactive0 = 0f, inactive1 = 0f;
            NodeMask? selection = null;

            if (metadata is NoMaskAndOneInactiveVal or MaskAndOneInactiveVal or MaskAndTwoInactiveVals)
            {
                inactive0 = ReadFloat();
                if (metadata == MaskAndTwoInactiveVals)
                    inactive1 = ReadFloat();
            }

            if (metadata is MaskAndNoInactiveVals or MaskAndOneInactiveVal or MaskAndTwoInactiveVals)
                selection = NodeMask.Load(_r.BaseStream, values.Length >> 6);

            switch (metadata)
            {
                case NoMaskAndAllVals:
                {
                    ReadDataBlock(values, compression, isHalf);
                    return;
                }
                case NoMaskOrInactiveVals:
                case NoMaskAndMinusBg:
                case NoMaskAndOneInactiveVal:
                {
                    var stored = valueMask.CountOn();
                    var data = new float[stored];
                    ReadDataBlock(data, compression, isHalf);
                    var idx = 0;
                    foreach (var slot in valueMask.OnBits())
                        values[slot] = data[idx++];

                    var inactive = metadata switch
                    {
                        NoMaskAndMinusBg => -background,
                        NoMaskAndOneInactiveVal => inactive0,
                        _ => background,
                    };
                    foreach (var slot in valueMask.OffBits(values.Length))
                    {
                        if (!childMask.IsOn(slot))
                            values[slot] = inactive;
                    }

                    return;
                }
                case MaskAndNoInactiveVals:
                case MaskAndOneInactiveVal:
                case MaskAndTwoInactiveVals:
                {
                    var stored = valueMask.CountOn();
                    var data = new float[stored];
                    ReadDataBlock(data, compression, isHalf);
                    var idx = 0;
                    foreach (var slot in valueMask.OnBits())
                        values[slot] = data[idx++];

                    var val0 = metadata switch
                    {
                        MaskAndNoInactiveVals => -background,
                        MaskAndOneInactiveVal => inactive0,
                        MaskAndTwoInactiveVals => inactive0,
                        _ => background,
                    };
                    var val1 = metadata switch
                    {
                        MaskAndNoInactiveVals => background,
                        MaskAndOneInactiveVal => background,
                        MaskAndTwoInactiveVals => inactive1,
                        _ => background,
                    };
                    foreach (var slot in valueMask.OffBits(values.Length))
                    {
                        if (childMask.IsOn(slot))
                            continue;
                        values[slot] = selection!.IsOn(slot) ? val1 : val0;
                    }

                    return;
                }
                default:
                    throw new InvalidDataException($"Unknown value metadata code {metadata}");
            }
        }

        private void ReadTileValuesVec3(Vector3[] values, NodeMask valueMask, NodeMask childMask, Vector3 background, bool isHalf, uint compression)
        {
            // Reuse the scalar reader on a flat buffer, then scatter. Background codes for vec3
            // use zeroVal (= 0,0,0) in OpenVDB, so component-wise +/- background degenerates to
            // sign-flipping each component.
            var flat = new float[values.Length * 3];
            var metadata = _r.ReadByte();
            float inactive0 = 0f, inactive1 = 0f;
            NodeMask? selection = null;

            if (metadata is NoMaskAndOneInactiveVal or MaskAndOneInactiveVal or MaskAndTwoInactiveVals)
            {
                inactive0 = ReadFloat();
                if (metadata == MaskAndTwoInactiveVals)
                    inactive1 = ReadFloat();
            }

            if (metadata is MaskAndNoInactiveVals or MaskAndOneInactiveVal or MaskAndTwoInactiveVals)
                selection = NodeMask.Load(_r.BaseStream, values.Length >> 6);

            switch (metadata)
            {
                case NoMaskAndAllVals:
                    ReadDataBlock(flat, compression, isHalf);
                    Scatter(flat, values);
                    return;
                case NoMaskOrInactiveVals:
                case NoMaskAndMinusBg:
                case NoMaskAndOneInactiveVal:
                {
                    var stored = valueMask.CountOn();
                    var data = new float[stored * 3];
                    ReadDataBlock(data, compression, isHalf);
                    var idx = 0;
                    foreach (var slot in valueMask.OnBits())
                    {
                        values[slot] = new Vector3(data[idx], data[idx + 1], data[idx + 2]);
                        idx += 3;
                    }

                    var inactive = metadata switch
                    {
                        NoMaskAndMinusBg => -background,
                        NoMaskAndOneInactiveVal => new Vector3(inactive0),
                        _ => background,
                    };
                    foreach (var slot in valueMask.OffBits(values.Length))
                    {
                        if (!childMask.IsOn(slot))
                            values[slot] = inactive;
                    }

                    return;
                }
                default:
                    throw new NotSupportedException("Mask-compressed vec3 tile tables are not supported");
            }
        }

        private void ReadInternalBuffers(VdbInternalNode2<float> node, uint compression, bool isHalf, float background)
        {
            foreach (var node1 in node.Children)
            {
                if (node1 == null)
                    continue;
                foreach (var slot in node1.ChildMask.OnBits())
                    ReadLeafBuffer(node1.Children[slot]!, compression, isHalf, background);
            }
        }

        private void ReadInternalBuffers(VdbInternalNode2<Vector3> node, uint compression, bool isHalf, Vector3 background)
        {
            foreach (var node1 in node.Children)
            {
                if (node1 == null)
                    continue;
                foreach (var slot in node1.ChildMask.OnBits())
                    ReadLeafBufferVec3(node1.Children[slot]!, compression, isHalf, background);
            }
        }

        private void ReadLeafBuffer(VdbLeafNode<float> leaf, uint compression, bool isHalf, float background)
        {
            // Leaf buffers re-store the value mask (LeafNode::writeBuffers) before the values.
            leaf.ValueMask.FillFrom(NodeMask.Load(_r.BaseStream, 8));

            var metadata = _r.ReadByte();
            float inactive0 = 0f, inactive1 = 0f;
            NodeMask? selection = null;

            if (metadata is NoMaskAndOneInactiveVal or MaskAndOneInactiveVal or MaskAndTwoInactiveVals)
            {
                inactive0 = ReadFloat();
                if (metadata == MaskAndTwoInactiveVals)
                    inactive1 = ReadFloat();
            }

            if (metadata is MaskAndNoInactiveVals or MaskAndOneInactiveVal or MaskAndTwoInactiveVals)
                selection = NodeMask.Load(_r.BaseStream, 8);

            var values = leaf.Values;
            switch (metadata)
            {
                case NoMaskAndAllVals:
                    ReadDataBlock(values, compression, isHalf);
                    return;
                case NoMaskOrInactiveVals:
                case NoMaskAndMinusBg:
                case NoMaskAndOneInactiveVal:
                {
                    var stored = leaf.ValueMask.CountOn();
                    var data = new float[stored];
                    ReadDataBlock(data, compression, isHalf);
                    var idx = 0;
                    foreach (var i in leaf.ValueMask.OnBits())
                        values[i] = data[idx++];

                    var inactive = metadata switch
                    {
                        NoMaskAndMinusBg => -background,
                        NoMaskAndOneInactiveVal => inactive0,
                        _ => background,
                    };
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (!leaf.ValueMask.IsOn(i))
                            values[i] = inactive;
                    }

                    return;
                }
                case MaskAndNoInactiveVals:
                case MaskAndOneInactiveVal:
                case MaskAndTwoInactiveVals:
                {
                    var stored = leaf.ValueMask.CountOn();
                    var data = new float[stored];
                    ReadDataBlock(data, compression, isHalf);
                    var idx = 0;
                    foreach (var i in leaf.ValueMask.OnBits())
                        values[i] = data[idx++];

                    var val0 = metadata switch
                    {
                        MaskAndNoInactiveVals => -background,
                        MaskAndOneInactiveVal => inactive0,
                        _ => inactive0,
                    };
                    var val1 = metadata switch
                    {
                        MaskAndNoInactiveVals => background,
                        MaskAndOneInactiveVal => background,
                        _ => inactive1,
                    };
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (leaf.ValueMask.IsOn(i))
                            continue;
                        values[i] = selection!.IsOn(i) ? val1 : val0;
                    }

                    return;
                }
                default:
                    throw new InvalidDataException($"Unknown leaf value metadata code {metadata}");
            }
        }

        private void ReadLeafBufferVec3(VdbLeafNode<Vector3> leaf, uint compression, bool isHalf, Vector3 background)
        {
            // Leaf buffers re-store the value mask (LeafNode::writeBuffers) before the values.
            leaf.ValueMask.FillFrom(NodeMask.Load(_r.BaseStream, 8));

            var metadata = _r.ReadByte();
            float inactive0 = 0f, inactive1 = 0f;
            NodeMask? selection = null;

            if (metadata is NoMaskAndOneInactiveVal or MaskAndOneInactiveVal or MaskAndTwoInactiveVals)
            {
                inactive0 = ReadFloat();
                if (metadata == MaskAndTwoInactiveVals)
                    inactive1 = ReadFloat();
            }

            if (metadata is MaskAndNoInactiveVals or MaskAndOneInactiveVal or MaskAndTwoInactiveVals)
                selection = NodeMask.Load(_r.BaseStream, 8);

            var values = leaf.Values;
            switch (metadata)
            {
                case NoMaskAndAllVals:
                {
                    var flat = new float[values.Length * 3];
                    ReadDataBlock(flat, compression, isHalf);
                    Scatter(flat, values);
                    return;
                }
                case NoMaskOrInactiveVals:
                case NoMaskAndMinusBg:
                case NoMaskAndOneInactiveVal:
                {
                    var stored = leaf.ValueMask.CountOn();
                    var data = new float[stored * 3];
                    ReadDataBlock(data, compression, isHalf);
                    var idx = 0;
                    foreach (var i in leaf.ValueMask.OnBits())
                    {
                        values[i] = new Vector3(data[idx], data[idx + 1], data[idx + 2]);
                        idx += 3;
                    }

                    var inactive = metadata switch
                    {
                        NoMaskAndMinusBg => -background,
                        NoMaskAndOneInactiveVal => new Vector3(inactive0),
                        _ => background,
                    };
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (!leaf.ValueMask.IsOn(i))
                            values[i] = inactive;
                    }

                    return;
                }
                default:
                    throw new NotSupportedException("Mask-compressed vec3 leaf buffers are not supported");
            }
        }

        private static void Scatter(float[] flat, Vector3[] target)
        {
            for (var i = 0; i < target.Length && i * 3 + 2 < flat.Length; i++)
                target[i] = new Vector3(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]);
        }

        /// <summary>
        /// io::readData: decompresses a value block. In half-precision grids the stored
        /// payload is an array of 2-byte half floats (typesize 2 for blosc shuffling).
        /// </summary>
        private void ReadDataBlock(float[] data, uint compression, bool isHalf)
        {
            if (data.Length == 0)
            {
                // Half-precision blocks write nothing when empty; full-precision ones still emit a size and an empty frame.
                if (!isHalf && (compression & (CompressBlosc | CompressZip)) != 0)
                {
                    var emptySize = ReadInt64();
                    if (emptySize > 0)
                        _r.BaseStream.Seek(emptySize, SeekOrigin.Current);
                }

                return;
            }

            var valueSize = isHalf ? sizeof(ushort) : sizeof(float);
            if ((compression & CompressBlosc) != 0)
            {
                var size = ReadInt64();
                if (size <= 0)
                {
                    ReadRawValues(data, size == 0 ? data.Length : (int)(-size) / valueSize, isHalf);
                    return;
                }

                var payload = ReadBytesChecked(size);
                // The block's own size is the real bound; the frame header is untrusted.
                var decompressed = Blosc.Decompress(payload, data.Length * valueSize);
                UnpackValues(decompressed, data, isHalf);
            }
            else if ((compression & CompressZip) != 0)
            {
                var size = ReadInt64();
                if (size <= 0)
                {
                    ReadRawValues(data, size == 0 ? data.Length : (int)(-size) / valueSize, isHalf);
                    return;
                }

                var payload = ReadBytesChecked(size);
                var bytes = DecompressZlib(payload, data.Length * valueSize);
                UnpackValues(bytes, data, isHalf);
            }
            else
            {
                ReadRawValues(data, data.Length, isHalf);
            }
        }

        private void ReadRawValues(float[] data, int count, bool isHalf)
        {
            if (isHalf)
            {
                for (var i = 0; i < data.Length && i < count; i++)
                    data[i] = HalfToFloat(ReadUInt16());
            }
            else
            {
                ReadFloats(data, count);
            }
        }

        private static void UnpackValues(byte[] bytes, float[] data, bool isHalf)
        {
            if (isHalf)
            {
                var count = Math.Min(data.Length, bytes.Length / sizeof(ushort));
                for (var i = 0; i < count; i++)
                    data[i] = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2)));
            }
            else
            {
                var count = Math.Min(data.Length, bytes.Length / sizeof(float));
                for (var i = 0; i < count; i++)
                    data[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
            }
        }

        private static byte[] DecompressZlib(byte[] payload, int expectedBytes)
        {
            using var source = new MemoryStream(payload);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            var bytes = new byte[expectedBytes];
            var read = 0;
            while (read < expectedBytes)
            {
                var n = zlib.Read(bytes, read, expectedBytes - read);
                if (n <= 0)
                    break;
                read += n;
            }

            return bytes;
        }

        private void ReadFloats(float[] data, int count)
        {
            var bytes = ReadBytesChecked((long)count * sizeof(float));
            for (var i = 0; i < count && i * 4 + 4 <= bytes.Length; i++)
                data[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
        }

        private VdbCoord ReadCoord() => new(ReadInt32(), ReadInt32(), ReadInt32());

        private float ReadSingle(bool isHalf) => isHalf ? HalfToFloat(ReadUInt16()) : ReadFloat();

        private Vector3 ReadVec3(bool isHalf) => new(ReadSingle(isHalf), ReadSingle(isHalf), ReadSingle(isHalf));

        /// <summary>ReadBytes allocates the requested size up front, so a size read from the file is checked against what is left.</summary>
        private byte[] ReadBytesChecked(long size)
        {
            var remaining = _r.BaseStream.Length - _r.BaseStream.Position;
            if (size < 0 || size > remaining || size > int.MaxValue)
                throw new InvalidDataException($"VDB block of {size} bytes exceeds the {remaining} bytes left in the file");

            return _r.ReadBytes((int)size);
        }

        private string ReadString()
        {
            var size = ReadUInt32();
            var bytes = ReadBytesChecked(size);
            return Encoding.UTF8.GetString(bytes);
        }

        private int ReadInt32()
        {
            Span<byte> b = stackalloc byte[4];
            _r.BaseStream.ReadExactly(b);
            return BinaryPrimitives.ReadInt32LittleEndian(b);
        }

        private long ReadInt64()
        {
            Span<byte> b = stackalloc byte[8];
            _r.BaseStream.ReadExactly(b);
            return BinaryPrimitives.ReadInt64LittleEndian(b);
        }

        private uint ReadUInt32()
        {
            Span<byte> b = stackalloc byte[4];
            _r.BaseStream.ReadExactly(b);
            return BinaryPrimitives.ReadUInt32LittleEndian(b);
        }

        private ushort ReadUInt16()
        {
            Span<byte> b = stackalloc byte[2];
            _r.BaseStream.ReadExactly(b);
            return BinaryPrimitives.ReadUInt16LittleEndian(b);
        }

        private float ReadFloat()
        {
            Span<byte> b = stackalloc byte[4];
            _r.BaseStream.ReadExactly(b);
            return BinaryPrimitives.ReadSingleLittleEndian(b);
        }

        private double ReadDouble()
        {
            Span<byte> b = stackalloc byte[8];
            _r.BaseStream.ReadExactly(b);
            return BinaryPrimitives.ReadDoubleLittleEndian(b);
        }

        private bool ReadBool() => _r.ReadByte() != 0;

        private static string StripSuffix(string uniqueName)
        {
            var sep = uniqueName.IndexOf('\x1e');
            return sep >= 0 ? uniqueName[..sep] : uniqueName;
        }

        private static float HalfToFloat(ushort half)
        {
            var sign = (uint)(half & 0x8000) << 16;
            var exponent = (half & 0x7C00) >> 10;
            var mantissa = half & 0x03FF;

            uint bits;
            if (exponent == 0)
            {
                if (mantissa == 0)
                {
                    bits = sign;
                }
                else
                {
                    var e = -1;
                    var m = mantissa;
                    do
                    {
                        e++;
                        m <<= 1;
                    } while ((m & 0x0400) == 0);

                    bits = sign | (uint)((127 - 15 - e) << 23) | (uint)((m & 0x03FF) << 13);
                }
            }
            else if (exponent == 31)
            {
                bits = sign | 0x7F800000u | (uint)(mantissa << 13);
            }
            else
            {
                bits = sign | (uint)((exponent - 15 + 127) << 23) | (uint)(mantissa << 13);
            }

            return BitConverter.UInt32BitsToSingle(bits);
        }
    }
}
