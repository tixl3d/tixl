#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

// ReSharper disable ConvertToLocalFunction

namespace T3.Core.Resource;

/// <summary>
/// Creates or loads shaders as "resources" and handles their filehooks, compilation, etc
/// Could do with some simplification - perhaps their arguments should be condensed into a struct?
/// </summary>
public static partial class ResourceManager
{

    public static Resource<TShader> CreateShaderResource<TShader>(string relativePath, IResourceConsumer? instance, Func<string> getEntryPoint, Action<TShader?>? onShaderCompiled = null)
        where TShader : AbstractShader
    {
        ArgumentNullException.ThrowIfNull(getEntryPoint, nameof(getEntryPoint));
        TryGenerate<TShader> func = (FileResource fileResource, 
                                     TShader? currentValue, 
                                     [NotNullWhen(true)]out TShader? newShader, 
                                     [NotNullWhen(false)]out string? reason) =>
                                    {
                                        var success = ShaderCompiling.ShaderCompiler.TryGetShaderFromFile(fileResource, ref currentValue, instance, out reason, getEntryPoint());
                                        newShader = currentValue;
                                        onShaderCompiled?.Invoke(currentValue);
                                        return success;
                                    };

        return new Resource<TShader>(relativePath, instance, func);
    }

    internal static Resource<TShader> CreateShaderResource<TShader>(InputSlot<string> sourceSlot, Func<string> getEntryPoint)
        where TShader : AbstractShader
    {
        ArgumentNullException.ThrowIfNull(getEntryPoint, nameof(getEntryPoint));
        var instance = sourceSlot.Parent;
        TryGenerate<TShader> func = (FileResource fileResource, 
                                     TShader? currentValue, 
                                     [NotNullWhen(true)]out TShader? newShader, 
                                     [NotNullWhen(false)]out string? reason) =>
                                    {
                                        var success = ShaderCompiling.ShaderCompiler.TryGetShaderFromFile(fileResource, ref currentValue, instance, out reason, getEntryPoint());
                                        newShader = currentValue;
                                        return success;
                                    };

        return new Resource<TShader>(sourceSlot, func);
    }
}