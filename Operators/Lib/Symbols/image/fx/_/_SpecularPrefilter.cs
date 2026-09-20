#nullable enable
using T3.Graphics.Compat;
using T3.Core.Rendering;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;
// ReSharper disable MemberCanBePrivate.Global

namespace Lib.image.fx.@_;

[Guid("cc3cc712-9e87-49c6-b04b-49a12cf2ba75")]
internal sealed class _SpecularPrefilter : Instance<_SpecularPrefilter>
{
    [Output(Guid = "5dab6e1b-6136-45a9-bd63-1e18eafc20b7")]
    public readonly Slot<Texture2D?> FilteredCubeMap = new();

    public _SpecularPrefilter()
    {
        FilteredCubeMap.UpdateAction += Update;
    }

    private bool _updatedOnce = false;
        
    private void Update(EvaluationContext context)
    {
        var qualityFactor = QualityFactor.GetValue(context);
        var exposure = Exposure.GetValue(context);
        
        ShaderResources.GetValues(ref _shaderResourceViews, context);
        SamplerStates.GetValues(ref _samplerStates, context);
            
        var vs = VertexShader.GetValue(context);
        var gs = GeometryShader.GetValue(context);

        if (CubeMap.HasInputConnections && CubeMap.DirtyFlag.IsDirty)
        {
            //Log.Debug("Dirty", this);
        }

        var cubeMapSrc = CubeMap.GetValue(context); // Needs to be checked for null!
        if (cubeMapSrc == null)
        {
            FilteredCubeMap.Value = null;
            return;
        }

        if ((cubeMapSrc.Description.OptionFlags & ResourceOptionFlags.TextureCube) == 0)
        {
            Log.Warning("[SetEnvironment] requires a CubeMap. Please use [TextureToCube] to convert your texture", this);
            return;
        }
            
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
            
        // Vertex shader stage
        var vsStage = deviceContext.VertexShader;
        var ps = PixelShader.GetValue(context);

        var updateLive = UpdateLive.GetValue(context);
        if (_updatedOnce && !updateLive)
        {
            FilteredCubeMap.Value = _prefilteredCubeMap;
            return;
        }
        
        // Saved in one go; the facade tracks the state, so there is nothing to read back.
        deviceContext.PushState(StateGroups.All);

        if (vs == null)
        {
            Log.Warning( $"{nameof(_SpecularPrefilter)} requires valid vertex shader", SymbolChildId );
            return;
        }
        
        vsStage.Set(vs);
        vsStage.SetShaderResources(0, _shaderResourceViews.Length, _shaderResourceViews);

        // Geometry shader stage
        var gsStage = deviceContext.GeometryShader;


        if (gs == null)
        {
            Log.Warning( $"{nameof(_SpecularPrefilter)} requires valid geometry shader", SymbolChildId );
            return;
        }
            
        gsStage.Set(gs);
        gsStage.SetShaderResources(0, _shaderResourceViews.Length, _shaderResourceViews);

        // Pixel shader stage
        var psStage = deviceContext.PixelShader;


        if (ps == null)
        {
            Log.Warning( $"{nameof(_SpecularPrefilter)} requires valid pixel shader", SymbolChildId );
            return;
        }
        psStage.Set(ps);
        psStage.SetShaderResources(0, _shaderResourceViews.Length, _shaderResourceViews);
        psStage.SetSamplers(0, _samplerStates);
            
        
        var cubeMapSize = new Vector2(cubeMapSrc.Description.Width, cubeMapSrc.Description.Height);

        var cubeMapDesc = new Texture2DDescription
                              {
                                  BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                                  Format = cubeMapSrc.Description.Format,
                                  Width = (int)cubeMapSize.X,
                                  Height = (int)cubeMapSize.Y,
                                  MipLevels = cubeMapSrc.Description.MipLevels,
                                  SampleDescription = cubeMapSrc.Description.SampleDescription,
                                  Usage = ResourceUsage.Default,
                                  OptionFlags = ResourceOptionFlags.TextureCube | ResourceOptionFlags.GenerateMipMaps,
                                  CpuAccessFlags = CpuAccessFlags.None,
                                  ArraySize = 6
                              };

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _prefilteredCubeMap);
        try
        {
            _prefilteredCubeMap = Texture2D.CreateTexture2D(cubeMapDesc);
        }
        catch (Exception e)
        {
            Log.Debug($"can't create CubeMap target {e.Message}", this);
            return;
        }

