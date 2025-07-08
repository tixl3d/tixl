#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using T3.Core.Logging;

namespace T3.Core.Compilation;

/// <summary>
/// The Big Operator Info container, which contains most all relevant information about a given operator type, precalculated and cached for runtime efficiency
/// by <see cref="AssemblyInformation"/>.
/// </summary>
public sealed class OperatorTypeInfo
{
    internal OperatorTypeInfo(List<InputSlotInfo> inputs,
                              List<OutputSlotInfo> outputs,
                              IReadOnlyList<string> memberNames,
                              bool isGeneric,
                              Type type,
                              bool isDescriptiveFileNameType,
                              ExtractableTypeInfo extractableTypeInfo)
    {
        // TrimExcess to reduce memory footprint
        inputs.TrimExcess();
        outputs.TrimExcess();
        
        // assign fields
        Inputs = inputs;
        Outputs = outputs;
        MemberNames = memberNames;
        Type = type;
        IsDescriptiveFileNameType = isDescriptiveFileNameType;
        ExtractableTypeInfo = extractableTypeInfo;
        
        // get generics information and constructor
        if (!isGeneric)
        {
            _nonGenericConstructor = Expression.Lambda<Func<object>>(Expression.New(type)).Compile();
        }
        else
        {
            _genericArguments = type.GetGenericArguments();
        }
    }

    internal readonly List<InputSlotInfo> Inputs;
    internal readonly List<OutputSlotInfo> Outputs;
    public readonly IReadOnlyList<string> MemberNames;
    private readonly Type[]? _genericArguments;
    public readonly Type Type;
    public readonly bool IsDescriptiveFileNameType;
    public readonly ExtractableTypeInfo ExtractableTypeInfo;
    
    private readonly Func<object>? _nonGenericConstructor;

    internal Func<object> GetConstructor()
    {
        if (_nonGenericConstructor != null)
            return _nonGenericConstructor;
        throw new InvalidOperationException($"Generic types must be provided for generic operators - use {nameof(TryGetGenericConstructor)} instead");
    }
    
    internal bool TryGetGenericConstructor([NotNullWhen(true)] out Func<object>? constructor, params Type[] genericArguments)
    {
        Type constructedType;
        try
        {
            constructedType = Type.MakeGenericType(genericArguments);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create constructor for {Type.FullName}<{string.Join(", ", genericArguments.Select(t => t.FullName))}>\n{e.Message}");
            constructor = null;
            return false;
        }

        if (_genericConstructors.TryGetValue(constructedType, out constructor))
            return true;
        
        constructor = Expression.Lambda<Func<object>>(Expression.New(constructedType)).Compile();
        _genericConstructors.Add(constructedType, constructor);

        return true;
    }
    

    #region Not needed?

    private static bool TryExtractGenericInformationOf(Type type, 
                                                       [NotNullWhen(true)] out Type[]? genericParameters, 
                                                       [NotNullWhen(true)] out Dictionary<Type, Type[]>? genericTypeConstraints)
    {
        if (!type.IsGenericTypeDefinition)
        {
            genericParameters = null;
            genericTypeConstraints = null;
            return false;
        }
        
        genericParameters = type.GetGenericArguments();
        genericTypeConstraints = new Dictionary<Type, Type[]>();
        foreach (var genericParameter in genericParameters)
        {
            var constraints = genericParameter.GetGenericParameterConstraints();
            genericTypeConstraints.Add(genericParameter, constraints);
        }

        return true;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="genericArguments"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">If this method is called on a non-generic operator</exception>
    private bool CanCreateWithArguments(params Type[] genericArguments)
    {
        ArgumentNullException.ThrowIfNull(genericArguments);
        
        if(genericArguments.Length != _genericArguments!.Length)
            throw new InvalidOperationException($"GenericArguments must have {_genericArguments.Length} elements for operator {Type.FullName}");

        var genericTypes = _genericArguments!;
        var genericTypeCount = genericTypes.Length;
        if (genericArguments.Length != genericTypeCount)
            return false;

        for (int i = 0; i < genericTypeCount; i++)
        {
            if (!genericTypes[i].IsAssignableFrom(genericArguments[i]))
                return false;
        }

        return true;
    }
    
    #endregion
    
    private readonly Dictionary<Type, Func<object>> _genericConstructors = new();
}