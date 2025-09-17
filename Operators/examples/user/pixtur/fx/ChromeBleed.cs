namespace Examples.user.pixtur.fx;

[Guid("007b7ad9-e8f2-4830-be0f-d3f5f3444683")]
public class ChromeBleed : Instance<ChromeBleed>
{
    [Output(Guid = "5ff09d7b-4f78-4230-8ca7-4d3da4606e3f")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "7504facd-2800-4d23-a86a-7b97ccc54529")]
    public readonly MultiInputSlot<Command> Command = new();

    [Input(Guid = "29579dbe-bc75-4be9-b268-845ec0cd389d")]
    public readonly InputSlot<System.Numerics.Vector4> Fill = new();

    [Input(Guid = "2b115c7f-9ced-43a1-b46c-46495ca262ff")]
    public readonly InputSlot<float> BlurRadius = new();

    [Input(Guid = "f8072fb7-9769-4fd1-8159-4f3d06b27994")]
    public readonly InputSlot<System.Numerics.Vector4> BackColor = new();

    [Input(Guid = "8d09202e-e73c-4998-9fe6-3d0211531bd3")]
    public readonly InputSlot<bool> OverlaySeed = new();

}