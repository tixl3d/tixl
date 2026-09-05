using SharpDX;
using SharpDX.Direct3D11;

namespace Types.Gfx;

[Guid("04858a08-f0fe-4536-9152-686659f0ab58")]
public sealed class DepthStencilState : Instance<DepthStencilState>
{
    [Output(Guid = "26E300CD-2DBE-49F2-AAB5-A60317DF5434")]
    public readonly Slot<SharpDX.Direct3D11.DepthStencilState> DepthState = new();

    public DepthStencilState()
    {
        DepthState.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        DepthState.Value?.Dispose();

        try
        {
            var depthStencilStateDescription = new DepthStencilStateDescription()
                                                   {
                                                       IsDepthEnabled = EnableZTest.GetValue(context),
                                                       DepthWriteMask = EnableZWrite.GetValue(context) ?  DepthWriteMask.All : DepthWriteMask.Zero,
                                                       DepthComparison = Comparison.GetValue(context),
                                                   };
                
            DepthState.Value = new SharpDX.Direct3D11.DepthStencilState(ResourceManager.Device, depthStencilStateDescription);
                
        }
        catch (SharpDXException e)
        {
            Log.Error("Failed to create DepthStencilState " + e.Message);
        } 
    }

    [Input(Guid = "956B735B-C38A-4E8E-8186-CAF4D36D4D20")]
    public readonly InputSlot<bool> EnableZTest = new();
        
    [Input(Guid = "2342DF71-A162-4DB7-AFC3-514916239897")]
    public readonly InputSlot<bool> EnableZWrite = new();

         
    [Input(Guid = "27F1F703-7333-49E5-A024-4606E34E8427")]
    public readonly InputSlot<Comparison> Comparison = new(SharpDX.Direct3D11.Comparison.Less);
        
}