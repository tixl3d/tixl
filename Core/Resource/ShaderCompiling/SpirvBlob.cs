#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using T3.Core.DataTypes.Vector;
using T3.Graphics;

namespace T3.Core.Resource.ShaderCompiling;

/// <summary>
/// SPIR-V together with the bindings its shader declares, in one byte array.
/// </summary>
/// <remarks>
/// Vulkan needs the bindings before a pipeline exists, and they come from the compiler's reflection — which
/// means they have to survive the shader cache. Packing them next to the code keeps the cache a plain
/// byte-array store, as it is for DXBC, instead of growing a second file per shader that could go missing.
/// </remarks>
public static class SpirvBlob
{
    public static byte[] Pack(ReadOnlySpan<byte> spirv, IReadOnlyList<ShaderBinding> bindings, Int3 threadGroups)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(threadGroups.X);
        writer.Write(threadGroups.Y);
        writer.Write(threadGroups.Z);
        writer.Write(bindings.Count);

        foreach (var binding in bindings)
        {
            writer.Write(binding.Set);
            writer.Write(binding.Slot);
            writer.Write((int)binding.Kind);
            writer.Write(binding.Name);
        }

        writer.Write(spirv.Length);
        writer.Write(spirv);
        writer.Flush();
        return stream.ToArray();
    }

    public static bool TryUnpack(byte[] blob, out byte[] spirv, out ShaderBinding[] bindings, out Int3 threadGroups)
    {
        spirv = [];
        bindings = [];
        threadGroups = default;

        if (blob.Length < 24)
            return false;

        try
        {
            using var stream = new MemoryStream(blob, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                return false;

            threadGroups = new Int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            var count = reader.ReadInt32();
            bindings = new ShaderBinding[count];

            for (var i = 0; i < count; i++)
            {
                var set = reader.ReadInt32();
                var slot = reader.ReadInt32();
                var kind = (BindingKind)reader.ReadInt32();
                bindings[i] = new ShaderBinding(set, slot, kind, reader.ReadString());
            }

            spirv = reader.ReadBytes(reader.ReadInt32());
            return true;
        }
        catch (Exception)
        {
            // A blob from an older build or a truncated cache file: treat it as a miss and recompile.
            return false;
        }
    }

    /// <summary>
    /// How many threads a compute shader's group has. SPIR-V carries it as an execution mode, and the
    /// compiler's reflection reports it — which matters because TiXL read it out of DXBC before.
    /// </summary>
    public static bool TryGetThreadGroups(byte[] blob, out Int3 threadGroups)
        => TryUnpack(blob, out _, out _, out threadGroups);

    /// <summary>True for a blob this class wrote, so a cache holding DXBC is not mistaken for one.</summary>
    public static bool IsSpirvBlob(byte[] blob)
        => blob.Length >= 4 && BitConverter.ToUInt32(blob, 0) == Magic;

    private const uint Magic = 0x5053_5854; // "TXSP"
    private const int Version = 1;
}
