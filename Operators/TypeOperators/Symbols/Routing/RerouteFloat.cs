namespace Types.Routing;

[Guid("f41e72fb-82ec-4381-b835-8007f2ce6894")]
public sealed class RerouteFloat : Instance<RerouteFloat>, IRerouteNode
{
    [Output(Guid = "82aec913-d773-4f5c-8b6c-05cbef19d2df")]
    public readonly Slot<float> Output = new();

    [Input(Guid = "9cbfb7e5-8d56-4132-b34e-f105633f90d7")]
    public readonly InputSlot<float> Input = new();
}
