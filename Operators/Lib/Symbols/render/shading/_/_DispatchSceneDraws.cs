using T3.Core.Rendering;
using T3.Core.Rendering.Material;

namespace Lib.render.shading._;

/// <summary>
/// Draws the nodes defined in the scene setup by dispatching a series of draw calls.
/// To improve performance we:
/// - set shaders once
/// - set shared constant buffers (transform, fog, etc.) only ones
/// - only update material related SRVs
/// - only clean-up once
///
/// LATER optimizations could include:
/// - Using point references for subcomponent manipulation of color and transform
/// - Using point references for instancing
/// - Sorting draw order to draw batches of matching materials, meshes etc points.  
/// 
/// </summary>
[Guid("6e32756e-4267-47f1-bad0-56ee8f58b070")]
internal sealed class _DispatchSceneDraws : Instance<_DispatchSceneDraws>
{
    [Output(Guid = "bda1583a-f5ea-4c3b-ae7d-0bac614d29ec")]
    public readonly Slot<Command> Output = new();

    public _DispatchSceneDraws()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var vsStage = deviceContext.VertexShader;
        var psStage = deviceContext.PixelShader;

        _useSceneMaterials = UseSceneMaterials.GetValue(context);
            
        // Keep current state
        _prevConstantBuffers = vsStage.GetConstantBuffers(0, ConstantBufferIndexCount);
        _prevShaderResourceViews = vsStage.GetShaderResources(0, _shaderResourceViews.Length);
        _prevSamplerStates = vsStage.GetSamplers(0, _samplerStates.Length);
        _prevVertexShader = vsStage.Get();
        _prevPixelShader = psStage.Get();
            
            

        // Shared resources shared by all draw calls
        var resourcesMissing = false;
        SamplerStates.GetValues(ref _samplerStates, context);
        var vs = VertexShader.GetValue(context);
        var ps = PixelShader.GetValue(context);

        resourcesMissing |= (vs == null || ps == null);
        resourcesMissing |= TryGetAndApplySrv(context, PrefilteredSpecular, PrefilteredSpecularIndex);
        resourcesMissing |= TryGetAndApplySrv(context, BrdfLookup, BrdfLookupIndex);

        resourcesMissing |= TryGetAndApplyBuffer(context, TransformBuffer, TransformBufferIndex);
        resourcesMissing |= TryGetAndApplyBuffer(context, FloatParameterBuffer, FloatParameterBufferIndex);
        resourcesMissing |= TryGetAndApplyBuffer(context, FogParameterBuffer, FogParameterBufferIndex);
        resourcesMissing |= TryGetAndApplyBuffer(context, PointLightBuffer, PointLightBufferIndex);

        // Draw...
        if (resourcesMissing)
        {
            Log.Warning("Resources missing", this);
        }
        else
        {
            vsStage.Set(vs);
            vsStage.SetSamplers(0, _samplerStates.Length, _samplerStates);
            psStage.Set(ps);
            psStage.SetSamplers(0, _samplerStates.Length, _samplerStates);

            TryDrawNodes(context, psStage, vsStage);
        }

        // Restore
        vsStage.Set(_prevVertexShader);
        vsStage.SetConstantBuffers(0, _prevConstantBuffers.Length, _prevConstantBuffers);
        vsStage.SetShaderResources(0, _prevShaderResourceViews.Length, _prevShaderResourceViews);

        psStage.Set(_prevPixelShader);
        psStage.SetConstantBuffers(0, _prevConstantBuffers.Length, _prevConstantBuffers);
        psStage.SetShaderResources(0, _prevShaderResourceViews.Length, _prevShaderResourceViews);
        psStage.SetSamplers(0, _prevSamplerStates.Length, _prevSamplerStates);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    private void TryDrawNodes(EvaluationContext context, SharpDX.Direct3D11.PixelShaderStage psStage, SharpDX.Direct3D11.VertexShaderStage vsStage)
    {
        var sceneSetup = SceneSetup.GetValue(context);
        if (sceneSetup?.Dispatches == null)
        {
            Log.Warning("undefined or empty scene setup");
            return;
        }

        PbrMaterial lastMaterial = null;
        foreach (var dispatch in sceneSetup.Dispatches)
        {
            // Resources changing with each draw call
            var resourcesMissing = false;
            var material = (dispatch.Material == null || !_useSceneMaterials) ? context.PbrMaterial : dispatch.Material;
            //var material =  context.PbrMaterial;
            if (material != lastMaterial)
            {
                _constantBuffers[PbrParameterBufferIndex] = material.ParameterBuffer;
                resourcesMissing |= TestAndSet(material?.NormalSrv, NormalMapIndex);
                resourcesMissing |= TestAndSet(material?.AlbedoMapSrv, AlbedoMapIndex);
                resourcesMissing |= TestAndSet(material?.EmissiveMapSrv, EmissiveMapIndex);
                resourcesMissing |= TestAndSet(material?.RoughnessMetallicOcclusionSrv, RmoColorMapIndex);
                lastMaterial = material;
            }
                
            // Compute local transform matrix
            var m = Matrix4x4.Multiply(dispatch.CombinedTransform,context.ObjectToWorld);
            var transformBufferContent = new TransformBufferLayout(context.CameraToClipSpace, context.WorldToCamera, m);
            //var transformBufferContent = new TransformBufferLayout(context.CameraToClipSpace, context.WorldToCamera, context.ObjectToWorld);
            ResourceManager.SetupConstBuffer(transformBufferContent, ref _constantBuffers[TransformBufferIndex]);
                
            psStage.SetConstantBuffers(0, ConstantBufferIndexCount, _constantBuffers);
            vsStage.SetConstantBuffers(0, ConstantBufferIndexCount, _constantBuffers);
                
            resourcesMissing |= TestAndSet(dispatch.MeshBuffers?.VertexBuffer?.Srv, MeshVerticesIndex);
            resourcesMissing |= TestAndSet(dispatch.MeshBuffers?.IndicesBuffer?.Srv, MeshIndicesIndex);

            if (resourcesMissing)
            {
                Log.Debug($"Skipping draw call for {dispatch.Material.Name} because we're missing some resources",this);
                continue;
            }

            vsStage.SetShaderResources(0, _shaderResourceViews.Length, _shaderResourceViews);
            psStage.SetShaderResources(0, _shaderResourceViews.Length, _shaderResourceViews);

            // Log.Debug($"Dispatching draw for {dispatch.VertexCount} vertices...");
            ResourceManager.Device.ImmediateContext.Draw(dispatch.VertexCount, dispatch.VertexStartIndex);
        }
    }

