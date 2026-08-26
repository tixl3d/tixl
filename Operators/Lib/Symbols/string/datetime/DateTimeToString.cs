using System.Globalization;

namespace Lib.@string.datetime;

[Guid("c1c3725a-0745-4ce1-874b-839810c2124c")]
internal sealed class DateTimeToString : Instance<DateTimeToString>
{
    [Output(Guid = "75ad6b31-2460-47fb-aa75-019e50e0fd44")]
    public readonly Slot<string> Output = new();

    public DateTimeToString()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var v = Value.GetValue(context);
        var format = Format.GetValue(context);
        try
        {
            Output.Value = string.IsNullOrEmpty(format) 
                               ? v.ToString(CultureInfo.InvariantCulture) 
                               : v.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            Output.Value = "Invalid Format";
        }
    }

    [Input(Guid = "C420E846-8BA2-4BE4-AD43-9F4380DA0851")]
    public readonly InputSlot<DateTime> Value = new();

    [Input(Guid = "5af4a05f-72dc-4c0d-a728-309bf3a1b1b9")]
    public readonly InputSlot<string> Format = new();
}