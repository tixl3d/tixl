using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.vec2;

[Guid("cba315e4-6113-4a2e-969e-e4c2e914c293")]
internal sealed class HasVec2Changed : Instance<HasVec2Changed>
{
    [Output(Guid = "E8AB20A6-73C3-4FE5-B749-B664E258B3CF")]
    public readonly Slot<bool> HasChanged = new();

    [Output(Guid = "6E03AE0A-4091-4921-8813-BD67FD2B0AD4")]
    public readonly Slot<Vector2> Delta = new();
        
    public HasVec2Changed()
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
            
        float delta = Vector2.Distance(newValue, _lastValue);
            
        var increase = delta > threshold;
        hasChanged = increase;
    
        var wasTriggered = MathUtils.WasTriggered(hasChanged, ref _wasHit);

        if (hasChanged && (preventContinuedChanges || wasTriggered))
        {
            var timeSinceLastHit = context.LocalFxTime - _lastHitTime;
            if (timeSinceLastHit >= minTimeBetweenHits)
            {
                _lastHitTime = context.LocalFxTime;

            }
            else
            {
                hasChanged = false;
            }
        }
            
        HasChanged.Value = hasChanged;

        Delta.Value = newValue - _lastValue;
        _lastValue = newValue;
    }

    private Vector2 _lastValue;
    private double _lastHitTime;
    private bool _wasHit;

    [Input(Guid = "8C6AF96B-C2EB-43B7-B3C3-EECC8E40117D")]
    public readonly InputSlot<Vector2> Value = new();

    [Input(Guid = "5148453b-9ed1-49f9-b44a-8308c9993b33")]
    public readonly InputSlot<float> Threshold = new();
        

    [Input(Guid = "ad325a4a-63ca-455d-9ff6-13fe0727bc4c")]
    public readonly InputSlot<float> MinTimeBetweenHits = new();

    [Input(Guid = "36c294ef-3b4c-43fa-a21a-07393d888899")]
    public readonly InputSlot<bool> PreventContinuedChanges = new();

        
}