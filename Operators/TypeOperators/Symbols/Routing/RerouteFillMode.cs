namespace Types.Routing;

// Passes SharpDX.Direct3D11.FillMode through the .t3 connection; no C# evaluator is needed.
[Guid("fcb87682-a90f-4ef2-8928-e32187ea6e92")]
public sealed class RerouteFillMode : Instance<RerouteFillMode>, IRerouteNode
{
    [Output(Guid = "ff2b53c4-6f70-438e-8574-a487de351683")]
    public readonly Slot<SharpDX.Direct3D11.FillMode> Output = new();

    [Input(Guid = "cd59abbe-7a5a-4496-ba92-db22f2671e5a")]
    public readonly InputSlot<SharpDX.Direct3D11.FillMode> Input = new();
}
