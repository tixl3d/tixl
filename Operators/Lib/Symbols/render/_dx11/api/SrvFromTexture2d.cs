using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using T3.Core.Utils;

namespace Lib.render._dx11.api;

[Guid("c2078514-cf1d-439c-a732-0d7b31b5084a")]
internal sealed class SrvFromTexture2d : Instance<SrvFromTexture2d>
{
    [Output(Guid = "{DC71F39F-3FBA-4FC6-B8EF-CE57C82BF78E}")]
    public readonly Slot<ShaderResourceView> ShaderResourceView = new();

    public SrvFromTexture2d()
    {
        ShaderResourceView.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        
        var texture = Texture.GetValue(context);
        if (texture != null && ShaderResourceView.Value != null && texture.GetHashCode() == _textureHash)
            return;

        _textureHash = texture?.GetHashCode() ?? 0;
        
        try
        {

            if (texture != null)
            {
                ShaderResourceView.Value?.Dispose();
                if ((texture.Description.BindFlags & BindFlags.DepthStencil) > 0)
                {
                    // it's a depth stencil texture, so we need to set the format explicitly
                    var desc = new ShaderResourceViewDescription()
                                   {
                                       Format = Format.R32_Float,
                                       Dimension = ShaderResourceViewDimension.Texture2D,
                                       Texture2D = new ShaderResourceViewDescription.Texture2DResource
                                                       {
                                                           MipLevels = 1,
                                                           MostDetailedMip = 0
                                                       }
                                   };
                    ShaderResourceView.Value = new ShaderResourceView(ResourceManager.Device, texture, desc);
                }
                else
                {
                    // todo: create via resource manager
                    // Sadly, this is not straight forward, because the texture resources are created and
                    // disposed in various situations that are not really evident (e.g. in AbstractResource).
                    ShaderResourceView.Value = new ShaderResourceView(ResourceManager.Device, texture); 
                }
            }
            else
            {
                Utilities.Dispose(ref ShaderResourceView.Value);
            }
            _complainedOnce = false;
        }
        catch (Exception e)
        {
            if(!_complainedOnce)
                Log.Error("Updating Shader Resource View failed: " + e.Message, this);

            _complainedOnce = true;
                
        }
    }

    private bool _complainedOnce = false;
    private int _textureHash;

    [Input(Guid = "{D5AFA102-2F88-431E-9CD4-AF91E41F88F6}")]
    public readonly InputSlot<Texture2D> Texture = new();
}