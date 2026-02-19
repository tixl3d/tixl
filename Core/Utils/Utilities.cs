using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.Utils;

// Todo: this class needs to be cleaned up!
public static class Utilities
{
    // This has been implemented by the C# compiler and is unnecessary. i.e. (a,b) = (b,a), (a, b) = kvp
    public static void Deconstruct<T1, T2>(this KeyValuePair<T1, T2> tuple, out T1 key, out T2 value)
    {
        key = tuple.Key;
        value = tuple.Value;
    }

    public static void Dispose<T>(ref T obj) where T : class, IDisposable
    {
        obj?.Dispose();
        obj = null;
    }

    public static void Swap<T>(this IList<T> list, int indexA, int indexB)
    {
        (list[indexA], list[indexB]) = (list[indexB], list[indexA]);
    }

    public static void Swap<T>(ref T a, ref T b)
    {
        (a, b) = (b, a);
    }

    public static float[] GetFloatsFromVector<T>(T v)
    {
        if (v is float v1)
        {
            return new[] { v1 };
        }
            
        if (v is Vector2 v2)
        {
            return new[] { v2.X, v2.Y };
        }
            
        if (v is Vector3 v3)
        {
            return new[] { v3.X, v3.Y, v3.Z };
        }

        if (v is Vector4 v4)
        {
            return new[] { v4.X, v4.Y, v4.Z, v4.W };
        }
        return Array.Empty<float>();
    }
        
    /// <summary>
    /// Clamps an integer to the number of enums.
    /// This prevents cast exceptions if index is out of range.   
    /// </summary>
    /// <remarks>Note that this doesn't work for Enums with non-zero start index.</remarks>
    public static T GetEnumValue<T>(this InputSlot<int> intInputSlot, EvaluationContext context) where T : Enum
    {
        var i = intInputSlot.GetValue(context).Clamp(0, Enum.GetValues(typeof(T)).Length - 1);
        return CastTo<T>.From(i);
    }
        
    public static int Hash<T>(T a, T b)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + a.GetHashCode();
            hash = hash * 31 + b.GetHashCode();
            return hash;
        }
    }

    public static void CopyImageMemory(IntPtr srcData,  IntPtr dstData, int height, int srcStride, int dstStride)
    {
        // Fast path, both strides are the same
        if (srcStride == dstStride)
        {
            SharpDX.Utilities.CopyMemory(dstData, srcData, height * srcStride);
        }
        else
        {
            //We could pass row width as argument, bu the smallest of each stride is enough here
            int rowWidth = Math.Min(srcStride, dstStride);
            for (int i = 0; i < height; i++)
            {
                SharpDX.Utilities.CopyMemory(dstData, srcData, rowWidth);
                srcData += srcStride;
                dstData += dstStride;   
            }
        }
    }

    /// <summary>
    /// Infinite modulo indexer, also allows to reference array indexes using negative values
    /// Note: assumes count is positive, does not perform check for it
    /// </summary>
    /// <param name="index">Index value</param>
    /// <param name="count">Element count</param>
    /// <returns>A valid index to lookup in the array</returns>
    public static int InfiniteModIndexer(int index, int count)
    {
        if (count == 0)
            return 0;

        if (index < count)
        {
            if (index >= 0)
            {
                return index;
            }
            else
            {
                int remainder = index % count;
                return remainder == 0 ? 0 : remainder + count;
            }
        }
        else
        {
            return index % count;
        }
    }

    /// <summary>
    /// Useful for checking if a reference has changed without keeping an GC reference. 
    /// </summary>
    public static bool HasObjectChanged(object obj, ref int? lastObjectId)
    {
        int? id = obj is null ? null : RuntimeHelpers.GetHashCode(obj);
        if (id == lastObjectId)
            return false;

        lastObjectId = id;
        return true;
    }
}

public static class CastTo<TTarget>
{
    public static TTarget From<TSource>(TSource source)
    {
        return Cache<TSource>.Caster(source);
    }

    private static class Cache<TSource>
    {
        public static readonly Func<TSource, TTarget> Caster = Get();

        private static Func<TSource, TTarget> Get()
        {
            var p = Expression.Parameter(typeof(TSource));
            var c = Expression.ConvertChecked(p, typeof(TTarget));
            return Expression.Lambda<Func<TSource, TTarget>>(c, p).Compile();
        }
    }
}