using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;

namespace Lib.render._dx11.api;

[Guid("e3596381-c118-4e2e-a482-83049a9f74af")]
internal sealed class ClearRenderTarget : Instance<ClearRenderTarget>
{
    [Output(Guid = "A6C06F65-1738-4DD0-9D0F-728864FF521B", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    public ClearRenderTarget()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        // deviceContext.Draw2(VertexCount.GetValue(context), VertexStartLocation.GetValue(context));
        var rtv = RenderTarget.GetValue(context);
        if (rtv != null)
        {
            var c = ClearColor.GetValue(context);
            deviceContext.ClearRenderTargetView(rtv, new RawColor4(c.X, c.Y, c.Z, c.W));
        }
        
        var dsv = DepthStencilView.GetValue(context);
        if (dsv != null)
        {
            deviceContext.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1.0f, 0);
        }
    }

    [Input(Guid = "D73D2FE7-1AF4-48D6-9AD3-F8C87C3369D6")]
    public readonly InputSlot<Vector4> ClearColor = new();
    
    [Input(Guid = "25C0C15C-5B95-4FE3-8D59-4E127FCE1CF2")]
    public readonly InputSlot<RenderTargetView> RenderTarget = new();
    
    [Input(Guid = "65077B57-F9EB-48AA-8195-588F906B0E72")]
    public readonly InputSlot<SharpDX.Direct3D11.DepthStencilView> DepthStencilView = new();
}