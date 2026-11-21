#nullable enable
using System.Collections.Concurrent;

namespace Lib.io.video.depthanything.Utils;

/// <summary>
/// Hands out reusable arrays so a steady stream of depth frames doesn't feed the garbage collector. Arrays up to
/// the largest size class may be longer than requested; callers must use explicit element counts.
/// </summary>
internal sealed class SizeClassBufferPool
{
    public float[] RentFloat(int size)
    {
        var sizeClass = GetSizeClass(size);
        if (_floatPools.TryGetValue(sizeClass, out var bag) && bag.TryTake(out var array))
            return array;

        return new float[size];
    }

    public void ReturnFloat(float[] array)
    {
        var bag = _floatPools.GetOrAdd(GetSizeClass(array.Length), static _ => new ConcurrentBag<float[]>());
        if (bag.Count < MaxPoolPerClass)
            bag.Add(array);
    }

    public byte[] RentByte(int size)
    {
        var sizeClass = GetSizeClass(size);
        if (_bytePools.TryGetValue(sizeClass, out var bag) && bag.TryTake(out var array))
            return array;

        return new byte[size];
    }

    public void ReturnByte(byte[] array)
    {
        var bag = _bytePools.GetOrAdd(GetSizeClass(array.Length), static _ => new ConcurrentBag<byte[]>());
        if (bag.Count < MaxPoolPerClass)
            bag.Add(array);
    }

    private static int GetSizeClass(int size)
    {
        foreach (var sizeClass in SizeClasses)
        {
            if (size <= sizeClass)
                return sizeClass;
        }

        return size;
    }

    private const int MaxPoolPerClass = 8;
    private static readonly int[] SizeClasses = { 64, 128, 256, 512, 1024, 2048, 4096, 8192 };

    private readonly ConcurrentDictionary<int, ConcurrentBag<float[]>> _floatPools = new();
    private readonly ConcurrentDictionary<int, ConcurrentBag<byte[]>> _bytePools = new();
}
