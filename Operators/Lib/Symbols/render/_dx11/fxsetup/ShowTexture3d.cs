namespace Lib.render._dx11.fxsetup;

[Guid("9733f5e1-4514-46de-9e7c-bd3912932d1b")]
internal sealed class ShowTexture3d : Instance<ShowTexture3d>
{
    [Output(Guid = "f5d05816-108d-4acf-a1f8-e1fbbfac2adb")]
    public readonly Slot<Texture3dWithViews> TextureOutput = new();

    public ShowTexture3d()
    {
        TextureOutput.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Command.GetValue(context);
        TextureOutput.Value = Texture.GetValue(context);
    }

    [Input(Guid = "bf5321e1-56b5-49ee-bd83-7c949bafef16")]
    public readonly InputSlot<Command> Command = new();
    [Input(Guid = "59cb775e-6dc5-4228-88c9-0ba11439cf56")]
    public readonly InputSlot<Texture3dWithViews> Texture = new();
}