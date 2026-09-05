namespace Lib.render._dx11.buffer;

[Guid("09641970-b03a-431f-b3c6-5d8df824dde8")]
internal sealed class Texture3dComponents : Instance<Texture3dComponents>
{
    [Output(Guid = "a3772275-af0b-48e6-a3f8-ecd7c4c6eda4")]
    public readonly Slot<Texture3D> Texture = new();

    [Output(Guid = "4f9b2aeb-9bfd-400b-8839-45bec3ce2543")]
    public readonly Slot<ShaderResourceView> ShaderResourceView = new();

    [Output(Guid = "45bef676-b9c1-45d2-964c-4a505471675b")]
    public readonly Slot<UnorderedAccessView> UnorderedAccessView = new();
        
    [Output(Guid = "bc7cfc1c-db71-4fd4-ba37-ede980400aa1")]
    public readonly Slot<RenderTargetView> RenderTargetView = new();

    public Texture3dComponents()
    {
        Texture.UpdateAction += Update;
        ShaderResourceView.UpdateAction += Update;
        UnorderedAccessView.UpdateAction += Update;
        RenderTargetView.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var texture3d = Input.GetValue(context);
        if (texture3d != null)
        {
            Texture.Value = texture3d.Texture;
            ShaderResourceView.Value = texture3d.Srv;
            UnorderedAccessView.Value = texture3d.Uav;
            RenderTargetView.Value = texture3d.Rtv;
        }
    }

    [Input(Guid = "29ded573-c67a-4f19-a988-8cd6473c98a6")]
    public readonly InputSlot<Texture3dWithViews> Input = new();
}