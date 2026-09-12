namespace Types.Routing;

// Passes SharpDX.Direct3D11.RenderTargetView through the .t3 connection; no C# evaluator is needed.
[Guid("13f07889-a261-496c-9631-6d91b4b96541")]
public sealed class RerouteRenderTargetView : Instance<RerouteRenderTargetView>, IRerouteNode
{
    [Output(Guid = "a0fc634f-1ab8-40d1-9d18-4c46860359d0")]
    public readonly Slot<SharpDX.Direct3D11.RenderTargetView> Output = new();

    [Input(Guid = "48108b7e-66d6-41f5-a763-82dbf02e20b8")]
    public readonly InputSlot<SharpDX.Direct3D11.RenderTargetView> Input = new();
}
