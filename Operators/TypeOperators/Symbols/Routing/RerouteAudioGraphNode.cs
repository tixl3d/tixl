namespace Types.Routing;

[Guid("35367fa4-ef12-4e9a-b1f2-11f093108dd5")]
public sealed class RerouteAudioGraphNode : Instance<RerouteAudioGraphNode>, IRerouteNode
{
    [Output(Guid = "1e72fc91-ed9a-423d-8dbd-35cc69605158")]
    public readonly Slot<T3.Core.DataTypes.AudioGraphNode> Output = new();

    [Input(Guid = "083a30a4-4129-4f9a-a88c-698da67499ae")]
    public readonly InputSlot<T3.Core.DataTypes.AudioGraphNode> Input = new();
}
