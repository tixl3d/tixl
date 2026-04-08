#if PLATFORM_WINDOWS
using System;
using SharpDX;
using SharpDX.Direct3D11;
using T3.Core.Resource;

namespace T3.Core.Rendering;

/// <summary>
/// A helper class to generate and provide reusable default states for various DX rendering states
/// </summary>
public static class DefaultRenderingStates
{
    private static DepthStencilState _defaultDepthStencilState;

    public static DepthStencilState DefaultDepthStencilState
    {
        get
        {
            if (_defaultDepthStencilState == null && ResourceManager.Device != null)
            {
                var depthStencilDescription = new DepthStencilStateDescription
                                                  {
                                                      IsDepthEnabled = true,
                                                      DepthWriteMask = DepthWriteMask.All,
                                                      DepthComparison = Comparison.Less,
                                                      StencilReadMask = 255,
                                                      StencilWriteMask = 255
                                                  };
                _defaultDepthStencilState = new DepthStencilState(ResourceManager.Device, depthStencilDescription);
            }

            return _defaultDepthStencilState;
        }
    }

    private static DepthStencilState _disabledDepthStencilState;

    public static DepthStencilState DisabledDepthStencilState
    {
        get
        {
            if (_disabledDepthStencilState == null && ResourceManager.Device != null)
            {
                var depthStencilDescription = new DepthStencilStateDescription
                                                  {
                                                      IsDepthEnabled = false
                                                  };
                _disabledDepthStencilState = new DepthStencilState(ResourceManager.Device, depthStencilDescription);
            }

            return _disabledDepthStencilState;
        }
    }

    private static BlendState _defaultBlendState;
    public static BlendState DefaultBlendState
    {
        get
        {
            if (_defaultBlendState == null && ResourceManager.Device != null)
            {
                var blendStateDescription = new BlendStateDescription();
                blendStateDescription.RenderTarget[0].IsBlendEnabled = true;
                blendStateDescription.RenderTarget[0].SourceBlend = BlendOption.SourceAlpha;
                blendStateDescription.RenderTarget[0].DestinationBlend = BlendOption.InverseSourceAlpha;
                blendStateDescription.RenderTarget[0].BlendOperation = BlendOperation.Add;
                blendStateDescription.RenderTarget[0].SourceAlphaBlend = BlendOption.One;
                blendStateDescription.RenderTarget[0].DestinationAlphaBlend = BlendOption.InverseSourceAlpha;
                blendStateDescription.RenderTarget[0].AlphaBlendOperation = BlendOperation.Add;
                blendStateDescription.RenderTarget[0].RenderTargetWriteMask = ColorWriteMaskFlags.All;
                blendStateDescription.AlphaToCoverageEnable = false;
                _defaultBlendState = new BlendState(ResourceManager.Device, blendStateDescription);
            }

            return _defaultBlendState;
        }
    }

    private static BlendState _disabledBlendState;

    public static BlendState DisabledBlendState
    {
        get
        {
            if (_disabledBlendState == null && ResourceManager.Device != null)
            {
                var blendStateDescription = new BlendStateDescription();
                blendStateDescription.RenderTarget[0].IsBlendEnabled = false;
                blendStateDescription.RenderTarget[0].RenderTargetWriteMask = ColorWriteMaskFlags.All;
                _disabledBlendState = new BlendState(ResourceManager.Device, blendStateDescription);
            }

            return _disabledBlendState;
        }
    }

    
    private static BlendState _additiveBlendState;
    public static BlendState AdditiveBlendState
    {
        get
        {
            if (_additiveBlendState == null && ResourceManager.Device != null)
            {
                var blendStateDescription = new BlendStateDescription();
                blendStateDescription.RenderTarget[0].IsBlendEnabled = true;
                blendStateDescription.RenderTarget[0].SourceBlend = BlendOption.SourceAlpha;
                blendStateDescription.RenderTarget[0].DestinationBlend = BlendOption.One;
                blendStateDescription.RenderTarget[0].BlendOperation = BlendOperation.Add;
                
                blendStateDescription.RenderTarget[0].SourceAlphaBlend = BlendOption.One;
                blendStateDescription.RenderTarget[0].DestinationAlphaBlend = BlendOption.InverseSourceAlpha;
                blendStateDescription.RenderTarget[0].AlphaBlendOperation = BlendOperation.Add;
                
                blendStateDescription.RenderTarget[0].RenderTargetWriteMask = ColorWriteMaskFlags.All;
                blendStateDescription.AlphaToCoverageEnable = false;
                _additiveBlendState = new BlendState(ResourceManager.Device, blendStateDescription);
            }

            return _additiveBlendState;
        }
    }
    
    
    public static Color4 DefaultBlendFactor { get { return new Color4(1, 1, 1, 1); } }

    private static RasterizerState _defaultRasterizerState;

    public static RasterizerState DefaultRasterizerState
    {
        get
        {
            if (_defaultRasterizerState == null && ResourceManager.Device != null)
            {
                var desc = new RasterizerStateDescription
                               {
                                   FillMode = FillMode.Solid,
                                   CullMode = CullMode.Back,
                                   IsDepthClipEnabled = true
                               };
                _defaultRasterizerState = new RasterizerState(ResourceManager.Device, desc);
            }

            return _defaultRasterizerState;
        }
    }

    private static SamplerState _defaultSamplerState;

    public static SamplerState DefaultSamplerState
    {
        get
        {
            if (_defaultSamplerState == null && ResourceManager.Device != null)
            {
                var desc = new SamplerStateDescription
                               {
                                   Filter = Filter.MinMagMipPoint,
                                   AddressU = TextureAddressMode.Clamp,
                                   AddressV = TextureAddressMode.Clamp,
                                   AddressW = TextureAddressMode.Clamp,
                                   MaximumAnisotropy = 16,
                                   ComparisonFunction = Comparison.Never,
                                   MaximumLod = Single.MaxValue
                               };

                _defaultSamplerState = new SamplerState(ResourceManager.Device, desc);
            }

            return _defaultSamplerState;
        }
    }
}
#endif // PLATFORM_WINDOWS
