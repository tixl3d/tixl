namespace Lib.render._dx11.api;

[Guid("84e02044-3011-4a5e-b76a-c904d9b4557f")]
internal sealed class UavFromTexture2d : Instance<UavFromTexture2d>
{
    [Output(Guid = "{83D2DCFD-3850-45D8-BB1B-93FE9C9F4334}")]
    public readonly Slot<UnorderedAccessView> UnorderedAccessView = new();

    public UavFromTexture2d()
    {
        UnorderedAccessView.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (!Texture.DirtyFlag.IsDirty)
            return; // nothing to do

        var texture = Texture.GetValue(context);
        if (texture == null || texture.IsDisposed)
            return;
            
        try
        {
            if (texture.Description.BindFlags.HasFlag(BindFlags.UnorderedAccess))
            {
                UnorderedAccessView.Value?.Dispose();
                UnorderedAccessView.Value = new UnorderedAccessView(ResourceManager.Device, texture); // todo: create via resource manager
            }
            else
            {
                Log.Warning("Trying to create an unordered access view for resource which doesn't have the uav bind flag set", this);
            }
        }
        catch (Exception e)
        {   
            Log.Error("UavFromTexture2d exception: " + e.Message, this);
            UnorderedAccessView.Value = null;
        }
    }

    [Input(Guid = "{4A4F6830-1809-42C9-91EB-D4DBD0290043}")]
    public readonly InputSlot<Texture2D> Texture = new();
}