        var rastDesc = new RasterizerStateDescription
                           {
                               FillMode = FillMode.Solid,
                               CullMode = CullMode.None,
                               IsDepthClipEnabled = false
                           };
        _rasterizerState = new RasterizerState(device, rastDesc);

        // Input Assembler
        //var previousTopology = device.ImmediateContext.InputAssembler.PrimitiveTopology;
        device.ImmediateContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

        //_prevBlendState = device.ImmediateContext.OutputMerger.GetBlendState(out _prevBlendFactor, out _prevSampleMask);
        device.ImmediateContext.OutputMerger.BlendState = DefaultRenderingStates.DisabledBlendState;
        device.ImmediateContext.OutputMerger.DepthStencilState = DefaultRenderingStates.DisabledDepthStencilState;
            
                
        var rtvDesc = new RenderTargetViewDescription()
                          {
                              Dimension = RenderTargetViewDimension.Texture2DArray,
                              Format = cubeMapSrc.Description.Format,
                              Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource()
                                                   {
                                                       ArraySize = 6,
                                                       FirstArraySlice = 0,
                                                       MipSlice = 0
                                                   }
                          };

        var size = _prefilteredCubeMap.Description.Width;
            
            
        device.ImmediateContext.Rasterizer.State = _rasterizerState;

        var numMipLevels = _prefilteredCubeMap.Description.MipLevels;
        var mipSlice = 0;

        var samplingParameters = size switch
                                     {
                                         <= 128 => _samplingParameters128,
                                         <= 256 => _samplingParameters256,
                                         <= 512 => _samplingParameters512,
                                         _      => _samplingParameters1024,
                                     };

        while (mipSlice < numMipLevels)
        {
            // Log.Debug($"Update mipmap level {mipSlice} size: {size}", this);
            var viewport = new Viewport { X = 0, Y = 0, Width = size, Height = size , MinDepth = 0, MaxDepth = 1};
            device.ImmediateContext.Rasterizer.SetViewports([viewport]);
                
                
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMapRtv);
            rtvDesc.Texture2DArray.MipSlice = mipSlice;
            _cubeMapRtv = new RenderTargetView(device, _prefilteredCubeMap, rtvDesc);
            device.ImmediateContext.OutputMerger.SetTargets(_cubeMapRtv, null);

            var roughness = (float)mipSlice / (_prefilteredCubeMap.Description.MipLevels - 1);
                
