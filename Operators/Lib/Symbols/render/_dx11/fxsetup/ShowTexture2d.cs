namespace Lib.render._dx11.fxsetup;

[Guid("b0212daa-7fba-4f6a-851c-3dd9e2e8a23e")]
internal sealed class ShowTexture2d : Instance<ShowTexture2d>
{
    [Output(Guid = "{996A44A6-005B-421F-85A4-A3CCA425044E}", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Texture2D> TextureOutput = new();

    public ShowTexture2d()
    {
        TextureOutput.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Command.GetValue(context);
        TextureOutput.Value = Texture.GetValue(context);
    }

    [Input(Guid = "{5A3E1FA0-21FC-4C2E-A4BB-45A311F24C00}")]
    public readonly InputSlot<Command> Command = new();
    [Input(Guid = "{5095C803-FA2A-408C-AB56-8057E49648D5}")]
    public readonly InputSlot<Texture2D> Texture = new();
}