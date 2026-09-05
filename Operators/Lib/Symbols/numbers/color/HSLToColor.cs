namespace Lib.numbers.color;

[Guid("1677fd74-6e54-479a-b478-c2ac77288f9c")]
internal sealed class HSLToColor : Instance<HSLToColor>
{
    [Output(Guid = "A73DC8D3-ECC5-454A-999A-1C79442FF2E2")]
    public readonly Slot<Vector4> Color = new();

    public HSLToColor()
    {
        Color.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var hue = (Hue.GetValue(context) % 1) *360f; // + Saturation.GetValue(context);
            
        var sat = Saturation.GetValue(context);// + Saturation.GetValue(context);
        var brightness = Lightness.GetValue(context);// + Saturation.GetValue(context);

        float fSatR = 1;
        float fSatG = 1;
        float fSatB = 1;
        if (hue < 120.0f)
        {
            fSatR = (120.0f - hue) / 60.0f;
            fSatG = hue / 60.0f;
            fSatB = 0.0f;
        }
        else if (hue < 240.0f)
        {
            fSatR = 0.0f;
            fSatG = (240.0f - hue) / 60.0f;
            fSatB = (hue - 120.0f) / 60.0f;
        }
        else
        {
            fSatR = (hue - 240.0f) / 60.0f;
            fSatG = 0.0f;
            fSatB = (360.0f - hue) / 60.0f;
        }

        fSatR = (fSatR < 1.0f) ? fSatR : 1.0f;
        fSatG = (fSatG < 1.0f) ? fSatG : 1.0f;
        fSatB = (fSatB < 1.0f) ? fSatB : 1.0f;

        var fTmpR = 2.0f * sat * fSatR + (1.0f - sat);
        var fTmpG = 2.0f * sat * fSatG + (1.0f - sat);
        var fTmpB = 2.0f * sat * fSatB + (1.0f - sat);

        float fR, fG, fB;
        if (brightness < 0.5f)
        {
            fR = brightness * fTmpR;
            fG = brightness * fTmpG;
            fB = brightness * fTmpB;
        }
        else
        {
            fR = (1.0f - brightness) * fTmpR + 2.0f * brightness - 1.0f;
            fG = (1.0f - brightness) * fTmpG + 2.0f * brightness - 1.0f;
            fB = (1.0f - brightness) * fTmpB + 2.0f * brightness - 1.0f;
        }
        //
        // Color.Value.X = brightness;
        // Color.Value.Y = fG;
        // Color.Value.Z = fB;
        Color.Value = new Vector4(fR, fG, fB, Alpha.GetValue(context));   
        //Color.Value = new System.Numerics.Vector4(hue, sat, brightness, Alpha.GetValue(context));
    }

    [Input(Guid = "2c1adb2b-36bd-4ca0-8fbb-e7571163a98d")]
    public readonly InputSlot<float> Hue = new();

    [Input(Guid = "9ee22450-f03f-40dd-8090-ce24d9bd04ed")]
    public readonly InputSlot<float> Saturation = new();

    [Input(Guid = "115E3D92-2E91-447D-BA81-18A508D3D36A")]
    public readonly InputSlot<float> Lightness = new();
        
    [Input(Guid = "FB8C2263-804D-4204-80AA-BA21BBEFDD8E")]
    public readonly InputSlot<float> Alpha = new(1f);
}