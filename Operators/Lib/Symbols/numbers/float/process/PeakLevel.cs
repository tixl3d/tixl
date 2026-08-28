using T3.Core.Animation;

namespace Lib.numbers.@float.process;

[Guid("d3fb5baf-43f8-4983-a1d9-42f4005a3af0")]
internal sealed class PeakLevel : Instance<PeakLevel>
{
    [Output(Guid = "6fe37109-0177-4823-9466-eaa49adb19d4", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<float> AttackLevel = new();
        
    [Output(Guid = "80DCAD3B-5E93-4991-855D-24176EC54F4D", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> FoundPeak = new();

    [Output(Guid = "EC9B98CF-DD88-4B54-977E-960DDF3D5B32", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> TimeSincePeak = new();

    [Output(Guid = "F0996EF5-39F0-4874-85A9-C3AC83C9D9E8", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> MovingSum = new();


    public PeakLevel()
    {
        AttackLevel.UpdateAction += Update;
        FoundPeak.UpdateAction += Update;
        TimeSincePeak.UpdateAction += Update;
        MovingSum.UpdateAction += Update;
    }


    private void Update(EvaluationContext context)
    {
        var t = context.Playback.FxTimeInBars;

        var wasEvaluatedThisFrame = Math.Abs(t - _lastEvalTime) < 0.001f;
        if (wasEvaluatedThisFrame)
        {
            return;
        }
            
        _lastEvalTime = t;
            
        var value = Value.GetValue(context);
        var increase = (value - _lastValue);//.Clamp(0, 10000);
            
        var timeSinceLastPeak = Playback.RunTimeInSecs - _lastPeakTime;
        if (timeSinceLastPeak < 0)
            _lastPeakTime = Double.NegativeInfinity;

        if (increase > Threshold.GetValue(context) && timeSinceLastPeak > MinTimeBetweenPeaks.GetValue(context))
        {
            _lastPeakTime = Playback.RunTimeInSecs;
            FoundPeak.Value = true;
        }
        else
        {
            FoundPeak.Value = false;
        }

        var previousSum = MovingSum.GetValue(context);

        const float precisionThreshold = 30000f;
        if (Math.Abs(previousSum) > precisionThreshold)
        {
            previousSum %= precisionThreshold;
        }
        MovingSum.Value = previousSum + increase;
            
        AttackLevel.Value = increase;
        TimeSincePeak.Value = (float)timeSinceLastPeak;
        _lastValue = value;
            
        FoundPeak.DirtyFlag.Clear();
        TimeSincePeak.DirtyFlag.Clear();
        AttackLevel.DirtyFlag.Clear();
    }
        

    private double _lastEvalTime;
    private double _lastPeakTime = double.NegativeInfinity;
    private float _lastValue;
        
        
    [Input(Guid = "88ed25d3-ab67-47da-ad38-2f0126ce0492")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "279C4F32-2F8A-4679-AFE4-BBF14CF6BA05")]
    public readonly InputSlot<float> Threshold = new();
        

    [Input(Guid = "D38D54B4-D15C-40C3-A5F1-6546F444965C")]
    public readonly InputSlot<float> MinTimeBetweenPeaks = new();
}