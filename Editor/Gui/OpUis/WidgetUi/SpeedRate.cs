namespace T3.Editor.Gui.OpUis.WidgetUi;

public struct SpeedRate
{
    private SpeedRate(float f, string label)
    {
        Factor = f;
        Label = label;
    }

    public float Factor;
    public string Label;

    public static readonly SpeedRate[] RelevantRates =
        {
            new (-1, "Ignore"),
            new (0.0f, "OFF"),
            new (1/32f, "1/16"),
            new (1/16f, "1/16"),
            new (0.125f, "1/8"),
            new (0.25f, "1/4"),
            new (0.5f, "1/2"),
            new (1, "1×"),
            new (4, "4×"),
            new (8, "8×"),
            new (16, "16×"),
            new (32, "32×"),
            new (100, "100×"),
            new (360, "360×"),
        };

    public static int FindClosestRateIndex(float rate)
    {
        for (var index = 0; index < SpeedRate.RelevantRates.Length; index++)
        {
            if (Math.Abs(RelevantRates[index].Factor - rate) < 0.01f)
                return index;
        }

        return -1;
    }
}