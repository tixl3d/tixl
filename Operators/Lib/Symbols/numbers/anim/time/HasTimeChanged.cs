using T3.Core.Animation;

namespace Lib.numbers.anim.time;

[Guid("2443b2fd-c397-4ea6-9588-b595f918cf01")]
internal sealed class HasTimeChanged : Instance<HasTimeChanged>
{
    [Output(Guid = "4883b1ec-16c1-422f-8db6-c74c3d48e5be", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasChanged = new();
        
    [Output(Guid = "B459946D-253A-4583-88EE-897E8041F468", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> DeltaTime = new();
        
    public HasTimeChanged()
    {
        HasChanged.UpdateAction += Update;
    }

    private int _lastFrameUpdate;

    private void Update(EvaluationContext context)
    {
        // Support double evaluation of animated outputs 
        if (Playback.FrameCount == _lastFrameUpdate)
        {
            return;
        }
        _lastFrameUpdate = Playback.FrameCount;
        
        var threshold = Threshold.GetValue(context);
        var mode = (Modes)Mode.GetValue(context);
        var whichTime = (Times)WhichTime.GetValue(context);

        double time = 0;

        switch (whichTime)
        {
                
            case Times.LocalTime:
                time = context.LocalTime;
                break;
                
            case Times.GlobalTime:
                time = context.Playback.TimeInBars;
                break;
                
            case Times.GlobalFxTime:
                time = context.Playback.FxTimeInBars;
                break;
                
            case Times.LocalFxTime:
            default:
                time = context.LocalFxTime;
                break;
        }
            
                
        var wasRewind = time < _lastTime - threshold;
        var wasAdvanced = time > _lastTime + threshold;
        DeltaTime.Value = (float)(time - _lastTime);

        bool hasChanged = false;
        bool wasAdditionalMotionBlurPass = false;
            
        switch (mode)
        {
            case Modes.DidRewind:
                hasChanged = wasRewind;
                break;
                
            case Modes.DidAdvanced:
                hasChanged = wasAdvanced;
                break;
                
            case Modes.DidChange:
                hasChanged = wasAdvanced | wasRewind;
                break;

            case Modes.DidAdvancedWithMotionBlur:
                if (context.IntVariables.TryGetValue("__MotionBlurPass", out var pass))
                {
                    if (pass == 0)
                    {
                        hasChanged = wasAdvanced;
                            
                    }
                    else
                    {
                        wasAdditionalMotionBlurPass = true;
                    }
                }
                else
                {
                    hasChanged = wasAdvanced;
                }

                break;
        }
            
        if(!wasAdditionalMotionBlurPass)
            _lastTime = time;
            
        HasChanged.Value = hasChanged;
        //HasChanged.DirtyFlag.Clear(); // FIXME: is this necessary?
    }

    private enum Modes
    {
        DidRewind,
        DidAdvanced,
        DidChange,
        DidAdvancedWithMotionBlur,
    }

    private enum Times
    {
        LocalTime,
        LocalFxTime,
        GlobalTime,
        GlobalFxTime,
    }
        
    private double _lastTime;
    //private bool _triggeredLastFrame; 
        
    [Input(Guid = "AA73CDBA-F295-446D-9693-53055CA4EDC6")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "BC112889-77A8-4967-A9B7-683B7C7017FE", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "5AC02169-6B75-4176-AE0A-04CAB6C4CE13", MappedType = typeof(Times))]
    public readonly InputSlot<int> WhichTime = new();

        
}