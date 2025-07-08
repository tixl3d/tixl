using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource;

namespace T3.Core.Compilation;

public sealed partial class AssemblyInformation
{
    /// <summary>
    /// The routine that calls relevant methods to extract operator information from the types in the assembly and caches them.
    /// </summary>
    /// <param name="types"></param>
    /// <param name="assembly"></param>
    /// <param name="shouldShareResources"></param>
    /// <param name="namespaces"></param>
    /// <param name="typesByName"></param>
    /// <param name="operatorTypeInfo"></param>
    private static void LoadTypes(Type[] types, Assembly assembly, out bool shouldShareResources, ConcurrentDictionary<Guid, OperatorTypeInfo> operatorTypeInfo,
                                  HashSet<string> namespaces, Dictionary<string, Type> typesByName)
    {
        if (!operatorTypeInfo.IsEmpty)
        {
            throw new InvalidOperationException("Operator types already loaded");
        }

        foreach (var type in types)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (type == null)
            {
                Log.Error($"Null type in assembly {assembly.FullName}");
                continue;
            }

            var nsp = type.Namespace;
            if (nsp != null)
                namespaces.Add(nsp);

            var name = type.FullName;
            if (name == null)
                continue;

            if (!typesByName.TryAdd(name, type))
            {
                Log.Warning($"Duplicate type {name} in assembly {assembly.FullName}");
            }
        }

        ConcurrentBag<Type> nonOperatorTypes = [];

        typesByName.Values.AsParallel().ForAll(type =>
                                               {
                                                   if (!type.IsAssignableTo(typeof(Instance)))
                                                   {
                                                       nonOperatorTypes.Add(type);
                                                   }
                                                   else
                                                   {
                                                       try
                                                       {
                                                           SetUpOperatorType(type, operatorTypeInfo);
                                                       }
                                                       catch (Exception e)
                                                       {
                                                           Log.Error($"Failed to set up operator type {type.FullName}\n{e.Message}");
                                                       }
                                                   }
                                               });

