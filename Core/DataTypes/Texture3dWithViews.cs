namespace T3.Core.DataTypes;

public class Texture3dWithViews
{
    public T3.Core.DataTypes.Texture3D Texture;
    public T3.Graphics.Compat.ShaderResourceView Srv;
    public T3.Graphics.Compat.UnorderedAccessView Uav;
    public T3.Graphics.Compat.RenderTargetView Rtv;
}