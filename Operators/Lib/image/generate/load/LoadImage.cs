namespace Lib.image.generate.load;

[Guid("0b3436db-e283-436e-ba85-2f3a1de76a9d")]
internal sealed class LoadImage : Instance<LoadImage>, IDescriptiveFilename, IStatusProvider
{
    [Output(Guid = "E0C4FEDD-5C2F-46C8-B67D-5667435FB037")]
    public readonly Slot<Texture2D> Texture = new();

    public LoadImage()
    {
        _textureResource = ResourceManager.CreateTextureResource(Path);
        _textureResource.AddDependentSlots(Texture);
        
        Texture.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var useCache = CacheResources.GetValue(context);
        if (!useCache)
        {
            DisposeCache();            
            
            Texture.Value = _textureResource.GetValue(context);
            Texture.DirtyFlag.Clear();

            if (Texture.Value == null)
            {
                _lastErrorMessage = "Failed to load texture: " + Path.Value;
            
                if(!string.IsNullOrEmpty(Path.Value))
                    Log.Warning(_lastErrorMessage, this);
            
                return;
            }

            var currentSrv = SrvManager.GetSrvForTexture(Texture.Value);
            if (currentSrv == null || currentSrv.IsDisposed)
                return;
        
            try
            {
                ResourceManager.Device.ImmediateContext.GenerateMips(currentSrv);
            }
            catch (Exception exception)
            {
                Log.Error($"Failed to generate mipmaps for texture {Path.Value}:" + exception);
            }
        }
        else
        {
            var filePath = Path.GetValue(context);
            if (_resourcesCache.TryGetValue(filePath, out var resource))
            {
                Texture.Value = resource.Value;
                return;
            }

            var newResource = ResourceManager.CreateTextureResource(filePath, this);
            if (newResource.Value != null)
            {
                _resourcesCache[filePath] = newResource;
                
                var currentSrv = SrvManager.GetSrvForTexture(newResource.Value);
                if (currentSrv == null || currentSrv.IsDisposed)
                    return;
        
                try
                {
                    ResourceManager.Device.ImmediateContext.GenerateMips(currentSrv);
                }
                catch (Exception exception)
                {
                    Log.Error($"Failed to generate mipmaps for texture {Path.Value}:" + exception);
                }
            }
        }
        
        _lastErrorMessage = string.Empty;
    }

    private void DisposeCache()
    {
        if (_resourcesCache.Count == 0)
            return;

        foreach (var r in _resourcesCache.Values)
        {
            r.Dispose();
        }
        _resourcesCache.Clear();
    }

    [Input(Guid = "76CC3811-4AE0-48B2-A119-890DB5A4EEB2")]
    public readonly InputSlot<string> Path = new();


    [Input(Guid = "96044C6C-D005-4D61-B605-7A6896E98CB6")]
    public readonly InputSlot<bool> CacheResources = new();
    
    
    private readonly Dictionary<string,  Resource<Texture2D> > _resourcesCache = new();
    
    public InputSlot<string> SourcePathSlot => Path;

    private readonly Resource<Texture2D> _textureResource;

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) 
            ? IStatusProvider.StatusLevel.Success 
            : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _lastErrorMessage;

    private string _lastErrorMessage = string.Empty;
}