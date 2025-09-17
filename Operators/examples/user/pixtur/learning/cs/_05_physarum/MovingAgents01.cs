namespace Examples.user.pixtur.learning.cs._05_physarum;

[Guid("0eff6d8b-0fca-420d-839c-3e90dff76cb8")]
public class MovingAgents01 : Instance<MovingAgents01>
{
    [Output(Guid = "93a2479a-e42d-4411-94c4-5280dc8d12fc")]
    public readonly Slot<Texture2D> ImgOutput = new();

    [Input(Guid = "f164e165-6ea1-4ca6-9604-80f240d721a4")]
    public readonly InputSlot<float> Spin = new();

    [Input(Guid = "84bf834b-e21c-479b-9544-44a887af8981")]
    public readonly InputSlot<float> TrailEngery = new();

    [Input(Guid = "91d53f63-6ead-4836-a456-8e5f5e0b255c")]
    public readonly InputSlot<float> SampleDistance = new();

    [Input(Guid = "abf905a0-bbaa-4e70-9db6-7f651482d058")]
    public readonly InputSlot<float> SampleAngle = new();

    [Input(Guid = "5cfff9b5-3638-4294-a921-2aabad7dd938")]
    public readonly InputSlot<float> MoveDistance = new();

    [Input(Guid = "0b688684-111d-47c9-8013-3b4b59322225")]
    public readonly InputSlot<float> RotateAngle = new();

    [Input(Guid = "55282d99-2f00-45e8-8529-9ac489476090")]
    public readonly InputSlot<float> ReferenceEnergy = new();

    [Input(Guid = "7b5ceb4c-0596-4acb-bcbe-592ef8c83225")]
    public readonly InputSlot<float> DecayRatio = new();

    [Input(Guid = "38b646b3-1379-4436-bba0-2e51f317cdf5")]
    public readonly InputSlot<float> RestoreLayout = new();

    [Input(Guid = "a104be9a-ffc3-43f3-8451-ef2064f728db")]
    public readonly InputSlot<bool> RestoreLayoutEnabled = new();

    [Input(Guid = "ffd6b714-ae1d-48c4-957c-95a0063893c1")]
    public readonly InputSlot<bool> ShowAgents = new();

    [Input(Guid = "e07183fd-9813-4f98-bc7a-5e82775a880b")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "1cc5ba51-e2a6-4743-805c-163237a9f4f1")]
    public readonly InputSlot<int> AgentCount = new();

    [Input(Guid = "acd0d26e-cadb-4c62-ac7a-aece6b3bb9e5")]
    public readonly InputSlot<float> SnapToAnglesCount = new();

    [Input(Guid = "2f57d137-4ba4-4a92-8d92-38d70f2df4a7")]
    public readonly InputSlot<float> SnapToAnglesAmount = new();

    [Input(Guid = "6be28291-4420-481a-a338-e74b431db011")]
    public readonly InputSlot<int> ComputeSteps = new();

    [Input(Guid = "a326bc60-e383-4de5-8e64-c08508002116")]
    public readonly InputSlot<float> BRatio = new();

    [Input(Guid = "dd6729d5-2625-457b-8e00-21da06c5e5c0")]
    public readonly InputSlot<float> BTrail = new();

    [Input(Guid = "718ffca4-0d50-4bd1-8502-344627a73f30")]
    public readonly InputSlot<float> BMoveDistance = new();

    [Input(Guid = "fc87c40a-489c-4838-b9cb-72922ab99608")]
    public readonly InputSlot<float> BRotate = new();


}