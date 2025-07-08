using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using T3.Core.Operator;

namespace T3.Core.Utils;

public static class OperatorUtils
{

    public static long ComputeInstanceHash(IReadOnlyList<Guid> idPath)
    {
        unchecked
        {
            var hash = 0xCBF29CE484222325; // FNV-1a 64-bit offset
            const long prime = 0x100000001B3;

            foreach (var guid  in idPath)
            {
                var parts = MemoryMarshal.Cast<Guid, ulong>(MemoryMarshal.CreateReadOnlySpan(in guid, 1));

                hash ^= parts[0];
                hash *= prime;
                hash ^= parts[1];
                hash *= prime;
            }
            return (long)hash;
        }
    }
}