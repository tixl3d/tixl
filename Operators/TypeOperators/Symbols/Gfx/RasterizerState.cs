using SharpDX.Direct3D11;

namespace Types.Gfx;

[Guid("c7283335-ef57-46ad-9538-abbade65845a")]
public sealed class RasterizerState : Instance<RasterizerState>
{
    [Output(Guid = "B409694A-0980-4992-8525-01871B940CD7")]
    public readonly Slot<SharpDX.Direct3D11.RasterizerState> Value = new();

    public RasterizerState()
    {
        Value.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Value.Value?.Dispose();

        var fillMode = FillMode.GetValue(context) switch
                           {
                               (int)SharpDX.Direct3D11.FillMode.Solid     => SharpDX.Direct3D11.FillMode.Solid,
                               (int)SharpDX.Direct3D11.FillMode.Wireframe => SharpDX.Direct3D11.FillMode.Wireframe,
                               _                                          => SharpDX.Direct3D11.FillMode.Solid
                           };

        var rasterizerDesc = new RasterizerStateDescription()
                                 {
                                     CullMode = CullMode.GetValue(context),
                                     DepthBias = DepthBias.GetValue(context),
                                     DepthBiasClamp = DepthBiasClamp.GetValue(context),
                                     FillMode = fillMode,
                                     IsAntialiasedLineEnabled = AntialiasedLineEnabled.GetValue(context),
                                     IsDepthClipEnabled = DepthClipEnabled.GetValue(context),
                                     IsFrontCounterClockwise = FrontCounterClockwise.GetValue(context),
                                     IsMultisampleEnabled = MultiSampleEnabled.GetValue(context),
                                     IsScissorEnabled = ScissorEnabled.GetValue(context),
                                     SlopeScaledDepthBias = SlopeScaledDepthBias.GetValue(context)
                                 };


        try
        {
            Value.Value = new SharpDX.Direct3D11.RasterizerState(ResourceManager.Device, rasterizerDesc); // todo: put into resource manager 
        }
        catch(Exception e)
        {
            Log.Error("Failed to create rasterizer state: " + e.Message);
        }
    }

        

    [Input(Guid = "03F3BC7F-3949-4A97-88CF-04E162CFA2F7")]
    public readonly InputSlot<CullMode> CullMode = new();
    [Input(Guid = "A2193AA0-E217-4248-A8DC-360CB89A613B")]
    public readonly InputSlot<int> DepthBias = new();
        
    [Input(Guid = "2B53507E-24C3-4895-8928-3400C6ACCCB6")]
    public readonly InputSlot<float> DepthBiasClamp = new();
        
    [Input(Guid = "78C9B432-A2B8-4AEA-81E4-0CD5086B3B94", MappedType = typeof(FillMode))]
    public readonly InputSlot<int> FillMode = new();
        
    [Input(Guid = "EEB75A91-2402-44BE-BB9D-B620E34085ED")]
    public readonly InputSlot<bool> AntialiasedLineEnabled = new();
    [Input(Guid = "33D5BCFA-996A-4019-9E80-D15B72EA9D4C")]
    public readonly InputSlot<bool> DepthClipEnabled = new();
    [Input(Guid = "31319FB4-8663-4908-95B8-E5D5A95F15B2")]
    public readonly InputSlot<bool> FrontCounterClockwise = new();
    [Input(Guid = "A6DCBF5C-7096-4023-878C-70495AD76F83")]
    public readonly InputSlot<bool> MultiSampleEnabled = new();
    [Input(Guid = "DFCA315F-85DE-439A-A0B4-30FDF8DA050E")]
    public readonly InputSlot<bool> ScissorEnabled = new();
    [Input(Guid = "03C80C25-B0B1-45C2-B67B-60906FE47FBE")]
    public readonly InputSlot<float> SlopeScaledDepthBias = new();
}