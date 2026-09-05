using SharpDX.Direct3D11;
using T3.Core.Utils;

namespace Lib.render._dx11.api;

[Guid("57a1ee33-702a-41ad-a17e-b43033d58638")]
internal sealed class RtvFromTexture2d : Instance<RtvFromTexture2d>, IStatusProvider
{
    [Output(Guid = "515E87C9-4CF8-4948-BA64-F6261F7FE5FC")]
    public readonly Slot<RenderTargetView> RenderTargetView = new();

    public RtvFromTexture2d()
    {
        RenderTargetView.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (!Texture.DirtyFlag.IsDirty && !ArrayIndex.DirtyFlag.IsDirty)
            return; // nothing to do

        var arrayIndex = ArrayIndex.GetValue(context).Clamp(0,10000);

        try
        {
            _lastErrorMessage = null;

            Texture2D texture = Texture.GetValue(context);
            if (texture != null)
            {
                var maxArraySize = texture.Description.ArraySize;
                if (arrayIndex > maxArraySize)
                {
                    var message = $"{arrayIndex} exceeds texture array size of {maxArraySize}";
                    Log.Warning(message, SymbolChildId);
                    arrayIndex = maxArraySize - 1;
                }
                    
                if (((int)texture.Description.BindFlags & (int)BindFlags.RenderTarget) > 0)
                {
                    RenderTargetView.Value?.Dispose();

                    var isTextureCube = (texture.Description.OptionFlags & ResourceOptionFlags.TextureCube) != 0;
                    if (isTextureCube)
                    {
                        var rtvDesc = new RenderTargetViewDescription()
                                          {
                                              Dimension = RenderTargetViewDimension.Texture2DArray,
                                              Format = texture.Description.Format,
                                              Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource()
                                                                   {
                                                                       ArraySize = 6,
                                                                       FirstArraySlice = 0,
                                                                       MipSlice = 0
                                                                   }
                                          };
                        RenderTargetView.Value = new RenderTargetView(ResourceManager.Device, texture, rtvDesc);
                    }
                    else if (arrayIndex > 0 && maxArraySize > 0)
                    {
                        var rtvDesc = new RenderTargetViewDescription()
                                          {
                                              Dimension = RenderTargetViewDimension.Texture2DArray,
                                              Format = texture.Description.Format,
                                              Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource()
                                                                   {
                                                                       FirstArraySlice = arrayIndex,
                                                                       ArraySize = 1,
                                                                       MipSlice = 0,
                                                                   }
                                          };
                            
                        RenderTargetView.Value = new RenderTargetView(ResourceManager.Device, texture, rtvDesc);
                    }
                    else
                    {
                        RenderTargetView.Value = new RenderTargetView(ResourceManager.Device, texture); // todo: create via resource manager
                    }
                }
                else
                {
                    Log.Warning("Trying to create an render target view for resource which doesn't have the rtv bind flag set", this);
                }
            }

        }
        catch (Exception e)
        {
            var message = $"Failed to create RenderTextureView: {e.Message}";
            _lastErrorMessage = null;
            Log.Warning(message, this);
        }
            
    }

    [Input(Guid = "73CF7C5D-CF0C-49DB-91E8-DAFE812E0232")]
    public readonly InputSlot<Texture2D> Texture = new();
        
    [Input(Guid = "00FC3534-0C07-41C2-9D56-3484D9B3A41F")]
    public readonly InputSlot<int> ArrayIndex = new();

    private string _lastErrorMessage = null;
        
    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        return _lastErrorMessage;
    }
}