        var assemblyLocation = assembly.Location;
        shouldShareResources = nonOperatorTypes
                              .Where(type =>
                                     {
                                         // check for shareable type
                                         if (!type.IsAssignableTo(typeof(IShareResources)))
                                         {
                                             return false;
                                         }

                                         try
                                         {
                                             var obj = Activator.CreateInstanceFrom(
                                                                                    assemblyFile: assemblyLocation,
                                                                                    typeName: type.FullName!,
                                                                                    ignoreCase: false,
                                                                                    bindingAttr: ConstructorBindingFlags,
                                                                                    binder: null, args: null, culture: null, activationAttributes: null);
                                             var unwrapped = obj?.Unwrap();
                                             if (unwrapped is IShareResources shareable)
                                             {
                                                 return shareable.ShouldShareResources;
                                             }

                                             Log.Error($"Failed to create {nameof(IShareResources)} for {type.FullName}");
                                         }
                                         catch (Exception e)
                                         {
                                             Log.Error($"Failed to create shareable resource for {type.FullName}\n{e.Message}");
                                         }

                                         return false;
                                     }).Any();
    }

    /// <summary>
    /// Actually extracts operator information from the type - this is the meat and beans of how we get the operator's slots, implemented interfaces, etc
    /// </summary>
    private static void SetUpOperatorType(Type type, ConcurrentDictionary<Guid, OperatorTypeInfo> operatorTypeInfo)
    {
        var gotGuid = TryGetGuidOfType(type, out var id);

        if (!gotGuid)
        {
            Log.Error($"Failed to get guid for {type.FullName}");
            return;
        }

        bool isGeneric = type.IsGenericTypeDefinition;

        var bindFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static;

        var allMembers = type.GetMembers(bindFlags);
        var memberNames = new string[allMembers.Length];

        List<InputSlotInfo> inputFields = new();
        List<OutputSlotInfo> outputFields = new();

        for (int i = 0; i < allMembers.Length; i++)
        {
            var member = allMembers[i];
            var name = member.Name;
            memberNames[i] = name;

            if (member is not FieldInfo field || field.IsSpecialName)
            {
                continue;
            }

            var fieldType = field.FieldType;

            if (!fieldType.IsAssignableTo(typeof(ISlot)))
                continue;

            if (field.IsStatic)
            {
                Log.Error($"Static slot '{name}' in '{type.FullName}' is not allowed - please remove the static modifier");
                continue;
            }

            if (!field.IsInitOnly)
            {
                Log.Warning($"Slot '{name}' in '{type.FullName}' is not read-only - it is recommended to make slots read-only");
            }

            var genericArguments = fieldType.GetGenericArguments();
            if (fieldType.IsAssignableTo(typeof(IInputSlot)))
            {
                var inputAttribute = member.GetCustomAttribute<InputAttribute>();
                if (inputAttribute is null)
                {
                    Log.Error($"Input slot {name} in {type.FullName} is missing {nameof(InputAttribute)}");
                    continue;
                }

                var genericTypeDefinition = fieldType.GetGenericTypeDefinition();
                var isMultiInput = genericTypeDefinition == typeof(MultiInputSlot<>);

                int genericIndex = GetSlotGenericIndex(isGeneric, fieldType);
                inputFields.Add(new InputSlotInfo(name, inputAttribute, genericArguments, field, isMultiInput, genericIndex));
            }
            else
            {
                var outputAttribute = member.GetCustomAttribute<OutputAttribute>();
                if (outputAttribute is null)
                {
                    Log.Error($"Output slot {name} in {type.FullName} is missing {nameof(OutputAttribute)}");
                    continue;
                }

                var genericIndex = GetSlotGenericIndex(isGeneric, fieldType);
                outputFields.Add(new OutputSlotInfo(name, outputAttribute, fieldType, genericArguments, field, genericIndex));
            }
        }

        ExtractableTypeInfo extractableTypeInfo = default;
        var isDescriptive = false;

        // collect information about implemented interfaces
        var interfaces = type.GetInterfaces();
        foreach (var interfaceType in interfaces)
        {
            if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IExtractedInput<>))
            {
                var extractableType = interfaceType.GetGenericArguments().Single();
                extractableTypeInfo = new ExtractableTypeInfo(true, extractableType);
            }
            else if (interfaceType == typeof(IDescriptiveFilename))
            {
                isDescriptive = true;
            }
        }

        var added = operatorTypeInfo.TryAdd(id, new OperatorTypeInfo(
                                                                     type: type,
                                                                     inputs: inputFields,
                                                                     isGeneric: isGeneric,
                                                                     outputs: outputFields,
                                                                     memberNames: memberNames,
                                                                     isDescriptiveFileNameType: isDescriptive,
                                                                     extractableTypeInfo: extractableTypeInfo));

        if (!added)
        {
            Log.Error($"Failed to add operator type {type.FullName} with guid {id} because the id was already in use by {operatorTypeInfo[id].Type.FullName}");
        }

        return;

        static int GetSlotGenericIndex(bool isGeneric, Type fieldType)
        {
            int genericIndex = -1;
            if (isGeneric && fieldType.IsGenericTypeDefinition)
            {
                genericIndex = fieldType.GenericParameterPosition;
            }

            return genericIndex;
        }
    }

    /// <summary>
    /// Tries to get the GuidAttribute from the given type. If the type has no GuidAttribute, or multiple GuidAttributes, this method will return false.
    /// Todo: this should support multiple guids for operator refactoring/replacement/deprecation purposes
    /// </summary>
    private static bool TryGetGuidOfType(Type newType, out Guid guid)
    {
        var guidAttributes = newType.GetCustomAttributes(typeof(GuidAttribute), false);
        switch (guidAttributes.Length)
        {
            case 0:
                Log.Error($"Type {newType.Name} has no GuidAttribute");
                guid = Guid.Empty;
                return false;

            case 1: // This is what we want - types with a single GuidAttribute
                var guidAttribute = (GuidAttribute)guidAttributes[0];
                var guidString = guidAttribute.Value;

                if (!Guid.TryParse(guidString, out guid))
                {
                    Log.Error($"Type {newType.Name} has invalid GuidAttribute");
                    return false;
                }

                return true;
            default:
                // this indicates there are multiple GuidAttributes on the type
                // we may want to support this at some point to allow for "refactoring" of operators
                // but it is not currently supported
                Log.Error($"Type {newType.Name} has multiple GuidAttributes");
                guid = Guid.Empty;
                return false;
        }
    }
}