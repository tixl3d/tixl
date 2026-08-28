namespace Lib.image.fx.feedback;

[Guid("d1a1f207-0537-416a-985b-e350c3f7e655")]
internal sealed class SimpleLiquid2 : Instance<SimpleLiquid2>
{
    [Output(Guid = "08692782-19d4-49fe-94e7-1209500ed1d8")]
    public readonly Slot<Texture2D> ColorBuffer = new();

    [Input(Guid = "5e6de9ce-c291-40fc-bbe4-cd74ebbe1434")]
    public readonly InputSlot<bool> TriggerReset = new();

    [Input(Guid = "0f3dd599-095e-4766-9483-c8cb21f9571a")]
    public readonly InputSlot<Vector2> Gravity = new();

    [Input(Guid = "faf738e5-5ff4-4bcd-85dc-b1c1484edfa6")]
    public readonly InputSlot<float> BorderStrength = new();

    [Input(Guid = "225df965-a92b-45d8-abae-81238aada793")]
    public readonly InputSlot<float> MassAttraction = new();

    [Input(Guid = "91d7a9e8-dc0e-4c77-8496-90ac7c4dfe0c")]
    public readonly InputSlot<float> ApplyFxTexture = new();

    [Input(Guid = "83b4782b-9ac3-4426-b7d9-1669e97c89b1")]
    public readonly InputSlot<float> FX_RG_Velocity = new();

    [Input(Guid = "c07bdf4d-8395-4327-b57e-6ba38f36e8a0")]
    public readonly InputSlot<float> SpeedFactor = new();

    [Input(Guid = "fcf28205-bcc4-46f3-af4c-1a3896a991a5")]
    public readonly InputSlot<float> FX_B_AddRemoveMass = new();

    [Input(Guid = "18d0f554-e8f7-4b0f-98c7-24c9742b66af")]
    public readonly InputSlot<float> StabilizeFactor = new();

    [Input(Guid = "128d4d4e-7dc9-466b-9e9d-e572d2cd0b5e")]
    public readonly InputSlot<float> ResetFillFactor = new();

    [Input(Guid = "f9351dc1-ccbd-44f2-9872-c1a0245bb1b0")]
    public readonly InputSlot<float> MouseClick_Force = new();

    [Input(Guid = "17a4765b-7032-4737-86c2-647b7bd4bedf")]
    public readonly InputSlot<float> OnClick_AddRemoveMass = new();

    [Input(Guid = "b5e1cded-fded-4769-8d1e-38598cf944db")]
    public readonly InputSlot<Texture2D> FxTexture = new();

    [Input(Guid = "b4d844b5-5d29-4c2d-a327-e1b9eb7411d8")]
    public readonly InputSlot<int> Iterations = new();

}