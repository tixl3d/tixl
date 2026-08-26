namespace Lib.image.analyze;

[Guid("0309e746-c356-4c7b-af05-93136a2607de")]
 internal sealed class OpticalFlow : Instance<OpticalFlow>
{
    [Output(Guid = "4969429f-c7f6-441e-94ab-2a5a12e4cb11")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "93227e69-35c7-4db6-bc6e-e655f2f8226a")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "394c0b8a-0d6e-4740-bd33-28c651b1471d")]
    public readonly InputSlot<Texture2D> Image2 = new();

    [Input(Guid = "a1eba7f5-e2f1-46cb-8982-ae9cb1b7531b")]
    public readonly InputSlot<float> Lod = new();

    [Input(Guid = "fa54b5d3-5a98-4cc1-b848-6e5b3632af6c")]
    public readonly InputSlot<bool> VisualizeResult = new();

    [Input(Guid = "587e2f2b-fd6a-4382-b1c2-99a5963d41ac")]
    public readonly InputSlot<float> VisualizationScale = new();

    [Input(Guid = "c54cd59c-7b60-4044-b178-1faa9b279138")]
    public readonly InputSlot<float> Amount = new();

    [Input(Guid = "ea2841c1-62bf-4826-8861-6e1c482a0fcd")]
    public readonly InputSlot<System.Numerics.Vector2> ClampRange = new();

    [Input(Guid = "dc54082f-0e94-471e-8aca-4573c167fda3", MappedType = typeof(OutputModes))]
    public readonly InputSlot<int> OutputMethod = new();

    private enum OutputModes
    {
        Signed,
        GrayScale,
    }
}