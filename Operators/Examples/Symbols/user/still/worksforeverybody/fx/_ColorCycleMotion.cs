namespace Examples.user.still.worksforeverybody.fx;

[Guid("a5ec9aa9-73fc-44f9-8530-e62afc0b112d")]
internal sealed class _ColorCycleMotion : Instance<_ColorCycleMotion>
{
    [Output(Guid = "67d69f50-2f46-4ef3-a6f1-825330759322")]
    public readonly Slot<Command> Result = new();

    [Output(Guid = "11a8a9be-ea04-4c8a-98df-41badc753370")]
    public readonly Slot<Texture2D> TextureOutput = new();


    [Input(Guid = "3ad5ed8e-947c-4c47-9e97-407017fa4b46")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "41b2c303-8be9-4f43-aa17-2f4e81789a8e")]
    public readonly InputSlot<Object> CameraReference = new();

    [Input(Guid = "4ed29c39-849f-44dd-9386-7c1814cdbfa8")]
    public readonly InputSlot<float> TimeOverride = new();

    [Input(Guid = "d50fd176-6f79-438d-84d5-a48d06c73b0c")]
    public readonly InputSlot<float> CycleSpeed = new();

    [Input(Guid = "00176885-28e6-4c93-b7b5-477ab4b8b239")]
    public readonly InputSlot<float> ColorPhaseSpread = new();

    [Input(Guid = "c709b479-cc5c-4829-a717-2b5a4e7700cd")]
    public readonly InputSlot<System.Numerics.Vector3> FakeTranslate = new();

    [Input(Guid = "2bcecacc-bf19-4c26-ad70-62e90282f545")]
    public readonly InputSlot<System.Numerics.Vector3> FakeRotate = new();

    [Input(Guid = "0f15aa0d-1e18-4407-9184-eb21e78cb7eb")]
    public readonly InputSlot<float> FakeAmount = new();

    [Input(Guid = "f6566a76-f545-4877-8660-49e471e7c9d3")]
    public readonly InputSlot<float> TimeSpread = new();

    [Input(Guid = "10e1a3e8-8f18-44f4-8efc-dec2a6bfa62e")]
    public readonly InputSlot<float> ZOffsetCenterPass = new();

    [Input(Guid = "e4280a81-15f6-42cd-9bbf-4cab66a15c17")]
    public readonly InputSlot<System.Numerics.Vector4> FadeOutColor = new();

}