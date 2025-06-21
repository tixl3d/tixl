namespace Lib.render._dx11.api;

[Guid("daec568f-f7b4-4d81-a401-34d62462daab")]
internal sealed class GetTextureSize : Instance<GetTextureSize>
{
    [Output(Guid = "be16d5d3-4d21-4d5a-9e4c-c7b2779b6bdc")]
    public readonly Slot<Int2> Size = new();

    [Output(Guid = "895C3BDD-38A8-4613-A8B2-503EC9D493C8")]
    public readonly Slot<Vector2> SizeFloat = new();

    [Output(Guid = "E54A3185-2E19-466B-9A1E-52A05A947FCD")]
    public readonly Slot<int> TotalSize = new();

    [Output(Guid = "209BF938-E317-4F9C-8906-265C2AFAE1E5")]
    public readonly Slot<bool> IsTextureValid = new();

    [Output(Guid = "CDEC05B6-9EE8-48F8-811F-3D1DE18A251F")]
    public readonly Slot<bool> IsCubeMap = new();
    
    public GetTextureSize()
    {
        Size.UpdateAction += Update;
        SizeFloat.UpdateAction += Update;
        TotalSize.UpdateAction += Update;
        IsTextureValid.UpdateAction += Update;
        IsCubeMap.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var isCubeMap = false;   
        var texture = Texture.GetValue(context);
        var isTextureValid = texture != null && !texture.IsDisposed;
        
        //if(isTextureValid )

        var customResolution = OverrideSize.GetValue(context);
        
        var mode = ResModes.UseContext;
        if (customResolution.Width > 0 && customResolution.Height > 0)
        {
            mode = ResModes.UseOverride;
        }
        else if (isTextureValid && customResolution.Width == 0 && customResolution.Height == 0)
        {
            mode = ResModes.UseTexture;
        }
        
        var resolution = mode switch
                             {
                                 ResModes.UseContext  => context.RequestedResolution,
                                 ResModes.UseTexture  => new Int2(texture!.Description.Width, texture.Description.Height),
                                 ResModes.UseOverride => customResolution,
                                 _                    => throw new ArgumentOutOfRangeException()
                             };


        if (isTextureValid)
        {
            isCubeMap = (texture.Description.OptionFlags & ResourceOptionFlags.TextureCube) != 0;
        }
        
        var alwaysDirty = mode == ResModes.UseContext;
        if (alwaysDirty != _alwaysDirty)
        {
            var dirtyFlagTrigger = alwaysDirty ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
            Size.DirtyFlag.Trigger = dirtyFlagTrigger;
            SizeFloat.DirtyFlag.Trigger = dirtyFlagTrigger;
            TotalSize.DirtyFlag.Trigger = dirtyFlagTrigger;
            IsTextureValid.DirtyFlag.Trigger = dirtyFlagTrigger;
            IsCubeMap.DirtyFlag.Trigger = dirtyFlagTrigger;
            
            _alwaysDirty = alwaysDirty;
        }

        
        IsTextureValid.Value = isTextureValid;
        Size.Value = resolution;
        SizeFloat.Value = new Vector2(resolution.Width, resolution.Height);
        TotalSize.Value = resolution.Width * resolution.Height;
        IsCubeMap.Value = isCubeMap;

        Size.DirtyFlag.Clear();
        TotalSize.DirtyFlag.Clear();
        SizeFloat.DirtyFlag.Clear();
        IsTextureValid.DirtyFlag.Clear();
        IsCubeMap.DirtyFlag.Clear();
    }

    private enum ResModes
    {
        UseTexture,
        UseContext,
        UseOverride,
    }

    private bool _alwaysDirty = false;

    [Input(Guid = "8b15d8e1-10c7-41e1-84db-a85e31e0c909")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "52b2f067-5619-4d8d-a982-58668a8dc6a4")]
    public readonly InputSlot<Int2> OverrideSize = new();
}