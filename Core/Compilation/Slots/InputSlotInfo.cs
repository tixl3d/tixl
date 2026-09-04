#nullable enable
using System;
using System.Reflection;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;

namespace T3.Core.Compilation;

/// <summary>
/// Used to organize type information about input slots in <see cref="AssemblyInformation"/>
/// </summary>
public readonly record struct InputSlotInfo
{
    public InputSlotInfo(string Name,
                         InputAttribute Attribute,
                         Type[] GenericArguments,
                         FieldInfo field,
                         bool IsMultiInput,
                         int GenericTypeIndex)
    {
        this.Name = Name;
        this.Attribute = Attribute;
        this.GenericArguments = GenericArguments;
        this.IsMultiInput = IsMultiInput;
        this.GenericTypeIndex = GenericTypeIndex;
        _field = field;
    }

    public bool IsGeneric => GenericTypeIndex >= 0;
    public string Name { get; }
    public InputAttribute Attribute { get; }
    public Type[] GenericArguments { get; }
    private readonly FieldInfo _field;
    public bool IsMultiInput { get; }
    public int GenericTypeIndex { get; }

    internal IInputSlot GetSlotObject(object instance) => (IInputSlot)_field.GetValue(instance)!;
}