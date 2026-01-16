namespace Types.Gfx;

[Guid("fc1ef086-c160-4174-8e60-a4eda931163d")]
public sealed class Texture3d : Instance<Texture3d>
{
    // [Output(Guid = "27495e79-5229-4a2d-b780-52265c3085ea")]
    // public readonly Slot<Texture2D> Texture = new Slot<Texture2D>();
    [Output(Guid = "3cbfceaa-4fa1-44e9-8c43-aff7dba7f871")]
    public readonly Slot<Texture3dWithViews> OutputTexture = new(new Texture3dWithViews());

        
    public Texture3d()
    {
        OutputTexture.UpdateAction += UpdateTexture;
    }

    private void UpdateTexture(EvaluationContext context)
    {
        Int3 size = Size.GetValue(context);
        if (size.X < 1 || size.Y < 1 || size.Z < 1)
        {
            Log.Warning($"Requested invalid texture resolution: {size}", this);
            return;
        }

        var texDesc = new Texture3DDescription
                          {
                              Width = size.X,
                              Height = size.Y,
                              Depth = size.Z,
                              MipLevels = MipLevels.GetValue(context),
                              Format = Format.GetValue(context),
                              Usage = ResourceUsage.GetValue(context),
                              BindFlags = BindFlags.GetValue(context),
                              CpuAccessFlags = CpuAccessFlags.GetValue(context),
                              OptionFlags = ResourceOptionFlags.GetValue(context)
                          };
        if (CreateTexture3d(texDesc, ref _texture3d))
        {
            var tex = _texture3d;
            OutputTexture.Value!.Texture = tex;
                
            if ((BindFlags.Value & SharpDX.Direct3D11.BindFlags.ShaderResource) > 0)
                tex.CreateShaderResourceView(ref OutputTexture.Value.Srv, "");
            if ((BindFlags.Value & SharpDX.Direct3D11.BindFlags.RenderTarget) > 0)
                tex.CreateRenderTargetView(ref OutputTexture.Value.Rtv, "");
            if ((BindFlags.Value & SharpDX.Direct3D11.BindFlags.UnorderedAccess) > 0)
                tex.CreateUnorderedAccessView(ref OutputTexture.Value.Uav, "");
                
        }
    }

    private static bool CreateTexture3d(Texture3DDescription description, [NotNullWhen(true)] ref Texture3D? texture)
    {
        var shouldCreateNew = texture == null;
        try
        {
            if (texture != null)
            {
                shouldCreateNew = shouldCreateNew || !EqualityComparer<Texture3DDescription>.Default.Equals(texture.Description, description);
            }
        }
        catch (Exception e)
        {
            shouldCreateNew = true;
            Log.Warning($"Failed to get texture description: {e}");
        }

        if (shouldCreateNew)
        {
            texture?.Dispose();
            texture = Texture3D.CreateTexture3D(description);
            return true;
        }

        // unchanged
        return false;
    }
    
    private Texture3D? _texture3d;


    [Input(Guid = "dca953d6-bdc1-42eb-9a4d-5974c42cf45b")]
    public readonly InputSlot<Int3> Size = new();

    [Input(Guid = "2e0fd6be-0c9e-4624-803c-178d1d80ea43")]
    public readonly InputSlot<int> MipLevels = new();

    [Input(Guid = "ce649059-f136-4d32-81c6-23d7b55f3378")]
    public readonly InputSlot<Format> Format = new();

    [Input(Guid = "7db98a0e-2589-425b-95eb-d7614e82ed93")]
    public readonly InputSlot<ResourceUsage> ResourceUsage = new();

    [Input(Guid = "b824dbd6-272d-4744-a20d-5afa5caa9209")]
    public readonly InputSlot<BindFlags> BindFlags = new();

    [Input(Guid = "cfd3cfbf-7429-42f9-abc9-0e0e173f0297")]
    public readonly InputSlot<CpuAccessFlags> CpuAccessFlags = new();

    [Input(Guid = "1884edfa-622b-4b96-a081-95dc361e79f3")]
    public readonly InputSlot<ResourceOptionFlags> ResourceOptionFlags = new();
}