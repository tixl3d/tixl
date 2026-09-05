namespace Lib.point.sim._legacy;

[Guid("dc3d1571-ad9f-46aa-bed9-df2f4e1c7040")]
internal sealed class LegacyParticleSimulation : Instance<LegacyParticleSimulation>
{

    [Output(Guid = "fd2f84af-0925-418e-b3fa-edec6fa19df3")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "088f9a81-7170-4f9d-bbfa-f08b0bf32317")]
    public readonly InputSlot<BufferWithViews> EmitPoints = new();

    [Input(Guid = "5525b00a-eea5-46ed-b4b4-cbcadcee3820")]
    public readonly InputSlot<bool> Emit = new();

    [Input(Guid = "18903940-ff20-4b64-a4f0-6078977edd7a")]
    public readonly InputSlot<int> MaxParticleCount = new();

    [Input(Guid = "ec99463b-c3e9-4eef-9321-3840d5d323b6")]
    public readonly InputSlot<bool> UseAging = new();

    [Input(Guid = "a03ffef9-11e3-41f9-9f13-71f107b484df")]
    public readonly InputSlot<float> AgingRate = new();

    [Input(Guid = "0f84199d-76f0-4155-b5b0-f6d05260423a")]
    public readonly InputSlot<float> MaxAge = new();

    [Input(Guid = "fc415c01-4293-47b0-bd9c-a5ba499b074e")]
    public readonly InputSlot<bool> ClampAtMaxAge = new();

    [Input(Guid = "267b6cae-2c3d-4874-9532-ca3da138fde6")]
    public readonly InputSlot<bool> Reset = new();

    [Input(Guid = "ae7aa205-faa0-454b-9a82-0067410275a0")]
    public readonly InputSlot<bool> Freeze = new();

    [Input(Guid = "889c7ee2-e996-412c-827f-2632056b6d9a")]
    public readonly InputSlot<bool> ApplyMovement = new();

    [Input(Guid = "3e6ff5e3-56a8-4be0-a918-ef041828e95f")]
    public readonly InputSlot<float> Speed = new();

    [Input(Guid = "79f17c7d-7ffe-43df-af17-36e97ab3813f")]
    public readonly InputSlot<float> Drag = new();

    [Input(Guid = "998e0875-ddad-49f0-b9cc-1ae5017a4bb6")]
    public readonly InputSlot<bool> SetInitialVelocity = new();

    [Input(Guid = "22670413-93a6-4743-8ef3-962a975410de")]
    public readonly InputSlot<float> InitialVelocity = new();
}