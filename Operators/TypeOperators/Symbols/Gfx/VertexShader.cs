using VertexShaderT3 = T3.Core.DataTypes.VertexShader;

namespace Types.Gfx;

[Guid("646f5988-0a76-4996-a538-ba48054fd0ad")]
public sealed class VertexShader : Instance<VertexShader>, IDescriptiveFilename, IStatusProvider, IShaderOperator<VertexShaderT3>
{
    [Output(Guid = "ED31838B-14B5-4875-A0FC-DC427E874362")]
    public readonly Slot<VertexShaderT3> Shader = new();

    public VertexShader()
    {
        ShaderOperatorImpl.Initialize();
    }

    public InputSlot<string> SourcePathSlot => Source;


    [Input(Guid = "78FB7501-74D9-4A27-8DB2-596F25482C87")]
    public readonly InputSlot<string> Source = new();

    [Input(Guid = "9A8B500E-C3B1-4BE1-8270-202EF3F90793")]
    public readonly InputSlot<string> EntryPoint = new();

    [Input(Guid = "C8A59CF8-6612-4D57-BCFD-3AEEA351BA50")]
    public readonly InputSlot<string> DebugName = new();
        

    #region IShaderOperator implementation
    private IShaderOperator<VertexShaderT3> ShaderOperatorImpl => this;
    InputSlot<string> IShaderOperator<VertexShaderT3>.Path => Source;
    InputSlot<string> IShaderOperator<VertexShaderT3>.EntryPoint => EntryPoint;
    InputSlot<string> IShaderOperator<VertexShaderT3>.DebugName => DebugName;
    Slot<VertexShaderT3> IShaderOperator<VertexShaderT3>.ShaderSlot => Shader;
    string IShaderOperator<VertexShaderT3>.CachedEntryPoint { get; set; } = default!; // set by Initialize()
    public void OnShaderUpdate(EvaluationContext context, VertexShaderT3? shader)
    {
            
    }
    #endregion
        
        
    #region IStatusProvider implementation
    private readonly DefaultShaderStatusProvider _statusProviderImplementation = new ();
    public void SetWarning(string message) => _statusProviderImplementation.Warning = message;

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _statusProviderImplementation.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _statusProviderImplementation.GetStatusMessage();
    #endregion
}