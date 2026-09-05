namespace Lib.image.use;

[Guid("3d0ad320-1055-4f71-b3c0-0e77261ca587")]
internal sealed class CombineMaterialChannels2 : Instance<CombineMaterialChannels2>
{
    [Output(Guid = "f80ea674-b8fe-497c-9628-b4a3653e2723")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "223ff98a-9e21-46a0-954e-8c763226128e")]
    public readonly InputSlot<Texture2D> ImageA = new();

    [Input(Guid = "8e959b1a-5f19-4962-a595-468370f42965")]
    public readonly InputSlot<Vector4> ColorA = new();

    [Input(Guid = "cca1cccc-b89c-4d08-8976-60740fab37ef")]
    public readonly InputSlot<Texture2D> ImageB = new();

    [Input(Guid = "0338198a-0f30-4c54-84bd-49838515de57")]
    public readonly InputSlot<Vector4> ColorB = new();

    [Input(Guid = "af0ee1f7-e78f-4ed8-a1c4-ef0928f6a69c")]
    public readonly InputSlot<Texture2D> ImageC = new();

    [Input(Guid = "99ff61d9-feed-4631-8833-9d108818260e")]
    public readonly InputSlot<Vector4> ColorC = new();

    //[Input(Guid = "c8c55f97-dbbd-4095-b2a1-d0a257bad1ea")]
    //public readonly InputSlot<int> SelectChannel_R = new InputSlot<int>();

    [Input(Guid = "5b4a01f1-0571-42c7-bcdd-1cfd8149a5b3")]
    public readonly InputSlot<int> SelectChannel_R = new();

    //[Input(Guid = "08b639cb-7cbd-4a1f-8609-c50df879fd91")]
    //public readonly InputSlot<int> SelectChannel_G = new InputSlot<int>();

    [Input(Guid = "271594ed-bdce-4c5a-a274-1bc1564151d1")]
    public readonly InputSlot<int> SelectChannel_G = new();

    //[Input(Guid = "b1f8e352-cea7-4b09-bc8e-0769940610e9")]
    //public readonly InputSlot<int> SelectChannel_B = new InputSlot<int>();

    [Input(Guid = "46b0d661-6fe2-4b40-bcd1-ddd9a67894ec")]
    public readonly InputSlot<int> SelectChannel_B = new();

    //[Input(Guid = "0aa7851e-1845-4cd0-98fd-25d2a77a35a7")]
    //public readonly InputSlot<int> AlphaMode = new InputSlot<int>();

    [Input(Guid = "3569b0ee-3e37-404d-8c4c-939cbbb6d358")]
    public readonly InputSlot<int> SelectAlphaChannel = new();

    [Input(Guid = "a2c0720a-ee57-4e83-b2b1-5f38b09415d9")]
    public readonly InputSlot<bool> GenerateMips = new();

    private enum SelectInput
    {
        ImageA_R = 0,
        ImageA_G = 1,
        ImageA_B = 2,
        ImageA_Average = 3,
        ImageA_Brightness = 4,
        ImageB_R = 5,
        ImageB_G = 6,
        ImageB_B = 7,
        ImageB_Average = 8,
        ImageB_Brightness = 9,
        ImageC_R = 10,
        ImageC_G = 11,
        ImageC_B = 12,
        ImageC_Average = 13,
        ImageC_Brightness = 14,
    }

    private enum SelectAlphaInput
    {
        UseImageA_Alpha = 0,
        UseImageB_Alpha = 1,
        UseImageC_Alpha = 2,
        SetToZero = 3,
        SetToOne = 4,
    }
}