using T3.Core.Utils;

namespace Types.Gfx;

[Guid("8bef116d-7d1c-4c1b-b902-25c1d5e925a9")]
public sealed class ComputeShaderStage : Instance<ComputeShaderStage>, IRenderStatsProvider
{
    [Output(Guid = "{C382284F-7E37-4EB0-B284-BC735247F26B}")]
    public readonly Slot<Command> Output = new();

    public ComputeShaderStage()
    {
        Output.UpdateAction += Update;
        if (!_statsRegistered)
        {
            RenderStatsCollector.RegisterProvider(this);
            _statsRegistered = true;
        }
    }

    private void Update(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;

        _cs = ComputeShader.GetValue(context);
            
        Int3 dispatchCount = Dispatch.GetValue(context);
        int count = DispatchCallCount.GetValue(context).Clamp(1, 100);

        GetAdditionalResources(context);
        ConstantBuffers.GetValues(ref _constantBuffers, context);
        ShaderResources.GetValues(ref _shaderResourceViews, context);
        SamplerStates.GetValues(ref _samplerStates, context);
        Uavs.GetValues(ref _uavs, context);
        int counter = UavBufferCounter.GetValue(context);

        if (_uavs.Length == 0 || _cs == null)
            return;

        
        _prevRenderTargetViews = device.ImmediateContext.OutputMerger.GetRenderTargets(2);
        device.ImmediateContext.OutputMerger.GetRenderTargets(out _prevDepthStencilView);
        
        csStage.Set(_cs);
        csStage.SetConstantBuffers(0, _constantBuffers.Length, _constantBuffers);
        csStage.SetShaderResources(0, _shaderResourceViews.Length, _shaderResourceViews);
        csStage.SetShaderResources(_shaderResourceViews.Length, _additionalSrvs.Length, _additionalSrvs);
            
        csStage.SetSamplers(0, _samplerStates);
        if (_uavs.Length == 4)
        {
            csStage.SetUnorderedAccessViews(0, _uavs, [-1, 0, -1, -1]);
        }
        else if (_uavs.Length == 1)
        {
            if (counter == -1)
                csStage.SetUnorderedAccessView(0, _uavs[0]);
            else
                csStage.SetUnorderedAccessViews(0, _uavs, [counter]);
        }
        else if (_uavs.Length == 2)
        {
            csStage.SetUnorderedAccessViews(0, _uavs, [0, 0]);
        }
        else if (_uavs.Length == 3)
        {
            csStage.SetUnorderedAccessViews(0, _uavs, [counter, -1, -1]);
        }
        else
        {
            csStage.SetUnorderedAccessViews(0, _uavs);
        }

        // Dispatch the shader
        for (int i = 0; i < count; i++)
        {
            deviceContext.Dispatch(dispatchCount.X, dispatchCount.Y, dispatchCount.Z);
        }
        
        if (_prevRenderTargetViews.Length > 0)
            deviceContext.OutputMerger.SetRenderTargets(_prevDepthStencilView, _prevRenderTargetViews);

        foreach (var rtv in _prevRenderTargetViews)
        {
            if (rtv == null || rtv.IsDisposed)
                continue;
            
            rtv.Dispose();            
        }
            
        Utilities.Dispose(ref _prevDepthStencilView);

        
        // unbind resources
        for (int i = 0; i < _uavs.Length; i++)
        {
            csStage.SetUnorderedAccessView(i, null);
        }
        
        for (int i = 0; i < _samplerStates.Length; i++)
        {
            csStage.SetSampler(i, null);
        }
        
        for (int i = 0; i < _shaderResourceViews.Length; i++)
        {
            csStage.SetShaderResource(i, null);
        }
        
        for (int i = 0; i < _constantBuffers.Length; i++)
        {
            csStage.SetConstantBuffer(i, null);
        }
            
            
        _statsUpdateCount++;
        _statsDispatchCount += dispatchCount.X * dispatchCount.Y * dispatchCount.Z;
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
    

    private SharpDX.Direct3D11.ComputeShader? _cs;
    private Buffer[] _constantBuffers = [];
    private ShaderResourceView[] _shaderResourceViews = [];
    private ShaderResourceView[] _additionalSrvs = [];
    private SharpDX.Direct3D11.SamplerState[] _samplerStates = [];
    private UnorderedAccessView[] _uavs = [];
        
        
    public IEnumerable<(string, int)> GetStats()
    {
        yield return ("Compute shaders", _statsUpdateCount);
        yield return ("Dispatches", _statsDispatchCount);
    }

    public void StartNewFrame()
    {
        _statsUpdateCount = 0;
        _statsDispatchCount = 0;
    }
        
    private static int _statsUpdateCount;
    private static int _statsDispatchCount;
    private static bool _statsRegistered;        

    [Input(Guid = "5c0e9c96-9aba-4757-ae1f-cc50fb6173f1")]
    public readonly InputSlot<T3.Core.DataTypes.ComputeShader> ComputeShader = new();

    [Input(Guid = "180cae35-10e3-47f3-8191-f6ecea7d321c")]
    public readonly InputSlot<Int3> Dispatch = new();

    [Input(Guid = "1495157D-601F-4054-84E2-29EBEBB461D8")]
    public readonly InputSlot<int> DispatchCallCount = new();
    
    [Input(Guid = "34cf06fe-8f63-4f14-9c59-35a2c021b817")]
    public readonly MultiInputSlot<Buffer> ConstantBuffers = new();

    [Input(Guid = "88938b09-d5a7-437c-b6e1-48a5b375d756")]
    public readonly MultiInputSlot<ShaderResourceView> ShaderResources = new();

    [Input(Guid = "2E33837E-54C0-4519-8EDA-04EEE80690A5")]
    public readonly MultiInputSlot<Object> VariousResources = new();
    
    [Input(Guid = "599384c2-bf6c-4953-be74-d363292ab1c7")]
    public readonly MultiInputSlot<UnorderedAccessView> Uavs = new();

    [Input(Guid = "0105aca4-5fd5-40c8-82a5-e919bb7dd507")]
    public readonly InputSlot<int> UavBufferCounter = new();
        
    [Input(Guid = "4047c9e7-1edb-4c71-b85c-c1b87058c81c")]
    public readonly MultiInputSlot<SharpDX.Direct3D11.SamplerState> SamplerStates = new();

    private RenderTargetView?[]? _prevRenderTargetViews;
    private DepthStencilView? _prevDepthStencilView;
}