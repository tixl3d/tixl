using T3.Graphics.Compat;

namespace Types.Gfx;

[Guid("04858a08-f0fe-4536-9152-686659f0ab58")]
public sealed class DepthStencilState : Instance<DepthStencilState>
{
    [Output(Guid = "26E300CD-2DBE-49F2-AAB5-A60317DF5434")]
    public readonly Slot<T3.Graphics.Compat.DepthStencilState> DepthState = new();

    public DepthStencilState()
    {
        DepthState.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        DepthState.Value?.Dispose();

        try
        {
            var enableTest = EnableZTest.GetValue(context);
            var enableWrite = EnableZWrite.GetValue(context);
            var comparison = Comparison.GetValue(context);

            // Both D3D11 and Vulkan skip depth writes while the depth test is off, so writing without
            // testing keeps the test on and lets every fragment pass.
            var depthStencilStateDescription = new DepthStencilStateDescription()
                                                   {
                                                       IsDepthEnabled = enableTest || enableWrite,
                                                       DepthWriteMask = enableWrite ? DepthWriteMask.All : DepthWriteMask.Zero,
                                                       DepthComparison = enableTest ? comparison : T3.Graphics.Compat.Comparison.Always,
                                                   };

            DepthState.Value = new T3.Graphics.Compat.DepthStencilState(ResourceManager.Device, depthStencilStateDescription);
                
        }
        catch (Exception e)
        {
            Log.Error("Failed to create DepthStencilState " + e.Message);
        } 
    }

    [Input(Guid = "956B735B-C38A-4E8E-8186-CAF4D36D4D20")]
    public readonly InputSlot<bool> EnableZTest = new();
        
    [Input(Guid = "2342DF71-A162-4DB7-AFC3-514916239897")]
    public readonly InputSlot<bool> EnableZWrite = new();

         
    [Input(Guid = "27F1F703-7333-49E5-A024-4606E34E8427")]
    public readonly InputSlot<Comparison> Comparison = new(T3.Graphics.Compat.Comparison.Less);
        
}