using T3.Core.Stats;
using PixelShaderT3 = T3.Core.DataTypes.PixelShader;

namespace Types.Gfx;

[Guid("f7c625da-fede-4993-976c-e259e0ee4985")]
public sealed class PixelShader : Instance<PixelShader>, IDescriptiveFilename, IStatusProvider, IShaderOperator<PixelShaderT3>
{
    [Output(Guid = "9C6E72F8-5CE6-42C3-ABAA-1829D2C066C1")]
    public readonly Slot<PixelShaderT3> Shader = new();
        
    [Output(Guid = "5D24B1D4-79E4-4AF9-BBC3-78F9ACE1BE98")]
    public readonly Slot<string> Warning = new();

    public PixelShader()
    {
        ShaderOperatorImpl.Initialize();
    }
    public void OnShaderUpdate(EvaluationContext context, PixelShaderT3? shader)
    {
    }

    public InputSlot<string> SourcePathSlot => Source;

    [Input(Guid = "24646F06-1509-43CE-94C6-EEB608AD97CD")]
    public readonly InputSlot<string> Source = new();

    [Input(Guid = "501338B3-F432-49A5-BDBD-BCF209671305")]
    public readonly InputSlot<string> EntryPoint  = new();

    [Input(Guid = "BE9B3DC1-7122-4B3D-B936-CCCF2581B69E")]
    public readonly InputSlot<string> DebugName = new();


    #region IShaderOperator implementation
    private IShaderOperator<PixelShaderT3> ShaderOperatorImpl => this;
    InputSlot<string> IShaderOperator<PixelShaderT3>.Path => Source;
    InputSlot<string> IShaderOperator<PixelShaderT3>.EntryPoint => EntryPoint;
    InputSlot<string> IShaderOperator<PixelShaderT3>.DebugName => DebugName;
    Slot<PixelShaderT3> IShaderOperator<PixelShaderT3>.ShaderSlot => Shader;
    string IShaderOperator<PixelShaderT3>.CachedEntryPoint { get; set; } = default!; // set by Initialize()
    #endregion
        
    #region IStatusProvider implementation
    private readonly DefaultShaderStatusProvider _statusProviderImplementation = new ();
    public void SetWarning(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            this.ClearErrorState();
        }
        else
        {
            this.LogErrorState(message);
        }
        _statusProviderImplementation.Warning = message;
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _statusProviderImplementation.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _statusProviderImplementation.GetStatusMessage();
    #endregion
}