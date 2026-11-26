#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using T3.Core.Operator.Slots;

namespace T3.Core.Utils;

public static class OperatorUtils
{
    internal static long ComputeInstanceHash(IReadOnlyList<Guid> idPath) => ComputeInstanceHash(idPath, idPath.Count);

    /** Hashes only the first <paramref name="count"/> ids, which equals the hash of that ancestor's own path. */
    internal static long ComputeInstanceHash(IReadOnlyList<Guid> idPath, int count)
    {
        unchecked
        {
            var hash = 0xCBF29CE484222325; // FNV-1a 64-bit offset
            const long prime = 0x100000001B3;

            for (var index = 0; index < count; index++)
            {
                var guid = idPath[index];
                var parts = MemoryMarshal.Cast<Guid, ulong>(MemoryMarshal.CreateReadOnlySpan(in guid, 1));

                hash ^= parts[0];
                hash *= prime;
                hash ^= parts[1];
                hash *= prime;
            }

            return (long)hash;
        }
    }

    public static int GetChildSlotHash(this ISlot input)
    {
        return input.Parent.SymbolChildId.GetHashCode() + input.Id.GetHashCode();
    }
}