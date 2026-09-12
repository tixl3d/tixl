namespace Types.Routing;

// Passes SharpDX.Direct3D11.RenderTargetBlendDescription through the .t3 connection; no C# evaluator is needed.
[Guid("d1ab44fe-eb4a-4e0c-bb52-2c2382f34f91")]
public sealed class RerouteRenderTargetBlendDescription : Instance<RerouteRenderTargetBlendDescription>, IRerouteNode
{
    [Output(Guid = "42e1ef3e-ca7b-43fd-adc9-78eaec12b915")]
    public readonly Slot<SharpDX.Direct3D11.RenderTargetBlendDescription> Output = new();

    [Input(Guid = "e5094f5d-efeb-41ed-93c5-dafabc5e37a0")]
    public readonly InputSlot<SharpDX.Direct3D11.RenderTargetBlendDescription> Input = new();
}
