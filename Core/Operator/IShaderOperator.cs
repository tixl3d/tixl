#nullable enable
using System;
using T3.Core.DataTypes;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.Stats;
using T3.Core.Utils;
using ShaderCompiler = T3.Core.Resource.ShaderCompiling.ShaderCompiler;

namespace T3.Core.Operator;

/// <summary>
/// An interface for shader operators (PixelShader, VertexShader, etc.)
/// Shader updating can be complex, so this interface is used to provide the common functionality for all of them
/// </summary>
/// <typeparam name="T">The type of shader (i.e. T3.Core.DataTypes.PixelShader)</typeparam>

public interface IShaderCodeOperator<T> where T : AbstractShader
{
    public InputSlot<string> Code { get; }
    public Slot<T> ShaderSlot { get; }
    public InputSlot<string> EntryPoint { get; }
    public InputSlot<string> DebugName { get; }

    public void SetWarning(string message);

    void Initialize()
    {
        Action<EvaluationContext> invalidateShader = context => ShaderSlot.DirtyFlag.Invalidate();
        Code.UpdateAction += invalidateShader;
        EntryPoint.UpdateAction += invalidateShader;
        ShaderSlot.UpdateAction += UpdateShader;
    }

    private void UpdateShader(EvaluationContext context)
    {
        // cache interface values to avoid additional virtual method calls
        var sourceSlot = Code;
        var entryPointSlot = EntryPoint;
        var debugNameSlot = DebugName;

        var debugName = debugNameSlot.GetValue(context);
        var shaderSlot = ShaderSlot;
        var currentShader = shaderSlot.Value;
        if (currentShader != null)
            currentShader.Name = debugName ?? string.Empty;

        if (!sourceSlot.DirtyFlag.IsDirty && !entryPointSlot.DirtyFlag.IsDirty)
        {
            return;
        }
        
        var previousSource = Code.Value;
        var newSource = sourceSlot.GetValue(context);
        if(previousSource==newSource)
            return;
        
        var entryPoint = entryPointSlot.GetValue(context);
        var instance = sourceSlot.Parent;

        if (string.IsNullOrWhiteSpace(newSource))
        {
            SetWarning("Shader code is empty");
            return;
        }

        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            SetWarning("Entry point is empty");
            return;
        }

        //Log.Debug($"Attempting to update shader \"{debugName}\" ({GetType().Name}) with entry point \"{entryPoint}\".");

        if (string.IsNullOrWhiteSpace(debugName))
        {
            debugName = $"{typeof(T).Name}";
        }

        var compilationArgs = new ShaderCompiler.ShaderCompilationArgs(
                                                                       SourceCode: newSource,
                                                                       EntryPoint: entryPoint,
                                                                       Owner: instance,
                                                                       Name: debugName,
                                                                       OldBytecode: currentShader?.CompiledBytecode);
        
        var compiled = ShaderCompiler.TryCompileShaderFromSource(compilationArgs, true, true, out currentShader, out var errorMessage);
                
        shaderSlot.Value = currentShader!;
        shaderSlot.DirtyFlag.Clear();

        if (!compiled)
        {
            instance.LogErrorState(errorMessage);
        }
        else
        {
            instance.ClearErrorState();
            currentShader!.Name = debugName;
        }
        
        SetWarning(errorMessage);
    }
}

public interface IShaderOperator<T> : IDescriptiveFilename where T : AbstractShader
{
    public InputSlot<string> Path { get; }
    InputSlot<string> IDescriptiveFilename.SourcePathSlot => Path;
    public Slot<T> ShaderSlot { get; }
    public InputSlot<string> EntryPoint { get; }
    public InputSlot<string> DebugName { get; }

    public void SetWarning(string message);
    
    protected string CachedEntryPoint { get; set; }
    void OnShaderUpdate(EvaluationContext context, T? shader);

    /// <summary>
    /// Outputs derived from the compiled shader (e.g. a compute shader's thread-group count) that must be
    /// re-invalidated when the shader file recompiles. A live recompile force-invalidates only the resource's
    /// dependent slots, so a second derived output must be listed here — otherwise downstream consumers keep
    /// the stale value until the graph is invalidated some other way (reconnect, restart).
    /// </summary>
    protected ISlot[]? ShaderDerivedOutputs => null;

    IResourceConsumer Instance => ShaderSlot.Parent;

    void Initialize()
    {
        var resource = ResourceManager.CreateShaderResource<T>(Path, () => CachedEntryPoint);
        var shaderSlot = ShaderSlot;
        resource.AddDependentSlots(shaderSlot);

        var derivedOutputs = ShaderDerivedOutputs;
        if (derivedOutputs != null)
            resource.AddDependentSlots(derivedOutputs);

        shaderSlot.UpdateAction = context =>
                                  {
                                      if (EntryPoint.DirtyFlag.IsDirty)
                                      {
                                          // we still need to recompile if the entrypoint changes
                                          resource.MarkFileAsChanged();
                                      }
                                      
                                      CachedEntryPoint = EntryPoint.GetValue(context) ?? string.Empty;
                                      
                                      // This is a hack to invalidate the input so that the op doesn't stay dirtly
                                      _ = DebugName.GetValue(context);
                                      
                                      var shader = resource.GetValue(context);
                                      ShaderSlot.Value = shader!;
                                      OnShaderUpdate(context, shader);
                                  };
    }
}