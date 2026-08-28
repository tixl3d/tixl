namespace Lib.point.sim;

[Guid("9c378944-9a57-4ae4-a88e-36c07244bcf7")]
internal sealed class SimForceOffset : Instance<SimForceOffset>
{

    [Output(Guid = "d41c5cd6-1902-4fb9-9639-6513906cef79")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Input(Guid = "2c31a936-3b5a-4c85-ad9d-7a575453bb0d")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "cc7cf8dc-13f4-4442-94b5-2c7cda64776c")]
    public readonly InputSlot<Vector3> Center = new();

    [Input(Guid = "2cce4aac-2d41-4c0d-a046-9a5529b912b1")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "4267c75c-35a6-4feb-9a98-92c3d676dcae")]
    public readonly InputSlot<float> RadiusFallOff = new();

    [Input(Guid = "b30b9a6e-1407-48e8-9eaa-955250f289e7")]
    public readonly InputSlot<float> RadialForce = new();

    [Input(Guid = "e68e305e-9e2b-4bf2-9a85-4630697a8c34")]
    public readonly InputSlot<float> UseWForMass = new();

    [Input(Guid = "40378b36-ab0f-4792-be80-d2354496f0a6")]
    public readonly InputSlot<float> Variation = new();

    [Input(Guid = "ce7b6d6d-9b0e-4c4e-99da-3c5af57e35cd")]
    public readonly InputSlot<Vector3> Gravity = new();

    [Input(Guid = "f3d5f8b5-c882-4098-afcb-0f86f5cd7964")]
    public readonly InputSlot<float> ForceDecayRate = new();

    [Input(Guid = "e6b12dd3-fd25-4b4c-a3bb-bd4da9c1c1e2")]
    public readonly InputSlot<bool> IsEnabled = new();
}