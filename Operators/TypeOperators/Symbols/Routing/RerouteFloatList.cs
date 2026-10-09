namespace Types.Routing;

[Guid("b86b65d9-db1e-46d8-88a9-2bb010835929")]
public sealed class RerouteFloatList : Instance<RerouteFloatList>, IRerouteNode
{
    [Output(Guid = "cb2025c2-3b0c-4744-a55f-748abbe3e1bb")]
    public readonly Slot<System.Collections.Generic.List<float>> Output = new();

    [Input(Guid = "fea94872-a42b-4afd-9d27-2ce0477b71a3")]
    public readonly InputSlot<System.Collections.Generic.List<float>> Input = new();
}
