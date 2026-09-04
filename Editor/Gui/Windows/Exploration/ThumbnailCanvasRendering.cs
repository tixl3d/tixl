using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Editor.Gui.UiHelpers;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.Exploration;

/// <summary>
/// A helper class that manages the setup for copying a texture in a texture atlas.
/// This is used to reduce exceeding resource usage for variation canvas.   
/// </summary>
public sealed class ThumbnailCanvasRendering
{
    public void InitializeCanvasTexture(Vector2 thumbnailSize)
    {
        if (_initialized)
            return;
            
        // if (_canvasTexture != null || _canvasTextureRtv == null)
        //     return;

        EvaluationContext = new EvaluationContext()
                                {
                                    RequestedResolution = new Int2((int)thumbnailSize.X, (int)thumbnailSize.Y)
                                };

        var description = new Texture2DDescription()
                              {
                                  Height = 2048,
                                  Width = 2048,
                                  ArraySize = 1,
                                  BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                                  Usage = ResourceUsage.Default,
                                  CpuAccessFlags = CpuAccessFlags.None,
                                  Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                                  //MipLevels = mipLevels,
                                  OptionFlags = ResourceOptionFlags.GenerateMipMaps,
                                  SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                              };

        _canvasTexture = Texture2D.CreateTexture2D(description);
        CanvasTextureSrv = SrvManager.GetSrvForTexture(_canvasTexture);
        _canvasTextureRtv = new RenderTargetView(Program.Device, _canvasTexture);
        _initialized = true;
    }

    public void CopyToCanvasTexture(Slot<Texture2D> textureSlot, ImRect rect)
    {
        if (!_initialized)
            return;
            
        var previewTextureSrv = SrvManager.GetSrvForTexture(textureSlot.Value);

        // Setup graphics pipeline for rendering into the canvas texture
        var deviceContext = ResourceManager.Device.ImmediateContext;
        deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

        deviceContext.Rasterizer.SetViewport(new ViewportF(rect.Min.X,
                                                           rect.Min.Y,
                                                           rect.GetWidth(),
                                                           rect.GetHeight(),
                                                           0.0f, 1.0f));
        deviceContext.OutputMerger.SetTargets(_canvasTextureRtv);

        var vertexShader = SharedResources.FullScreenVertexShaderResource.Value;
        deviceContext.VertexShader.Set(vertexShader);
        var pixelShader = SharedResources.FullScreenPixelShaderResource.Value;
        deviceContext.PixelShader.Set(pixelShader);
        deviceContext.PixelShader.SetShaderResource(0, previewTextureSrv);

        // Render the preview in the canvas texture
        deviceContext.Draw(3, 0);
        deviceContext.PixelShader.SetShaderResource(0, null);
    }

    public void ClearTexture()
    {
        if (!_initialized)
            return;
            
        Program.Device.ImmediateContext.ClearRenderTargetView(_canvasTextureRtv, new RawColor4(0, 0, 0, 0));
    }

    public Vector2 GetCanvasTextureSize()
    {
        return _initialized 
                   ? new Vector2(_canvasTexture.Description.Width, _canvasTexture.Description.Height)
                   : Vector2.Zero;
    }

            
    public EvaluationContext EvaluationContext;
    public ShaderResourceView CanvasTextureSrv;

    private Texture2D _canvasTexture;
    private RenderTargetView _canvasTextureRtv;
    private bool _initialized;
}