            // Is this required?
            if (_settingsBuffer != null)
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _settingsBuffer);

            for (var i = 0; i < samplingParameters.Length; ++i)
            {
                var indexToUse = -1;
                if (Math.Abs(roughness - samplingParameters[i].roughness) < 0.01f)
                {
                    indexToUse = i;
                }

                if (indexToUse == -1 && roughness < samplingParameters[i].roughness)
                {
                    indexToUse = i - 1;
                }

                if (indexToUse != -1)
                {
                    var parameterData = samplingParameters[indexToUse];
                    parameterData.roughness = roughness;
                    parameterData.exposure = exposure;
                    parameterData.numSamples = (int)(parameterData.numSamples * qualityFactor).Clamp(1,1000);
                    ResourceManager.SetupConstBuffer(parameterData, ref _settingsBuffer);
                    break;
                }
            }

            var constantBuffers = new[] { _settingsBuffer };
            psStage.SetConstantBuffers(0, 1, constantBuffers);
            vsStage.SetConstantBuffers(0, 1, constantBuffers);
            gsStage.SetConstantBuffers(0, 1, constantBuffers);

            device.ImmediateContext.Draw(3, 0);
            size /= 2;
            ++mipSlice;
        }

        FilteredCubeMap.Value = _prefilteredCubeMap;
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMapRtv);

        //device.ImmediateContext.InputAssembler.PrimitiveTopology = previousTopology;
        Restore();
        _updatedOnce = true;
    }
        
        
    private void Restore()
    {
        ResourceManager.Device.ImmediateContext.PopState();
    }        

        
    [StructLayout(LayoutKind.Explicit, Size = Stride)]
    public struct SamplingParameter
    {
        [FieldOffset(0)]
        public float roughness;
            
        [FieldOffset(1 * 4)]
        public int baseMip;
            
        [FieldOffset(2 * 4)]
        public int numSamples;

        [FieldOffset(3 * 4)]
        public float exposure;

        private const int Stride = 4 * 4;
    }

    private const int f = 1;
        
    
        
    // 128
    private readonly SamplingParameter[] _samplingParameters128 =
        [
            new() { roughness = 0, baseMip = 0, numSamples = 1 },        // 128    
            new() { roughness = 0.1f, baseMip = 1, numSamples = 40* f }, //  64
            new() { roughness = 0.2f, baseMip = 2, numSamples =  30* f }, // 32
            new() { roughness = 0.3f, baseMip = 2, numSamples =  30* f }, // 32
            new() { roughness = 0.6f, baseMip = 3, numSamples =  30* f }, // 16
            new() { roughness = 0.6f, baseMip = 3, numSamples =  30* f }, //  8
            new() { roughness = 1.0f, baseMip = 3, numSamples =   30* f } // 4   
        ];
        
    // 256
    private readonly SamplingParameter[] _samplingParameters256 =
        [
            new() { roughness = 0, baseMip = 0, numSamples = 1 },
            new() { roughness = 0.1f, baseMip = 1, numSamples = 150 }, // 500
            new() { roughness = 0.3f, baseMip = 2, numSamples = 50 }, // 500
            new() { roughness = 1.0f, baseMip = 3, numSamples = 20 } // 20
        ];
        
    // 512
    private readonly SamplingParameter[] _samplingParameters512 =
        [
            new() { roughness = 0, baseMip = 0, numSamples = 1 },
            new() { roughness = 0.1f, baseMip = 1, numSamples = 200 }, // 500
            new() { roughness = 0.3f, baseMip = 2, numSamples = 100 }, // 500
            new() { roughness = 0.6f, baseMip = 3, numSamples = 100 }, // 20
            new() { roughness = 0.7f, baseMip = 5, numSamples = 100 }, // 20
            new() { roughness = 1.0f, baseMip = 8, numSamples = 50 } // 20
        ];
        
    // 1024
    private SamplingParameter[] _samplingParameters1024 =
        [
            new() { roughness = 0, baseMip = 0, numSamples = 1 },
            new() { roughness = 0.15f, baseMip = 1, numSamples = 200 }, // 500
            new() { roughness = 0.205f, baseMip = 3, numSamples = 500 }, // 500
            new() { roughness = 0.405f, baseMip = 5, numSamples = 400 }, // 500
            new() { roughness = 0.6f, baseMip = 7, numSamples = 300 }, // 200
            new() { roughness = 0.8f, baseMip = 10, numSamples = 100 }, // 100
            new() { roughness = 1.0f, baseMip = 12, numSamples = 100 } // 20
        ];
        
    protected override void Dispose(bool disposing)
    {
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _prefilteredCubeMap);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _cubeMapRtv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _rasterizerState);
        base.Dispose(disposing);
    }

    private Texture2D? _prefilteredCubeMap;
        
        
    //private BlendState _prevBlendState;
    // private Vector4 _prevBlendFactor;
    // private int _prevSampleMask;
        
        
    private RenderTargetView? _cubeMapRtv;
    private RasterizerState? _rasterizerState;

    //private Buffer[] _constantBuffers = new Buffer[0];
    private ShaderResourceView[] _shaderResourceViews = [];
    private SamplerState[] _samplerStates = [];

    // VS

    // GS

    // PS

    private static Buffer? _settingsBuffer;
        
        
    [Input(Guid = "9f7926aa-ac69-4963-af1d-342ad06fc278")]
    public readonly InputSlot<Texture2D> CubeMap = new();
        

    [Input(Guid = "D7C5E69E-9DA0-44F1-BAF7-A9D2A91CA41C")]
    public readonly InputSlot<VertexShader> VertexShader = new();

    [Input(Guid = "2A217F9D-2F9F-418A-8568-F767905384D5")]
    public readonly InputSlot<GeometryShader> GeometryShader = new();

    [Input(Guid = "04D1B56F-8655-4D6C-9BDC-A84057A199D0")]
    public readonly InputSlot<PixelShader> PixelShader = new();


    [Input(Guid = "26459A4A-1BD8-4987-B41B-6C354CC48D47")]
    public readonly MultiInputSlot<ShaderResourceView> ShaderResources = new();

    [Input(Guid = "86D3EEE1-A4B2-4F23-9C5E-39830C90D0DA")]
    public readonly InputSlot<float> Exposure = new();
        
    [Input(Guid = "B994BFF4-D1AC-4A30-A6DC-DC7BBE05D15D")]
    public readonly MultiInputSlot<SamplerState> SamplerStates = new();

    [Input(Guid = "9D792412-D1F0-45F9-ABD6-4EAB79719924")]
    public readonly InputSlot<bool> UpdateLive = new();
        
    [Input(Guid = "663BE4F2-AE53-4A4F-A825-D4D8A30161AD")]
    public readonly InputSlot<float> QualityFactor = new();

}