    private  bool TryGetAndApplySrv(EvaluationContext context, InputSlot<ShaderResourceView> srvInput, int index)
    {
        var srv = srvInput.GetValue(context);
        _shaderResourceViews[index] = srv;
        return srv == null;
    }

    private  bool TryGetAndApplyBuffer(EvaluationContext context, InputSlot<Buffer> bufferInput, int index)
    {
        var buffer = bufferInput.GetValue(context);
        _constantBuffers[index] = buffer;
        return buffer == null;
    }

    private  bool TestAndSet(ShaderResourceView srv, int index)
    {
        _shaderResourceViews[index] = srv;
        return srv == null;
    }

    private const int MeshVerticesIndex = 0;
    private const int MeshIndicesIndex = 1;
    private const int AlbedoMapIndex = 2;
    private const int EmissiveMapIndex = 3;
    private const int RmoColorMapIndex = 4;
    private const int NormalMapIndex = 5;
    private const int PrefilteredSpecularIndex = 6;
    private const int BrdfLookupIndex = 7;
    private const int SrvIndexCount = 8;

    private const int TransformBufferIndex = 0;
    private const int FloatParameterBufferIndex = 1;
    private const int FogParameterBufferIndex = 2;
    private const int PointLightBufferIndex = 3;
    private const int PbrParameterBufferIndex = 4; // Coming from PbrMaterial
    private const int ConstantBufferIndexCount = 5; 

    private readonly ShaderResourceView[] _shaderResourceViews = new ShaderResourceView[SrvIndexCount];
    private readonly Buffer[] _constantBuffers = new Buffer[ConstantBufferIndexCount];
    private SamplerState[] _samplerStates = Array.Empty<SamplerState>();
    private SharpDX.Direct3D11.PixelShader _prevPixelShader;
    private SharpDX.Direct3D11.VertexShader _prevVertexShader;
    private SamplerState[] _prevSamplerStates = Array.Empty<SamplerState>();
    private Buffer[] _prevConstantBuffers;
    private ShaderResourceView[] _prevShaderResourceViews;
        
    private bool _useSceneMaterials;

    [Input(Guid = "DAD22148-B87F-439A-9219-785BEE63991C")]
    public readonly InputSlot<SceneSetup> SceneSetup = new();

    [Input(Guid = "7a9ae929-7001-42ef-b7f2-f2e03bbb7206")]
    public readonly InputSlot<T3.Core.DataTypes.VertexShader> VertexShader = new();

    [Input(Guid = "59864DA4-3658-4D7D-830E-6EF0D3CBB505")]
    public readonly InputSlot<T3.Core.DataTypes.PixelShader> PixelShader = new();

    [Input(Guid = "27405ECF-4994-4E67-8604-D86426345344")]
    public readonly InputSlot<ShaderResourceView> PrefilteredSpecular = new();

    [Input(Guid = "3C19533B-C59A-470B-8712-073E71813C0C")]
    public readonly InputSlot<ShaderResourceView> BrdfLookup = new();

    [Input(Guid = "60bae25c-64fe-40df-a2e6-a99297a92e0b")]
    public readonly MultiInputSlot<SamplerState> SamplerStates = new();

    [Input(Guid = "AF6C8349-F48E-4C5A-87FC-33421E255117")]
    public readonly InputSlot<Buffer> TransformBuffer = new();

    [Input(Guid = "883AEABC-19C8-4754-BB3B-9152B491FFA7")]
    public readonly InputSlot<Buffer> FloatParameterBuffer = new();

    [Input(Guid = "8DC3E6E7-0EEF-4082-9DC8-4379A901E56B")]
    public readonly InputSlot<Buffer> FogParameterBuffer = new();

    [Input(Guid = "DB81BA63-8B7B-425C-9D6A-8F3CAF0BE70F")]
    public readonly InputSlot<Buffer> PointLightBuffer = new();
        
    [Input(Guid = "46AEE96F-A74E-491C-AE02-843EF62124F7")]
    public readonly InputSlot<bool> UseSceneMaterials = new();

}