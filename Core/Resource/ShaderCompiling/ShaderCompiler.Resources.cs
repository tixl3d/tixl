#nullable enable
using T3.Core.DataTypes;
using T3.Core.Logging;

namespace T3.Core.Resource.ShaderCompiling;

public abstract partial class ShaderCompiler
{
    public static bool TryGetShaderFromFile<TShader>(FileResource fileResource, ref TShader? shader, IResourceConsumer? instance,
                                                     out string reason, string entryPoint = "main", bool forceRecompile = false)
        where TShader : AbstractShader
    {
        if(string.IsNullOrWhiteSpace(entryPoint))
            entryPoint = "main";
        
        var fileArgs = new ShaderCompilationFileArgs(fileResource, entryPoint, instance, shader?.CompiledBytecode);
        if (TryPrepareSourceFile(fileArgs, out reason, out var args))
        {
            return TryCompileShaderFromSource(args, true, forceRecompile, out shader, out reason);
        }
        
        shader = null;
        Log.Error($"{fileResource.AbsolutePath} -> {entryPoint}: {reason}");
        return false;
    }
}