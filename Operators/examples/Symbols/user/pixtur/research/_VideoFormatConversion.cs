namespace Examples.user.pixtur.research;

[Guid("7845ec24-e249-4c6a-84e3-63422ede1a1d")]
public class _VideoFormatConversion : Instance<_VideoFormatConversion>
{
    [Output(Guid = "a722cd4f-82da-4532-a7bc-7a6bdc30b06e")]
    public readonly Slot<Texture2D> Output = new();


    [Input(Guid = "4454891b-ddd0-41f2-80c3-75074df8320b")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "b0c4ceaa-5101-42c6-bf71-230a01918916")]
    public readonly InputSlot<SharpDX.DXGI.Format> Format = new();

}