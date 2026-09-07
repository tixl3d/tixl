#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.DataTypes.ShaderGraph;

/// <summary>
/// Handles the collection and graph parameters and the insertion of padding params to ensure the alignment
/// ConstantBuffers to 16byte borders.
/// </summary>
public static class ShaderParamHandling
{
    internal static List<ShaderParamInput> CollectInputSlots(Instance instance, string nodePrefix)
    {
        List<ShaderParamInput> inputSlots = [];
        var fields = instance.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var field in fields)
        {
            // Check if the field is of type InputSlot and has the [GraphParam] attribute
            if (field.GetCustomAttribute<GraphParamAttribute>() == null)
                continue;

            if (field.GetValue(instance) is not IInputSlot slot)
                continue;

            inputSlots
               .Add(slot switch
                        {
                            Slot<float> floatSlot
                                => new ShaderParamInput(slot,
                                                        field.Name,
                                                        "float",
                                                        (floatValues, codeParams)
                                                            =>
                                                        {
                                                            AddScalarParameter(floatValues, codeParams, $"{nodePrefix}{field.Name}", floatSlot.Value);
                                                        },
                                                        context => { floatSlot.GetValue(context); }
                                                       ),
                            Slot<Vector2> vec2Slot
                                => new ShaderParamInput(
                                                        slot,
                                                        field.Name,
                                                        "float2",
                                                        (floatValues, codeParams)
                                                            =>
                                                        {
                                                            AddVec2Parameter(floatValues, codeParams, $"{nodePrefix}{field.Name}", vec2Slot.Value);
                                                        },
                                                        context => { vec2Slot.GetValue(context); }
                                                       ),
                            Slot<Vector3> vec3Slot
                                => new ShaderParamInput(slot,
                                                        field.Name,
                                                        "float3",
                                                        (floatValues, codeParams)
                                                            =>
                                                        {
                                                            AddVec3Parameter(floatValues, codeParams, $"{nodePrefix}{field.Name}", vec3Slot.Value);
                                                        },
                                                        context => { vec3Slot.GetValue(context); }
                                                       ),
                            Slot<Vector4> vec4Slot
                                => new ShaderParamInput(slot,
                                                        field.Name,
                                                        "float4",
                                                        (floatValues, codeParams)
                                                            =>
                                                        {
                                                            AddVec4Parameter(floatValues, codeParams, $"{nodePrefix}{field.Name}", vec4Slot.Value);
                                                        },
                                                        context => { vec4Slot.GetValue(context); }
                                                       ),

                            Slot<Matrix4x4> matrixSlot
                                => new ShaderParamInput(slot,
                                                        field.Name,
                                                        "float4x4",
                                                        (floatValues, codeParams)
                                                            =>
                                                        {
                                                            AddMatrixParameter(floatValues, codeParams, $"{nodePrefix}{field.Name}", matrixSlot.Value);
                                                        },
                                                        context => { matrixSlot.GetValue(context); }
                                                       ),

                            Slot<int> intSlot
                                => new ShaderParamInput(slot,
                                                        field.Name,
                                                        "int",
                                                        (floatValues, codeParams)
                                                            =>
                                                        {
                                                            AddScalarParameter(floatValues, codeParams, $"{nodePrefix}{field.Name}", intSlot.Value);
                                                        },
                                                        context => { intSlot.GetValue(context); }
                                                       ),
                            _ => throw new ArgumentOutOfRangeException()
                        });
        }

        return inputSlots;
    }

    internal static void AddScalarParameter(List<float> floatValues, List<ShaderCodeParameter> codeParams, string name, float value)
    {
        floatValues.Add(value);
        codeParams.Add(new ShaderCodeParameter("float", name));
    }

    internal static void AddVec2Parameter(List<float> floatValues, List<ShaderCodeParameter> codeParams, string name, Vector2 value)
    {
        PadFloatParametersToVectorComponentCount(floatValues, codeParams, 2);
        floatValues.Add(value.X);
        floatValues.Add(value.Y);
        codeParams.Add(new ShaderCodeParameter("float2", name));
    }

    internal static void AddVec3Parameter(List<float> floatValues, List<ShaderCodeParameter> codeParams, string name, Vector3 value)
    {
        PadFloatParametersToVectorComponentCount(floatValues, codeParams, 3);
        floatValues.Add(value.X);
        floatValues.Add(value.Y);
        floatValues.Add(value.Z);
        codeParams.Add(new ShaderCodeParameter("float3", name));
    }

    private static void AddVec4Parameter(List<float> floatValues, List<ShaderCodeParameter> codeParams, string name, Vector4 value)
    {
        PadFloatParametersToVectorComponentCount(floatValues, codeParams, 4);
        floatValues.Add(value.X);
        floatValues.Add(value.Y);
        floatValues.Add(value.Z);
        floatValues.Add(value.W);
        codeParams.Add(new ShaderCodeParameter("float4", name));
    }

    internal static void AddMatrixParameter(List<float> floatValues, List<ShaderCodeParameter> codeParams, string name, Matrix4x4 matrix)
    {
        PadFloatParametersToVectorComponentCount(floatValues, codeParams, 4);
        Span<float> elements = MemoryMarshal.CreateSpan(ref matrix.M11, 16);
        foreach (var value in elements)
        {
            floatValues.Add(value);
        }

        codeParams.Add(new ShaderCodeParameter("float4x4", name));
    }

    /**
         *  |0123|0123|
         *  |VVV |    | 0 ok
         *  | VVV|    | 1 ok
         *  |  VV|V   | 2 -> padBy 2
         *  |   V|VV  | 3 -> padBy 1
         *
         *  |0123|0123|
         *  |vvvv|    | 0 ok
         *  | vvv|v   | 1 -> padBy 3
         *  |  vv|vv  | 2 -> padBy 2
         *  |   v|vvv | 3 -> padBy 1
        */
    private static void PadFloatParametersToVectorComponentCount(List<float> values, List<ShaderCodeParameter> codeParams,
                                                                 int size)
    {
        var currentStart = values.Count % 4;
        var requiredPadding = 0;
        if (size == 2)
        {
            requiredPadding = currentStart % 2;
        }
        else if (size == 3)
        {
            if (currentStart == 2)
                requiredPadding = 2;
            else if (currentStart == 3)
                requiredPadding = 1;
        }
        else if (size == 4)
        {
            requiredPadding = (4 - currentStart) % 4;
        }

        for (var i = 0; i < requiredPadding; i++)
        {
            values.Add(0);
            codeParams.Add(new ShaderParamHandling.ShaderCodeParameter("float", "__padding" + values.Count));
        }
    }

    /** We collect these at instance construction to avoid later casting */
    internal sealed record ShaderParamInput(
        IInputSlot Slot,
        string Name,
        string ShaderTypeName,
        GetFloatDelegate GetAsFloatValues,
        UpdateDelegate Update);

    public sealed record ShaderCodeParameter(string ShaderTypeName, string Name);

    internal delegate void GetFloatDelegate(List<float> floatValues,
                                            List<ShaderCodeParameter> codeParams);

    internal delegate void UpdateDelegate(EvaluationContext context);
}