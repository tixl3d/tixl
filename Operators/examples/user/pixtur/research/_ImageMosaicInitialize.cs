namespace Examples.user.pixtur.research;

[Guid("0c30ce21-0c02-4676-a636-63d744bfa788")]
public class _ImageMosaicInitialize : Instance<_ImageMosaicInitialize>
{
    [Output(Guid = "2edb3376-7daf-4ab7-9633-2890253bd2ee")]
    public readonly Slot<ShaderResourceView> ShaderResourceView = new();

    [Input(Guid = "914df0ba-a276-4839-868c-a903b744ec04")]
    public readonly InputSlot<bool> TriggerUpdate = new();

    [Input(Guid = "a16ccad1-00b7-4779-8346-52b03b81249f")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "6d9eb435-595b-421f-9852-6e923a532aed")]
    public readonly InputSlot<string> Folder = new();

    [Output(Guid = "70aa6b0a-3096-4bee-a2ab-9fe63a31e3fa")]
    public readonly Slot<Command> Output2 = new();

    [Output(Guid = "c0f96ebc-9cf7-4eef-9442-ebd7a47dce5c")]
    public readonly Slot<Texture2D> TextureArray = new();


}