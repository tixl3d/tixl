namespace Lib.image.fx.glitch;

[Guid("e34c88f6-815e-4ce1-a6a8-59e2c8101849")]
internal sealed class SubdivisionStretch : Instance<SubdivisionStretch>
{
    [Output(Guid = "d8ec6fe5-ee96-4eaa-ba1f-05c67cdf0f0b")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "40bc83fb-a3a4-4bfd-b131-8ecf2908b1a3")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "32d082f5-d6e1-4068-bcd9-a01977ed72df")]
    public readonly InputSlot<int> MaxSubdivisions = new InputSlot<int>();

    [Input(Guid = "71c99158-cf0e-4ceb-82ee-5ef5685441b3")]
    public readonly InputSlot<float> Threshold = new InputSlot<float>();

    [Input(Guid = "6bdb499c-1300-4fe1-8634-7d3edc4d4050")]
    public readonly InputSlot<bool> UseAspectForSplit = new InputSlot<bool>();

    [Input(Guid = "d30b5542-d564-46e1-9f9e-987351eb425b")]
    public readonly InputSlot<float> SplitCenter = new InputSlot<float>();

    [Input(Guid = "c8550e83-bd37-4db3-a859-4a6bcd64a63c")]
    public readonly InputSlot<float> SplitCenterVariation = new InputSlot<float>();

    [Input(Guid = "6d3c2c76-df35-411a-8e25-450dc9cd6f4e")]
    public readonly InputSlot<float> DirectionBias = new InputSlot<float>();

    [Input(Guid = "c6f0a794-ba0f-44cf-95cb-9ec6484f9f02")]
    public readonly InputSlot<Vector2> ScrollOffset = new InputSlot<Vector2>();

    [Input(Guid = "22afde68-08c5-438f-9425-fe255b2e079f")]
    public readonly InputSlot<Vector2> ScrollGainAndBias = new InputSlot<Vector2>();

    [Input(Guid = "7a8684c9-ee81-49c1-ad13-d91d62799efb")]
    public readonly InputSlot<float> RandomPhase = new InputSlot<float>();

    [Input(Guid = "819898d2-230b-4176-a1b7-a008aed9631d")]
    public readonly InputSlot<int> RandomSeed = new InputSlot<int>();

    [Input(Guid = "82a8fbb3-c1be-494e-8a8d-e7ccf5440556")]
    public readonly InputSlot<float> GapWidth = new InputSlot<float>();

    [Input(Guid = "3e164af6-1cb3-45b3-a319-e562789d73f7")]
    public readonly InputSlot<float> Feather = new InputSlot<float>();

    [Input(Guid = "5cae4d6e-d441-42f7-8e17-3aeb58719f08")]
    public readonly InputSlot<Vector4> GapColor = new InputSlot<Vector4>();

    [Input(Guid = "ee2bc31a-a67f-4bec-bce7-0a70db88082c", MappedType = typeof(GradientModes))]
    public readonly InputSlot<int> GradientMode = new InputSlot<int>();

    [Input(Guid = "be07242e-39fc-4631-ab6e-3023c9444406")]
    public readonly InputSlot<Gradient> Gradient = new InputSlot<Gradient>();

    [Input(Guid = "DEE8B0C6-CAF4-4C23-9F9E-2B8F468476F6", MappedType = typeof(ColorModes))]
    public readonly InputSlot<int> ColorMode = new InputSlot<int>();

    [Input(Guid = "d90350a0-9fa8-4747-805b-ba7ab8125595")]
    public readonly InputSlot<bool> Use4xMSAA = new InputSlot<bool>();

    [Input(Guid = "9606da82-7896-4312-9e0c-dd3f2c178cab")]
    public readonly InputSlot<float> TextureFx = new InputSlot<float>();

    private enum GradientModes
    {
        Random,
        Subdivisions,
    }

    private enum ColorModes
    {
        MultiplyGradient,
        GradientOnly,
    }
}