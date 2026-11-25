#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.DataTypes;

namespace T3.Core.Resource.ShaderCompiling;

public abstract partial class ShaderCompiler
{
    private static ShaderCompiler? _instance;

    public static ShaderCompiler Instance
    {
        get => _instance!;
        set
        {
            if (_shutdown)
            {
                throw new InvalidOperationException($"Can't set {nameof(ShaderCompiler)}.{nameof(Instance)} after shutdown.");
            }
            
            if (_instance != null)
            {
                throw new InvalidOperationException($"Can't set {nameof(ShaderCompiler)}.{nameof(Instance)} twice.");
            }

            _instance = value;
        }
    }

    /// <param name="diagnostics">Filled with every error and warning the compiler reported when compiling fails.</param>
    protected abstract bool CompileShaderFromSource<TShader>(ShaderCompiler.ShaderCompilationArgs args, out byte[] blob, out string errorMessage,
                                                             List<ShaderCompileFailures.Diagnostic> diagnostics)
        where TShader : AbstractShader;

    protected abstract void CreateShaderInstance<TShader>(string name, in byte[] blob, out TShader shader)
        where TShader : AbstractShader;

    public static void Shutdown()
    {
        _instance = null;
        _shutdown = true;
    }

    private static bool _shutdown;
}