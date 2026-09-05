using VertexShaderD3D = T3.Core.DataTypes.VertexShader;

namespace Types.Gfx;


[Guid("18e09bad-5b18-439f-b5fc-ef61ce54b190")]
internal sealed class VertexShaderFromSource : 
    Instance<VertexShaderFromSource>
  , 
     IShaderCodeOperator<VertexShaderD3D>, 
     IStatusProvider
{
    [Output(Guid = "C513F15D-3A7E-4501-B825-EF3E585293C7")]
    public readonly Slot<VertexShaderD3D> VertexShader = new();

    public VertexShaderFromSource()
    {
        ShaderOperatorImpl.Initialize();
    }

    [Input(Guid = "a192e8cc-2874-4f02-bbf1-4622e99666e1")]
    public readonly InputSlot<string> ShaderSource = new();

    [Input(Guid = "2b616fb0-2966-45a9-a0cc-da960ca509cf")]
    public readonly InputSlot<string> EntryPoint = new();

    [Input(Guid = "baa49d7d-127c-4c93-ae90-7e4db3598af9")]
    public readonly InputSlot<string> DebugName = new();


    #region IShaderOperator implementation
    private IShaderCodeOperator<VertexShaderD3D> ShaderOperatorImpl => this;
    InputSlot<string> IShaderCodeOperator<VertexShaderD3D>.Code => ShaderSource;
    InputSlot<string> IShaderCodeOperator<VertexShaderD3D>.EntryPoint => EntryPoint;
    InputSlot<string> IShaderCodeOperator<VertexShaderD3D>.DebugName => DebugName;
    Slot<VertexShaderD3D> IShaderCodeOperator<VertexShaderD3D>.ShaderSlot => VertexShader;
    #endregion
    
    #region IStatusProvider implementation
    private readonly DefaultShaderStatusProvider _statusProviderImplementation = new ();
    public void SetWarning(string message) => _statusProviderImplementation.Warning = message;
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _statusProviderImplementation.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _statusProviderImplementation.GetStatusMessage();
    #endregion    
}