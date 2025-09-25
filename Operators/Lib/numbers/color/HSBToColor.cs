namespace Lib.numbers.color;

[Guid("13b08a56-0af1-473c-8dbc-20eccbcbd372")]
internal sealed class HSBToColor : Instance<HSBToColor>
{
    [Output(Guid = "2f1335f8-eddd-44ac-94ee-ab0f3b8efa80")]
    public readonly Slot<Vector4> Color = new();

    public HSBToColor()
    {
        Color.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var hue = (Hue.GetValue(context) % 360f);
        var saturation = Saturation.GetValue(context);
        var brightness = Brightness.GetValue(context);

        // HSB to RGB conversion
        float r = 0, g = 0, b = 0;

        if (saturation == 0)
        {
            // Grayscale
            r = g = b = brightness;
        }
        else
        {
            // Normalize hue to [0, 360)
            hue %= 360f;
            if (hue < 0) hue += 360f;

            // Calculate sector (0-5)
            int sector = (int)(hue / 60f);
            float fractional = (hue / 60f) - sector;

            float p = brightness * (1 - saturation);
            float q = brightness * (1 - saturation * fractional);
            float t = brightness * (1 - saturation * (1 - fractional));

            switch (sector)
            {
                case 0: // 0° - 60°
                    r = brightness;
                    g = t;
                    b = p;
                    break;
                case 1: // 60° - 120°
                    r = q;
                    g = brightness;
                    b = p;
                    break;
                case 2: // 120° - 180°
                    r = p;
                    g = brightness;
                    b = t;
                    break;
                case 3: // 180° - 240°
                    r = p;
                    g = q;
                    b = brightness;
                    break;
                case 4: // 240° - 300°
                    r = t;
                    g = p;
                    b = brightness;
                    break;
                case 5: // 300° - 360°
                    r = brightness;
                    g = p;
                    b = q;
                    break;
            }
        }

        Color.Value = new Vector4(r, g, b, Alpha.GetValue(context));
    }

    [Input(Guid = "fe2ea451-3363-4de0-9e29-996b5c076e41")]
    public readonly InputSlot<float> Hue = new();

    [Input(Guid = "bf1e8706-8eed-4aa8-95bc-062b8db64d53")]
    public readonly InputSlot<float> Saturation = new();

    [Input(Guid = "6bd015a4-d14d-4963-9698-bbad15bd668b")]
    public readonly InputSlot<float> Brightness = new();

    [Input(Guid = "436d7556-2d45-4ed3-ad45-657d5b9fa7f4")]
    public readonly InputSlot<float> Alpha = new();
}