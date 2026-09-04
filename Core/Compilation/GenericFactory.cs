#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace T3.Core.Compilation;

/// <summary>
/// Just a helper class to create instances of T based on a type.
/// Provides "constructors" (Func&lt;T&gt;) for types that are not known at runtime that are considerably faster than Activator.CreateInstance.
/// </summary>
/// <typeparam name="T">The type to create instances of</typeparam>
public sealed class GenericFactory<T>
{
    private readonly Type _defaultType;
    private readonly Type? _enumType;
    private readonly ConcurrentDictionary<Type, Func<T>> _entries = new();
    
    public GenericFactory(Type? defaultType, Type? enumType = null)
    {
        if(defaultType == null || !defaultType.IsAssignableTo(typeof(T)))
            throw new ArgumentException("defaultType must be a subclass of T", nameof(defaultType));
        
        _defaultType = defaultType;
        _enumType = enumType;
    }
    public T CreateFor(Type type)
    {
        if (_entries.TryGetValue(type, out var factory))
        {
            return factory();
        }

        factory = AddFactory(type, null);
        return factory();
    }

    public Func<T> AddFactory(Type type, Func<T>? constructor)
    {
        constructor ??= CreateFactoryFor(type);
        _entries[type] = constructor;
        return constructor;
    }

    private Func<T> CreateFactoryFor(Type type)
    {
        Type genericType;
        if(_enumType != null && type.IsEnum)
        {
            genericType = _enumType.MakeGenericType(type);
        }
        else
        {
            genericType = _defaultType.MakeGenericType(type);
        }
            
        var compiledConstructor = Expression.Lambda<Func<T>>(Expression.New(genericType)).Compile();
        return () => (T)compiledConstructor();
    }

}