namespace Lib.@string.datetime;

[Guid("bd8d684c-96ae-4864-84fd-ca87f98ce1a4")]
internal sealed class NowAsDateTime : Instance<NowAsDateTime>
{
    [Output(Guid = "99f94d1c-7d79-497d-9d42-dff8b749e493", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<DateTime> Output = new();

    public NowAsDateTime()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (!Freeze.GetValue(context))
        {
            _lastValue = DateTime.Now;
        }
        Output.Value = _lastValue;
    }

    private DateTime _lastValue;

    [Input(Guid = "a3f7c291-4e8d-4b19-b62e-8c5d1f0a9e74")]
    public readonly InputSlot<bool> Freeze = new();
}