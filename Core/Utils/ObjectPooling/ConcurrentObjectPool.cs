using System;
using System.Collections.Concurrent;

// ReSharper disable FieldCanBeMadeReadOnly.Local

namespace T3.Core.Utils.ObjectPooling;

public sealed class ConcurrentObjectPool<T>(Func<T> createFunc, Action<T> returnAction = null) : ObjectPoolBase<T>
{
    private ConcurrentQueue<T> _objects = new();

    public T Get()
    {
        if (!_objects.TryDequeue(out var obj))
        {
            obj = createFunc();
        }
        
        return obj;
    }
    
    public void Return(T obj)
    {
        returnAction?.Invoke(obj);
        _objects.Enqueue(obj);
    }
}
