namespace Lib.point.sim;

[Guid("5f846187-e109-45d1-97e0-ae95e3e7d9ba")]
internal sealed class SimNoiseOffset : Instance<SimNoiseOffset>
{

    [Output(Guid = "cf976e9a-ea57-44f7-aeb9-f57f2e712b41")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "679d6321-a5d7-41cc-a191-5e6cf353dfc4")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "cc98bda7-34a5-4c18-9b9e-fabe51b02d71")]
    public readonly InputSlot<float> Amount = new();

    [Input(Guid = "757c259a-118c-4438-bc09-cdc5708af2bc")]
    public readonly InputSlot<float> Frequency = new();

    [Input(Guid = "cb3ae9c7-5146-4bc6-9a87-3d17012fab52")]
    public readonly InputSlot<float> Phase = new();

    [Input(Guid = "ecdccc3c-b7a4-43f7-a4c8-0269ecff916a")]
    public readonly InputSlot<float> Variation = new();

    [Input(Guid = "7f73e109-b13a-4114-b2a5-fe9e86270893")]
    public readonly InputSlot<Vector3> AmountDistribution = new();

    [Input(Guid = "41ee0e65-e2dc-4bf7-af40-bc90517c6c23")]
    public readonly InputSlot<float> RotLookupDistance = new();

    [Input(Guid = "a0982e6c-da72-4d7f-a562-1a6ff144db46")]
    public readonly InputSlot<bool> UseCurlNoise = new();

    [Input(Guid = "536948f3-f76c-418a-9b76-2c9fed4dee33")]
    public readonly InputSlot<bool> IsEnabled = new();
}