namespace Lib.numbers.anim._obsolete;

[Guid("5952d7b4-29ac-41fb-9324-287392d55048")]
internal sealed class __ObsoletePulse : Instance<__ObsoletePulse>
{
    [Output (Guid = "56020950-21f1-4868-b753-07c5ad1d22e8", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();
        
    public __ObsoletePulse()
    {
        Result.UpdateAction += Update;
    }

    private void Update (EvaluationContext context)
    {
        var bang = Trigger.GetValue(context);
        if (bang != _lastBang)
        {
            if (bang)
            {
                _lastTriggerTime = (float)context.LocalFxTime;
            }
            _lastBang = bang;
        }

        var timeSinceTrigger = (float)context.LocalFxTime - _lastTriggerTime;
        if (timeSinceTrigger < 0)
            timeSinceTrigger = 0;
            
        Result.Value = Math.Max(Amplitude.GetValue(context) - timeSinceTrigger * Decay.GetValue(context),0);
    }

    private float _lastTriggerTime;
    private bool _lastBang;

    [Input(Guid = "A8D06CD6-CA17-404F-9852-A45CE35DA623")]
    public readonly InputSlot<bool> Trigger = new();

    [Input(Guid = "168EFB4D-E4CB-4264-B845-B6EDE38C1919")]
    public readonly InputSlot<float> Amplitude = new();

    [Input(Guid = "FB8CF1DB-A1AC-4563-ACA0-73A4726D46A4")]
    public readonly InputSlot<float> Decay = new();

}