namespace Examples.user.pixtur.learning.cs._05_physarum;

[Guid("4edc34ed-36f4-4f24-837f-4cc5696b2baa")]
public class _MovingAgents02 : Instance<_MovingAgents02>
{
    [Output(Guid = "fc65f025-f050-403d-9fd9-097a7cc676ca")]
    public readonly Slot<Texture2D> ImgOutput = new();

    [Input(Guid = "f333dbce-cc54-43ba-99b0-065426820f36")]
    public readonly InputSlot<float> RestoreLayout = new();

    [Input(Guid = "8ba49b49-8eee-461b-acd8-ba6bc21ba866")]
    public readonly InputSlot<bool> RestoreLayoutEnabled = new();

    [Input(Guid = "3b2e07ad-8218-4740-9e3e-17949ed5fca6")]
    public readonly InputSlot<bool> ShowAgents = new();

    [Input(Guid = "693957bc-5364-404d-84cd-0248fc609eca")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "351eb848-829d-48cb-96c3-16c00366d34f")]
    public readonly InputSlot<int> AgentCount = new();

    [Input(Guid = "2b19f72d-db37-4e02-a780-2bb82adb1b54")]
    public readonly InputSlot<int> ComputeSteps = new();

    [Input(Guid = "ae4938aa-43c2-443b-8ace-9dbd2045b448")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> BreedsBuffer = new();

    [Input(Guid = "9c6960f3-ac8d-4ab7-a34e-e24a6217b092")]
    public readonly InputSlot<System.Numerics.Vector4> DecayRatio = new();

    [Input(Guid = "ee393179-a8a6-4e62-979b-c906244ebb2e")]
    public readonly InputSlot<Texture2D> EffectTexture = new();

    [Input(Guid = "ad6beaae-4d4b-4f3b-ad7b-7d6389789610")]
    public readonly InputSlot<Int2> BlockCount = new();

    [Input(Guid = "fccbc208-b0ca-4eae-851d-fc5982a55616")]
    public readonly InputSlot<float> AngleLockSteps = new();

    [Input(Guid = "b3c90262-6b98-476b-a2a1-f8b3d00d9bb5")]
    public readonly InputSlot<float> AngleLockFactor = new();


}