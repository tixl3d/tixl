using System.Globalization;

namespace Examples.user.fuzzy.midi;

[Guid("dcd44623-3cbc-4cf4-b5b9-00ea0f884a22")]
internal sealed class RGBToSysEx : Instance<RGBToSysEx>
{
    [Output(Guid = "0680af36-2947-48e3-9bfe-2c8479ce174c")]
    public readonly Slot<List<string>> Output = new();

    public RGBToSysEx()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var values = Value.GetValue(context);
        // Expecting a flattened list of 3 values vectors (hence % 3 != 0)
        if (values == null || values.Count == 0 || values.Count % 3 != 0)
        {
            Output.Value = [];
            return;
        }

        var res = new List<string>();
        for (int i = 0; i < values.Count / 3; i++)
        {
            StringBuilder sb = new StringBuilder("");
            for (int j = 0; j < 3; j++)
            {
                int index = i * 3 + j;
                sb.Append(Math.Min(255, (int)Math.Round(values[index])).ToString("X2") + " ");
            }
            res.Add(sb.ToString().Trim());
        }
        Output.Value = res;
    }

    [Input(Guid = "0bc553ea-f217-4cc8-8ca9-68d0e8db3f94")]
    public readonly InputSlot<List<float>> Value = new();

}