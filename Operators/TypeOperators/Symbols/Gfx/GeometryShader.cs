using GeometryShaderT3 = T3.Core.DataTypes.GeometryShader;

namespace Types.Gfx;

[Guid("a908cc64-e8cb-490c-ae45-c2c5fbfcedfb")]
public sealed class GeometryShader : Instance<GeometryShader>, IShaderOperator<GeometryShaderT3>, IStatusProvider
{
    [Output(Guid = "85B65C27-D5B3-4FE1-88AF-B1F6ABAA4515")]
    public readonly Slot<GeometryShaderT3> Shader = new();

    public GeometryShader()
    {
        ShaderOperatorImpl.Initialize();
    }

    [Input(Guid = "258c53e6-7708-49b7-88e2-1e40d2a4f88d")]
    public readonly InputSlot<string> Source = new();

    [Input(Guid = "9675eb2e-ae6a-4826-a53e-07bed7d5b8a0")]
    public readonly InputSlot<string> EntryPoint = new();

    [Input(Guid = "08789371-8193-49af-9ef4-97b12d9e6981")]
    public readonly InputSlot<string> DebugName = new();

    #region IShaderOperator implementation
    private IShaderOperator<GeometryShaderT3> ShaderOperatorImpl => this;
    InputSlot<string> IShaderOperator<GeometryShaderT3>.Path => Source;
    Slot<GeometryShaderT3> IShaderOperator<GeometryShaderT3>.ShaderSlot => Shader;
    InputSlot<string> IShaderOperator<GeometryShaderT3>.EntryPoint => EntryPoint;
    InputSlot<string> IShaderOperator<GeometryShaderT3>.DebugName => DebugName;
    string IShaderOperator<GeometryShaderT3>.CachedEntryPoint { get; set; } = default!; // set by Initialized()
    void IShaderOperator<GeometryShaderT3>.OnShaderUpdate(EvaluationContext context, GeometryShaderT3? shader)
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