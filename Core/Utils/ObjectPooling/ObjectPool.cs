using System;
using System.Collections.Generic;

// ReSharper disable FieldCanBeMadeReadOnly.Local

namespace T3.Core.Utils.ObjectPooling;

public sealed class ObjectPool<T>(Func<T> createFunc, Action<T> returnAction = null, int capacity = 10) : ObjectPoolBase<T>
{
    private Queue<T> _objects = new(capacity);

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