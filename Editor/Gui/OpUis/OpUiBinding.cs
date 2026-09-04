#nullable enable
using System.Reflection;
using T3.Core.Operator;
using T3.Core.Utils;

namespace T3.Editor.Gui.OpUis;

/// <summary>
/// This abstract class will later allow caching of the parameter input references.
/// </summary>
internal abstract class OpUiBinding
{
    public bool IsValid;

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    internal sealed class BindInputAttribute : Attribute
    {
        public Guid Id { get; }
        public BindInputAttribute(string id) => Id = new Guid(id);
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    internal sealed class BindOutputAttribute : Attribute
    {
        public Guid Id { get; }
        public BindOutputAttribute(string id) => Id = new Guid(id);
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    internal sealed class BindPropertyAttribute : Attribute
    {
        public string PropertyName { get; }
        public BindPropertyAttribute(string propertyName) => PropertyName = propertyName;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    internal sealed class BindFieldAttribute : Attribute
    {
        public string FieldName { get; }
        public BindFieldAttribute(string fieldName) => FieldName = fieldName;
    }

    protected bool AutoBind(Instance instance)
    {
        bool allFound = true;

        var members = GetType().GetMembers(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        foreach (var member in members)
        {
            Type? memberType = member switch
                                   {
                                       FieldInfo f    => f.FieldType,
                                       PropertyInfo p => p.PropertyType,
                                       _              => null
                                   };

            if (memberType == null)
                continue;

            object? valueToBind = null;

            if (member.GetCustomAttribute<BindInputAttribute>() is { } inputAttr)
            {
                valueToBind = instance.Inputs.FirstOrDefault(i =>
                                                                 i.Id == inputAttr.Id && memberType.IsInstanceOfType(i));

                if (valueToBind == null)
                {
                    Log.Warning($" Failed to bind {instance} input {inputAttr.Id.ShortenGuid()}", instance);
                }
            }
            else if (member.GetCustomAttribute<BindOutputAttribute>() is { } outputAttr)
            {
                valueToBind = instance.Outputs.FirstOrDefault(o =>
                                                                  o.Id == outputAttr.Id && memberType.IsInstanceOfType(o));
                
                if (valueToBind == null)
                {
                    Log.Warning($" Failed to bind {instance} output {outputAttr.Id.ShortenGuid()}", instance);
                }
                
            }
            else if (member.GetCustomAttribute<BindPropertyAttribute>() is { } propAttr)
            {
                valueToBind = instance.GetType().GetProperty(propAttr.PropertyName,
                                                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                
                if (valueToBind == null)
                {
                    Log.Warning($" Failed to bind {instance} property {propAttr.PropertyName}", instance);
                }
            }
            else if (member.GetCustomAttribute<BindFieldAttribute>() is { } fieldAttr)
            {
                valueToBind = instance.GetType().GetField(fieldAttr.FieldName,
                                                          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                
                if (valueToBind == null)
                {
                    Log.Warning($" Failed to bind {instance} field {fieldAttr.FieldName}", instance);
                }
            }

            if (valueToBind != null)
            {
                switch (member)
                {
                    case FieldInfo field:
                        field.SetValue(this, valueToBind);
                        break;
                    case PropertyInfo prop when prop.CanWrite:
                        prop.SetValue(this, valueToBind);
                        break;
                }
            }
            else if (member.IsDefined(typeof(BindInputAttribute)) ||
                     member.IsDefined(typeof(BindOutputAttribute)) ||
                     member.IsDefined(typeof(BindPropertyAttribute)) ||
                     member.IsDefined(typeof(BindFieldAttribute)))
            {
                
                allFound = false;
            }
        }

        return allFound;
    }
}