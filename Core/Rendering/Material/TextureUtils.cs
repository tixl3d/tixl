using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Resource;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Core.Rendering.Material;

internal static class TextureUtils
{
    internal static Texture2D CreateColorTexture(Vector4 c)
    {
        var colorDesc = new Texture2DDescription()
                            {
                                ArraySize = 1,
                                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                                CpuAccessFlags = CpuAccessFlags.None,
                                Format = Format.R16G16B16A16_Float,
                                Width = 1,
                                Height = 1,
                                MipLevels = 0,
                                OptionFlags = ResourceOptionFlags.None,
                                SampleDescription = new SampleDescription(1, 0),
                                Usage = ResourceUsage.Default
                            };

        var dxTex = new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, colorDesc);
        var colorBuffer = new Texture2D(dxTex);
        var colorBufferRtv = new RenderTargetView(ResourceManager.Device, dxTex);
        ResourceManager.Device.ImmediateContext.ClearRenderTargetView(colorBufferRtv, new Color(c.X, c.Y, c.Z, c.W));
        return colorBuffer;
    }
}