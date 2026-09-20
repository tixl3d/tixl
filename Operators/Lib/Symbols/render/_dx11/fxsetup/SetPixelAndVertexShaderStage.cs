namespace Lib.render._dx11.fxsetup;

[Guid("b956f707-2a33-4330-b7ff-9c91edbcf041")]
internal sealed class SetPixelAndVertexShaderStage : Instance<SetPixelAndVertexShaderStage>
{
    [Output(Guid = "805e271d-b9c5-45a2-9040-f30c68b06ea6")]
    public readonly Slot<Command> Output = new(new Command());

    public SetPixelAndVertexShaderStage()
    {
        Output.UpdateAction += Update;
        Output.Value.RestoreAction = Restore;
    }

    private void Update(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var vsStage = deviceContext.VertexShader;
        var psStage = deviceContext.PixelShader;

        
        ConstantBuffers.GetValues(ref _constantBuffers, context);
        ShaderResources.GetValues(ref _shaderResourceViews, context);
        SamplerStates.GetValues(ref _samplerStates, context);

        // Both stages are saved, and the pop puts each back where it was. The readback this replaces took
        // the previous state from the vertex stage alone and applied it to both, left the appended resource
        // views bound, and allocated three arrays a frame.
        deviceContext.PushState(StateGroups.VertexShader | StateGroups.PixelShader);

        // First update Shaders -> GenerateShaderCode -> ShaderGraphNodes ...
        var vs = VertexShader.GetValue(context);
        var ps = PixelShader.GetValue(context);
        
        // ... then add updated resources. 
        GetAdditionalResources(context);
        
        if (vs != null)
        {
            vsStage.Set(vs);
            vsStage.SetSamplers(0, _samplerStates.Length, _samplerStates);
            vsStage.SetConstantBuffers(0, _constantBuffers.Length, _constantBuffers);
            vsStage.SetShaderResources(0, _shaderResourceViews.Length, _shaderResourceViews);
            vsStage.SetShaderResources(_shaderResourceViews.Length, _additionalSrvs.Length, _additionalSrvs);
        }

        if (ps != null)
        {
            psStage.Set(ps);
            psStage.SetSamplers(0, _samplerStates.Length, _samplerStates);
            psStage.SetConstantBuffers(0, _constantBuffers.Length, _constantBuffers);
            psStage.SetShaderResources(0, _shaderResourceViews.Length, _shaderResourceViews);
            psStage.SetShaderResources(_shaderResourceViews.Length, _additionalSrvs.Length, _additionalSrvs);
        }
        
    }

    private void GetAdditionalResources(EvaluationContext context)
    {
        if (!VariousResources.DirtyFlag.IsDirty)
            return;
        
        var collectedTypedInputs = VariousResources.GetCollectedTypedInputs();
        
        foreach (var t in collectedTypedInputs)
        {
            switch (t.GetValue(context))
            {
                case List<ShaderResourceView> srvs:
                {
                    if (srvs.Count != _additionalSrvs.Length)
                        _additionalSrvs = new ShaderResourceView[srvs.Count];

                    for (var srvIndex = 0; srvIndex < srvs.Count; srvIndex++)
                    {
                        var srv = srvs[srvIndex];
                        _additionalSrvs[srvIndex] = srv;
                    }
                    break;
                }
            }
        }
        VariousResources.DirtyFlag.Clear();
    }

    private void Restore(EvaluationContext context)
    {
        ResourceManager.Device.ImmediateContext.PopState();
    }

    private Buffer[] _constantBuffers = [];
    private ShaderResourceView[] _shaderResourceViews = [];
    private ShaderResourceView[] _additionalSrvs = [];
    private SamplerState[] _samplerStates = [];


    [Input(Guid = "7a9ae929-7001-42ef-b7f2-f2e03bbb7206")]
    public readonly InputSlot<T3.Core.DataTypes.VertexShader> VertexShader = new();

    [Input(Guid = "59864DA4-3658-4D7D-830E-6EF0D3CBB505")]
    public readonly InputSlot<T3.Core.DataTypes.PixelShader> PixelShader = new();

    [Input(Guid = "9571b16e-72d1-4544-aa98-8a08b63bb5f6")]
    public readonly MultiInputSlot<Buffer> ConstantBuffers = new();

    [Input(Guid = "83fdb275-3018-46a9-b75e-e9ee3d8660f4")]
    public readonly MultiInputSlot<ShaderResourceView> ShaderResources = new();

    [Input(Guid = "CC866663-5BFA-4A17-9EFC-E2F381767317")]
    public readonly MultiInputSlot<Object> VariousResources = new();

    [Input(Guid = "60bae25c-64fe-40df-a2e6-a99297a92e0b")]
    public readonly MultiInputSlot<SamplerState> SamplerStates = new();
}