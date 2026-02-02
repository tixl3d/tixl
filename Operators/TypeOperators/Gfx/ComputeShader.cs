using ComputeShaderT3 = T3.Core.DataTypes.ComputeShader;

namespace Types.Gfx;

[Guid("a256d70f-adb3-481d-a926-caf35bd3e64c")]
public sealed class ComputeShader : Instance<ComputeShader>, IDescriptiveFilename, IStatusProvider, IShaderOperator<ComputeShaderT3>
{
    [Output(Guid = "{6C118567-8827-4422-86CC-4D4D00762D87}")]
    public readonly Slot<ComputeShaderT3> Shader = new();

    [Output(Guid = "a6fe06e0-b6a9-463c-9e62-930c58b0a0a1")]
    public readonly Slot<Int3> ThreadCount = new();

    public ComputeShader()
    {
        ThreadCount.UpdateAction += context =>
                                    {
                                        var shader = Shader.GetValue(context);
                                        OnShaderUpdate(context, shader);
                                    };
            
        ShaderOperatorImpl.Initialize();
    }

    public void OnShaderUpdate(EvaluationContext context, ComputeShaderT3? shader)
    {
        ThreadCount.DirtyFlag.Clear();
        Shader.DirtyFlag.Clear();
        
        if (shader == null)
        {
            return;
        }
            
        if (shader.TryGetThreadGroups(out var threadCount))
            ThreadCount.Value = threadCount;


    }

    public InputSlot<string> SourcePathSlot => Source;

    [Input(Guid = "{AFB69C81-5063-4CB9-9D42-841B994B5EC0}")]
    public readonly InputSlot<string> Source = new();

    [Input(Guid = "{8AD9E58D-A767-4A5F-BFBF-D082B80901D6}")]
    public readonly InputSlot<string> EntryPoint = new();

    [Input(Guid = "{C0701D0B-D37F-4570-9E9A-EC2E88B919D1}")]
    public readonly InputSlot<string> DebugName = new();
        
        
    #region IShaderOperator implementation
    private IShaderOperator<ComputeShaderT3> ShaderOperatorImpl => this;
    InputSlot<string> IShaderOperator<ComputeShaderT3>.Path => Source;
    InputSlot<string> IShaderOperator<ComputeShaderT3>.EntryPoint => EntryPoint;
    InputSlot<string> IShaderOperator<ComputeShaderT3>.DebugName => DebugName;

    string IShaderOperator<ComputeShaderT3>.CachedEntryPoint { get; set; } = default!; // set by Initialize()

    Slot<ComputeShaderT3> IShaderOperator<ComputeShaderT3>.ShaderSlot => Shader;
    #endregion

    #region IStatusProvider implementation
    private readonly DefaultShaderStatusProvider _statusProviderImplementation = new ();
    public void SetWarning(string message) => _statusProviderImplementation.Warning = message;
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _statusProviderImplementation.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _statusProviderImplementation.GetStatusMessage();
    #endregion
}