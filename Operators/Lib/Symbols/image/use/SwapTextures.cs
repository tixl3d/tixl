namespace Lib.image.use;

[Guid("590a0b0b-c847-433c-8ffa-602ed0ae8f28")]
internal sealed class SwapTextures : Instance<SwapTextures>
{
    [Output(Guid = "699B819E-E807-425F-8195-DD1E45E993DC")]
    public readonly Slot<Texture2D> TextureA = new();

    [Output(Guid = "6E60449C-ACA3-40A2-A792-247023E424EA")]
    public readonly Slot<Texture2D> TextureB = new();

        
    public SwapTextures()
    {
        TextureA.UpdateAction += Update;
        TextureB.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var textureAValue = TextureBInput.GetValue(context);
        var textureBValue = TextureAInput.GetValue(context);
            
        if (EnableSwap.GetValue(context))
        {
            TextureA.Value = textureAValue;
            TextureB.Value = textureBValue;
        }
        else
        {
            TextureA.Value = textureBValue;
            TextureB.Value = textureAValue;
        }
            
        TextureA.DirtyFlag.Clear();
        TextureB.DirtyFlag.Clear();
    }

    [Input(Guid = "9DD14A67-AFDB-4CD4-BDD5-9FCD565BC65D")]
    public readonly InputSlot<Texture2D> TextureAInput = new();

    [Input(Guid = "3EFA9405-7078-4924-8050-52885D6B67EC")]
    public readonly InputSlot<Texture2D> TextureBInput = new();

    [Input(Guid = "baa2c4e5-a0ad-42b3-b142-3c61be471383")]
    public readonly InputSlot<bool> EnableSwap = new();
        
}