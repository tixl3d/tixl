using System.Globalization;

namespace Lib.@string.convert;

[Guid("39c96cfd-dedf-4f76-a471-d1c26c9ba9fa")]
internal sealed class FloatToString : Instance<FloatToString>
{
    [Output(Guid = "{C63A1977-A594-490D-B5FB-DE4D40BAD016}")]
    public readonly Slot<string> Output = new();

    public FloatToString()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var s = Format.GetValue(context);
        try
        {
            Output.Value = string.IsNullOrEmpty(s) ? v.ToString(CultureInfo.InvariantCulture) : string.Format(CultureInfo.InvariantCulture, s, v);
        }
        catch (FormatException)
        {
            Output.Value = "Invalid Format";
        }
    }

    [Input(Guid = "{F36E4078-2608-4308-AB5F-077C05B1181A}")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "{B2B32C44-62D8-4ACB-A9A7-4856EC7A33BB}")]
    public readonly InputSlot<string> Format = new();
}