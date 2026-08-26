using T3.Core.Utils;

namespace Lib.@string.datetime;

[Guid("10acf0aa-2cb3-446b-b837-b6abe24d44da")]
internal sealed class DateTimeToFloat : Instance<DateTimeToFloat>
{
    [Output(Guid = "E07DC943-B32B-4037-9E84-0E50B4B67D05")]
    public readonly Slot<float> Output = new ();

    public DateTimeToFloat()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var dateTime = Value.GetValue(context);
        var offset = HourOffset.GetValue(context);

        var offsetSpan = new TimeSpan(0, (int)offset, (int)(offset*60 % 60), (int)(offset*60*60 % 60));
        dateTime -= offsetSpan;
        var value = OutputMapping.GetEnumValue<Modes>(context) switch
                        {
                            Modes.TimeOfDay_Hours      => (float)(dateTime.TimeOfDay.TotalMilliseconds / (double)(60 * 60 * 1000)),
                            Modes.TimeOfDay_Normalized => (float)(dateTime.TimeOfDay.TotalMilliseconds / (double)(24 * 60 * 60 * 1000)),
                            Modes.DayOfTheYear         => dateTime.DayOfYear,
                            Modes.DayOfTheMonths       => dateTime.Day,
                            _                          => 0f
                        };

        Output.Value = value;
    }

    [Input(Guid = "7b432357-225c-499e-9109-61168b4be4a7")]
    public readonly InputSlot<DateTime> Value = new();

    [Input(Guid = "5D9065A3-A124-4261-B5A9-25D969E30F73", MappedType = typeof(Modes))]
    public readonly InputSlot<int> OutputMapping = new();

    [Input(Guid = "EC1A2E0C-4C65-45E8-BFAC-FAE10CF3A717")]
    public readonly InputSlot<float> HourOffset = new();
        
    private enum Modes
    {
        TimeOfDay_Hours,
        TimeOfDay_Normalized,
        DayOfTheYear,
        DayOfTheMonths,
    }
}