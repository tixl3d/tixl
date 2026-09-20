namespace Types.Gfx;

[Guid("a9600440-4203-4315-bdb1-4dfd603b4515")]
public sealed class VertexShaderStage : Instance<VertexShaderStage>
{
    [Output(Guid = "65B394A9-06DC-4D9B-8819-15394EDE2997")]
    public readonly Slot<Command> Output = new(new Command());

    public VertexShaderStage()
    {
        Output.UpdateAction += Update;
        
        if(Output.Value != null)  
            Output.Value.RestoreAction = Restore;
    }

    private void Update(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var vsStage = deviceContext.VertexShader;

        // ConstantBuffers.GetValues(ref _constantBuffers, context, clearDirty:false);
        // ShaderResources.GetValues(ref _shaderResourceViews, context, clearDirty:false);
        // SamplerStates.GetValues(ref _samplerStates, context, clearDirty:false);

        ConstantBuffers.GetValues(ref _constantBuffers, context);
        ShaderResources.GetValues(ref _shaderResourceViews, context);
        SamplerStates.GetValues(ref _samplerStates, context);

        // Saved explicitly; the enclosing operator pops it.
        deviceContext.PushState(StateGroups.VertexShader);

        var vs = VertexShader.GetValue(context);
        if (vs == null)
            return;

        vsStage.Set(vs);
        vsStage.SetConstantBuffers(0, _constantBuffers.Length, _constantBuffers);
        vsStage.SetShaderResources(0, _shaderResourceViews.Length, _shaderResourceViews);
    }

    private void Restore(EvaluationContext context)
    {
        ResourceManager.Device.ImmediateContext.PopState();
    }

    private Buffer[] _constantBuffers = new Buffer[0];
    private ShaderResourceView[] _shaderResourceViews = new ShaderResourceView[0];
    private T3.Graphics.Compat.SamplerState[] _samplerStates = new T3.Graphics.Compat.SamplerState[0];


    [Input(Guid = "B1C236E5-6757-4D77-9911-E3ACD5EA9FE9")]
    public readonly InputSlot<T3.Core.DataTypes.VertexShader> VertexShader = new();

    [Input(Guid = "BBA8F6EB-7CFF-435B-AB47-FEBF58DD8FBA")]
    public readonly MultiInputSlot<Buffer> ConstantBuffers = new();

    [Input(Guid = "3A0BEA89-BD93-4594-B1B6-3E25689C67E6")]
    public readonly MultiInputSlot<ShaderResourceView> ShaderResources = new();

    [Input(Guid = "2BC7584D-A347-4954-9120-C1841AF76650")]
    public readonly MultiInputSlot<T3.Graphics.Compat.SamplerState> SamplerStates = new();
}