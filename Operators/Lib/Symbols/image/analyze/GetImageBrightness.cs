namespace Lib.image.analyze;

[Guid("787f44a8-8c51-4cfa-a7d5-7014d11b6a28")]
internal sealed class GetImageBrightness : Instance<GetImageBrightness>
{

    [Output(Guid = "4a1be0ac-96af-459b-b31a-2fd1373964ab")]
    public readonly Slot<Command> Update = new Slot<Command>();

    [Output(Guid = "0edd2f73-be22-4164-9d0f-0552faddb094")]
    public readonly Slot<float> Brightness = new Slot<float>();

    [Input(Guid = "380ee818-52f2-4eed-9a6b-4a44a90abd7b")]
    public readonly InputSlot<Texture2D> Texture2d = new();

}