namespace Lib.image.use;

[Guid("1d958538-98e7-4053-b1e2-3b9f1bc4faa9")]
internal sealed class Combine3Images : Instance<Combine3Images>
{
    [Output(Guid = "d45d6948-5482-4585-9c05-5d32f99b2558")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "81e08eab-9428-435b-a710-6cede1549834")]
    public readonly InputSlot<Texture2D> ImageA = new();

    [Input(Guid = "a3d1ab20-fbfb-4123-a877-09050edae8bf")]
    public readonly InputSlot<Vector4> ColorA = new();

    [Input(Guid = "d42bf975-9789-40d8-9a7e-c83b157603fb")]
    public readonly InputSlot<Texture2D> ImageB = new();

    [Input(Guid = "188ab3e1-4a91-48e8-a764-6178f006069b")]
    public readonly InputSlot<Vector4> ColorB = new();

    [Input(Guid = "8950b878-944d-42e1-b106-49337fa15ffa")]
    public readonly InputSlot<Texture2D> ImageC = new();

    [Input(Guid = "f6438d7e-da04-4e43-a8f1-2c18b6aff948")]
    public readonly InputSlot<Vector4> ColorC = new();

    //[Input(Guid = "c8c55f97-dbbd-4095-b2a1-d0a257bad1ea")]
    //public readonly InputSlot<int> SelectChannel_R = new InputSlot<int>();

    [Input(Guid = "c8c55f97-dbbd-4095-b2a1-d0a257bad1ea", MappedType = typeof(SelectInput))]
    public readonly InputSlot<int> SelectChannel_R = new();

    //[Input(Guid = "08b639cb-7cbd-4a1f-8609-c50df879fd91")]
    //public readonly InputSlot<int> SelectChannel_G = new InputSlot<int>();

    [Input(Guid = "08b639cb-7cbd-4a1f-8609-c50df879fd91", MappedType = typeof(SelectInput))]
    public readonly InputSlot<int> SelectChannel_G = new();

    //[Input(Guid = "b1f8e352-cea7-4b09-bc8e-0769940610e9")]
    //public readonly InputSlot<int> SelectChannel_B = new InputSlot<int>();

    [Input(Guid = "b1f8e352-cea7-4b09-bc8e-0769940610e9", MappedType = typeof(SelectInput))]
    public readonly InputSlot<int> SelectChannel_B = new();

    //[Input(Guid = "0aa7851e-1845-4cd0-98fd-25d2a77a35a7")]
    //public readonly InputSlot<int> AlphaMode = new InputSlot<int>();

    [Input(Guid = "0aa7851e-1845-4cd0-98fd-25d2a77a35a7", MappedType = typeof(SelectAlphaInput))]
    public readonly InputSlot<int> SelectAlphaChannel = new();

    [Input(Guid = "2b6f8853-5836-45d5-800b-c56e1c59e3c9")]
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