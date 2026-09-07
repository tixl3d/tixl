using System;

namespace T3.Core.Utils.ObjectPooling;

public sealed class ArrayPool<T>
{
    private readonly ObjectPoolBase<T[]> _pool;
    private readonly int _arrayLength;

    public ArrayPool(bool concurrent, int arrayLength)
    {
        _arrayLength = arrayLength;
        if (concurrent)
        {
            _pool = new ConcurrentObjectPool<T[]>(CreateFunc);
        }
        else
        {
            _pool = new ObjectPool<T[]>(CreateFunc);
        }
    }

    public T[] Get() => _pool.Get();

    public void Return(T[] obj, bool clear)
    {
        var length = _arrayLength;
        if (obj.Length != length)
        {
            throw new ArgumentException("Invalid array length - this array did not come from this pool", nameof(obj));
        }
        
        if (clear)
        {
            for (var i = 0; i < length; i++)
            {
                obj[i] = default;
            }
        }

        _pool.Return(obj);
    }

    private T[] CreateFunc()
    {
        return new T[_arrayLength];
    }
}