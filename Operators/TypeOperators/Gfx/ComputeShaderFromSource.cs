using ComputeShaderT3 = T3.Core.DataTypes.ComputeShader;

namespace Types.Gfx;

[Guid("4e5bc624-9cda-46a8-9681-7fd412ea3893")]
public sealed class ComputeShaderFromSource : Instance<ComputeShaderFromSource>, IStatusProvider, IShaderCodeOperator<ComputeShaderT3>
{
    [Output(Guid = "190e262f-6554-4b34-b5b6-6617a98ab123")]
    public readonly Slot<ComputeShaderT3> Shader = new();

    [Output(Guid = "a3e0a72f-68d0-4278-8b9b-f4cf33603305")]
    public readonly Slot<Int3> ThreadCount = new();

    public ComputeShaderFromSource()
    {
        ShaderOperatorImpl.Initialize();
        Shader.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var shader = Shader.Value;
        if (shader == null)
            return;
            
        if (shader.TryGetThreadGroups(out var threadCount))
            ThreadCount.Value = threadCount;

        ThreadCount.DirtyFlag.Clear();
    }

    [Input(Guid = "a8ee59c3-cb62-42e5-a3c9-f4968876c9cc")]
    public readonly InputSlot<string> Code = new();

    [Input(Guid = "d1cbd9eb-5e5a-499d-b7af-0cfe283f896b")]
    public readonly InputSlot<string> EntryPoint = new();

    [Input(Guid = "08399b7a-a390-4a11-83eb-36ac68f76bc6")]
    public readonly InputSlot<string> DebugName = new();

    #region IShaderCodeOperator implementation
    private IShaderCodeOperator<ComputeShaderT3> ShaderOperatorImpl => this;
    InputSlot<string> IShaderCodeOperator<ComputeShaderT3>.Code => Code;
    InputSlot<string> IShaderCodeOperator<ComputeShaderT3>.EntryPoint => EntryPoint;
    InputSlot<string> IShaderCodeOperator<ComputeShaderT3>.DebugName => DebugName;
    Slot<ComputeShaderT3> IShaderCodeOperator<ComputeShaderT3>.ShaderSlot => Shader;
    #endregion

       

    #region IStatusProvider implementation
    private readonly DefaultShaderStatusProvider _statusProviderImplementation = new ();
    public void SetWarning(string message) => _statusProviderImplementation.Warning = message;
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => _statusProviderImplementation.GetStatusLevel();
    string IStatusProvider.GetStatusMessage() => _statusProviderImplementation.GetStatusMessage();
    #endregion

}