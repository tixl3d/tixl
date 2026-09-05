namespace Lib.numbers.anim.time;

[Guid("0bee3ec6-7e0d-471b-9ef7-ab9c22c6883b")]
internal sealed class DateTimeInSecs : Instance<DateTimeInSecs>
{
    [Output(Guid = "111e179b-8c9e-4084-91ec-dd0d02eb3973", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Result = new();

    public DateTimeInSecs()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (!Freeze.GetValue(context))
        {
            var xxx = DateTimeOffset.Now.ToUnixTimeSeconds();
            //Log.Debug("secs:" + xxx, this);
            _lastValue = (int)xxx;
        }
            
        Result.Value = _lastValue;
    }

    private int _lastValue;

    [Input(Guid = "d81a3ecb-5718-48a9-9a50-85820e18957c")]
    public readonly InputSlot<bool> Freeze = new();
}