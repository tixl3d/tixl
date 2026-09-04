#nullable enable
using System;
using System.Linq;
using System.Reflection;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;

namespace T3.Core.Compilation;

/// <summary>
/// Used to organize type information about output slots in <see cref="AssemblyInformation"/>
/// </summary>
public readonly record struct OutputSlotInfo
{
    internal readonly Type? OutputDataType;

    internal OutputSlotInfo(string name, OutputAttribute attribute, Type type, Type[] genericArguments, FieldInfo field, int genericTypeIndex)
    {
        Name = name;
        Attribute = attribute;
        GenericArguments = genericArguments;
        GenericTypeIndex = genericTypeIndex;
        OutputDataType = GetOutputDataType(type);
        IsGeneric = genericTypeIndex >= 0;
        _field = field;
    }

    public string Name { get; }
    public OutputAttribute Attribute { get; }
    public Type[] GenericArguments { get; }
    public int GenericTypeIndex { get; }
    public bool IsGeneric { get; }

    private readonly FieldInfo _field;

    internal ISlot GetSlotObject(object instance)
    {
        return (ISlot)_field.GetValue(instance)!;
    }

    private static Type? GetOutputDataType(Type fieldType)
    {
        var interfaces = fieldType.GetInterfaces();
        Type? foundInterface = null;
        foreach (var i in interfaces)
        {
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IOutputDataUser<>))
            {
                foundInterface = i;
                break;
            }
        }

        return foundInterface?.GetGenericArguments().Single();
    }
}