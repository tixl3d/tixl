namespace Lib.image.color;


[Guid("d5516087-f7dd-44d4-a7e1-3c18767de921")]
internal sealed class ConvertColors : Instance<ConvertColors>
{
    [Output(Guid = "49e4972f-e360-4bc3-b780-032d5e985540")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "dd2f08a9-a539-41e9-aa1e-f8ad64fb8d29")]
    public readonly InputSlot<Texture2D> Texture2d = new();

    [Input(Guid = "ffcba423-04e2-4bc6-b3bf-fbc3b15c84b8", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "c11ad183-0f0c-46cb-b543-8f39cc707427")]
    public readonly InputSlot<bool> GenerateMipmaps = new();

    [Input(Guid = "caa374cf-d07a-4af9-89a0-42597e11a6ff")]
    public readonly InputSlot<Format> OutputFormat = new();

    private enum Modes
    {
        RgbToOKLab,
        OKLabToRgb,
        RgbToLCh,
        LChToRgb,
    }
}