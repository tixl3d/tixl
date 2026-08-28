using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.vec3;

[Guid("841e4072-4138-4dda-b156-b01ec3a8f9ae")]
internal sealed class HasVec3Changed : Instance<HasVec3Changed>
{
    [Output(Guid = "3c76da29-7290-438b-bb9a-7ab576f6b30f")]
    public readonly Slot<bool> HasChanged = new();

    [Output(Guid = "FF9DDD91-3647-4EA7-B7A6-9A0EFBE9E47E")]
    public readonly Slot<Vector3> Delta = new();

    [Output(Guid = "10D165C1-2DDF-4921-BA39-0CE683A6AEB3")]
    public readonly Slot<Vector3> DeltaOnHit = new();

        
    public HasVec3Changed()
    {
        HasChanged.UpdateAction += Update;
        Delta.UpdateAction += Update;
    }

    private double _lastEvalTime = 0;

    private void Update(EvaluationContext context)
    {
        var newValue = Value.GetValue(context);
        var threshold = Threshold.GetValue(context);
        var minTimeBetweenHits = MinTimeBetweenHits.GetValue(context);
        var preventContinuedChanges = PreventContinuedChanges.GetValue(context);

        if (Math.Abs(Playback.RunTimeInSecs - _lastEvalTime) < 0.010f)
            return;

        _lastEvalTime = Playback.RunTimeInSecs;

            
        var hasChanged = false;
            
        var delta = new Vector3(
                                Math.Abs(newValue.X - _lastValue.X),
                                Math.Abs(newValue.Y - _lastValue.Y),
                                Math.Abs(newValue.Z - _lastValue.Z))
            ;

        switch ((Modes)Mode.GetValue(context).Clamp(0, Enum.GetNames(typeof(Modes)).Length -1))
        {
            case Modes.Changed:
                var increase = delta.X > threshold 
                               || delta.Y > threshold
                               || delta.Z > threshold;
                    
                hasChanged = increase;
                break;
                
            case Modes.Increased:
                hasChanged = newValue.X > (_lastValue.X + threshold)
                             ||newValue.Y > (_lastValue.Y + threshold)
                             ||newValue.Z > (_lastValue.Z + threshold);
                break;
                
            case Modes.Decreased:
                hasChanged = newValue.X < _lastValue.X - threshold
                             ||newValue.Y < _lastValue.Y - threshold
                             ||newValue.Z < _lastValue.Z - threshold;
                break;
                
            default:
                throw new ArgumentOutOfRangeException();
        }

        var wasTriggered = MathUtils.WasTriggered(hasChanged, ref _wasHit);

        if (hasChanged && (preventContinuedChanges || wasTriggered))
        {
            var timeSinceLastHit = context.LocalFxTime - _lastHitTime;
            if (timeSinceLastHit >= minTimeBetweenHits)
            {
                _lastHitTime = context.LocalFxTime;
                _lastHitDelta = delta;

            }
            else
            {
                hasChanged = false;
            }
        }
            
        HasChanged.Value = hasChanged;

        Delta.Value = newValue - _lastValue;
        _lastValue = newValue;

        DeltaOnHit.Value = _lastHitDelta;
    }

    private Vector3 _lastValue;
    private double _lastHitTime;
    private Vector3 _lastHitDelta;
    private bool _wasHit;

    [Input(Guid = "1D19DA45-54E4-4EE9-A1A8-A04D9FFC5CAC")]
    public readonly InputSlot<Vector3> Value = new();

    [Input(Guid = "a5b87ffd-0710-4dc6-8eb7-f40a92fae0ef")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "7db5d745-7c61-41cc-af9a-b752540d4842")]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "b6d0899b-9c65-4622-af80-43a9a34110f2")]
    public readonly InputSlot<float> MinTimeBetweenHits = new();

    [Input(Guid = "56a9a9df-6e45-4124-b9fe-a157fef8f40a")]
    public readonly InputSlot<bool> PreventContinuedChanges = new();


    private enum Modes
    {
        Changed,
        Increased,
        Decreased,
    }
}