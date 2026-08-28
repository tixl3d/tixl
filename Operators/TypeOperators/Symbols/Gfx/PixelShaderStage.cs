namespace Types.Gfx;

[Guid("75306997-4329-44e9-a17a-050dae532182")]
public sealed class PixelShaderStage : Instance<PixelShaderStage>
{
    [Output(Guid = "76E7AD5D-A31D-4B1F-9C42-B63C5161117C")]
    public readonly Slot<Command> Output = new(new Command());

    public PixelShaderStage()
    {
        Output.UpdateAction += Update;
        Output.Value!.RestoreAction = Restore;
    }

    private void Update(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var psStage = deviceContext.PixelShader;

        var ps = PixelShader.GetValue(context);
            
        ConstantBuffers.GetValues(ref _constantBuffers, context);
        ShaderResources.GetValues(ref _shaderResourceViews, context);
        SamplerStates.GetValues(ref _samplerStates, context);

        _prevPixelShader = psStage.Get();
        _prevConstantBuffers = psStage.GetConstantBuffers(0, _constantBuffers.Length);
        _prevShaderResourceViews = psStage.GetShaderResources(0, _shaderResourceViews.Length);
        _prevSamplerStates = psStage.GetSamplers(0, _samplerStates.Length);

        if (ps == null)
            return;

        psStage.Set(ps);
        psStage.SetConstantBuffers(0, _constantBuffers.Length, _constantBuffers);
        psStage.SetShaderResources(0, _shaderResourceViews.Length, _shaderResourceViews);
        psStage.SetSamplers(0, _samplerStates.Length, _samplerStates);
    }

    private void Restore(EvaluationContext context)
    {
        var deviceContext = ResourceManager.Device.ImmediateContext;
        var psStage = deviceContext.PixelShader;

        psStage.Set(_prevPixelShader);
        if (_prevConstantBuffers != null)
            psStage.SetConstantBuffers(0, _prevConstantBuffers.Length, _prevConstantBuffers);
        
        if (_prevShaderResourceViews != null)
            psStage.SetShaderResources(0, _prevShaderResourceViews.Length, _prevShaderResourceViews);
        psStage.SetSamplers(0, _prevSamplerStates.Length, _prevSamplerStates);
    }

    private Buffer[] _constantBuffers = new Buffer[0];
    private ShaderResourceView[] _shaderResourceViews = new ShaderResourceView[0];
    private SharpDX.Direct3D11.SamplerState[] _samplerStates = new SharpDX.Direct3D11.SamplerState[0];

    private SharpDX.Direct3D11.PixelShader? _prevPixelShader;
    private Buffer[]? _prevConstantBuffers;
    private ShaderResourceView[]? _prevShaderResourceViews;
    private SharpDX.Direct3D11.SamplerState[] _prevSamplerStates = new SharpDX.Direct3D11.SamplerState[0];

    [Input(Guid = "C4E91BC6-1691-4EB4-AED5-DD4CAE528149")]
    public readonly MultiInputSlot<SharpDX.Direct3D11.SamplerState> SamplerStates = new();

    [Input(Guid = "BE02A84B-A666-4119-BB6E-FEE1A3DF0981")]
    public readonly MultiInputSlot<Buffer> ConstantBuffers = new();

    [Input(Guid = "1B9BE6EB-96C8-4B1C-B854-99B64EAF5618")]
    public readonly InputSlot<T3.Core.DataTypes.PixelShader> PixelShader = new();

    [Input(Guid = "50052906-4691-4A84-A69D-A109044B5300")]
    public readonly MultiInputSlot<ShaderResourceView> ShaderResources = new();
}