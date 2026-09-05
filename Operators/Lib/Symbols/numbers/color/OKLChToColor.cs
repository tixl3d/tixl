using T3.Core.Utils;

namespace Lib.numbers.color;

[Guid("7547a0f5-eee2-474e-9000-ae81757f624e")]
internal sealed class OKLChToColor : Instance<OKLChToColor>
{
    [Output(Guid = "c836862e-7edd-4624-b4a4-e54e06ca8d5c")]
    public readonly Slot<Vector4> Color = new();

    public OKLChToColor()
    {
        Color.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var boost = IntensityBoost.GetValue(context);
        
        Color.Value = OkLab.FromOkLCh(Brightness.GetValue(context), 
                                      Saturation.GetValue(context), 
                                      (Hue.GetValue(context) % 1) *360f, 
                                      Alpha.GetValue(context),
                                      UseGamma.GetValue(context)) 
                      * new Vector4(boost,boost,boost,1);
    }

    [Input(Guid = "72507abd-979d-412f-87a1-764d2ca1a70e")]
    public readonly InputSlot<float> Hue = new();

    [Input(Guid = "9559b8ad-cb36-46d8-b88e-d1363be133e0")]
    public readonly InputSlot<float> Saturation = new();

    [Input(Guid = "0ccf0379-a8f3-439c-a269-918bbfd715b7")]
    public readonly InputSlot<float> Brightness = new();
        
    [Input(Guid = "dec03f8a-e363-459d-ac55-0cd3914e660b")]
    public readonly InputSlot<float> Alpha = new(1f);
    
    [Input(Guid = "67BB3837-3CC3-4BF1-95B8-1A4FFD464FB6")]
    public readonly InputSlot<bool> UseGamma = new();
    
    [Input(Guid = "4D6A10A7-0C48-4472-AB4B-ADB8823B0337")]
    public readonly InputSlot<float> IntensityBoost = new();
}