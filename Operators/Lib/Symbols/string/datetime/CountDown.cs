using System.Globalization;

namespace Lib.@string.datetime;

[Guid("e4a38f3c-bd4c-406a-9979-bb683d79b39b")]
internal sealed class CountDown : Instance<CountDown>
{
    [Output(Guid = "511af1e0-9ada-46a2-8c14-0e18db506f95", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> Output = new();

    public CountDown()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var now = DateTime.Now;

        try
        {
            var targetTime = DateTime.Today;
            var launchTime = LaunchTime.GetValue(context);
            if(DateTime.TryParse(launchTime, out var d))
            {
                targetTime = d;
                //Log.Debug("date:" + d);
            }
            else
            {
                Log.Warning($"invalid format for lauchTime '{launchTime}'", this);
            }
            //var v = Duration.GetValue(context);
            var duration = DateTime.Now - targetTime;
                
                
            var format = Format.GetValue(context);
            var outString = duration.ToString(format, CultureInfo.InvariantCulture);
            Output.Value = outString;
        }
        catch (FormatException)
        {
            //Log.Warning("Failed to format CountDown time: " + e.Message, this);
            Output.Value = "Invalid Format";
            return;
        }
    }

    [Input(Guid = "61E5281B-B772-4BBD-B52F-E6B6310D259A")]
    public readonly InputSlot<string> LaunchTime = new();

    [Input(Guid = "d404e744-fa84-4bc8-8aa3-e6e972d9a5a7")]
    public readonly InputSlot<string> Format = new();
}