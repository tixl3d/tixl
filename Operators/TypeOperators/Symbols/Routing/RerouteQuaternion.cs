namespace Types.Routing;

[Guid("2d54c862-9e4b-4068-8381-ea793a1a0cce")]
public sealed class RerouteQuaternion : Instance<RerouteQuaternion>, IRerouteNode
{
    [Output(Guid = "e64874ab-0382-473f-8dd2-1f9716c0563c")]
    public readonly Slot<System.Numerics.Quaternion> Output = new();

    [Input(Guid = "7fa06341-7189-4881-ad08-213f0e3ead89")]
    public readonly InputSlot<System.Numerics.Quaternion> Input = new();
}
