namespace Lib.point.sim.experimental;

[Guid("98df563e-fd59-4458-a490-b7b7604ec1f3")]
internal sealed class GrowStrains : Instance<GrowStrains>
{

    [Output(Guid = "e7303155-e0c6-4b0c-91a9-1b3f944c77e7")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "b5849477-6a5a-47cd-8be2-684e95816559")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "08b98003-d96d-41e0-a88e-6912da407811")]
    public readonly InputSlot<BufferWithViews> GTargets = new();

    [Input(Guid = "6c39d992-67fb-4f89-a7de-a5e38e4a68fe")]
    public readonly InputSlot<float> Variation = new();

    [Input(Guid = "ee9cd2af-3f3e-473c-8641-3b562fb6fbdc")]
    public readonly InputSlot<float> NoiseAmount = new();

    [Input(Guid = "5f17571b-f16a-47af-ab64-e4195927d1e7")]
    public readonly InputSlot<float> Frequency = new();

    [Input(Guid = "0a547614-3de1-4dc7-9a39-7258235b64b4")]
    public readonly InputSlot<float> NoisePhase = new();

    [Input(Guid = "6977957b-97fa-413f-8867-3dba8868792f")]
    public readonly InputSlot<Vector3> NoiseDistribution = new();

    [Input(Guid = "8350ff91-6e7c-4968-85b7-d39bc76558a6")]
    public readonly InputSlot<float> NoiseRotationLookUp = new();

    [Input(Guid = "46d87bdf-a797-4a8c-940b-fb034d487a3b")]
    public readonly InputSlot<float> Length = new();

    [Input(Guid = "75e005ac-9e28-4558-9d8b-94be4c8056d6")]
    public readonly InputSlot<float> Width = new();

    [Input(Guid = "da72a065-14db-481e-b676-767b9074d24f")]
    public readonly InputSlot<float> NoiseDensity = new();
}