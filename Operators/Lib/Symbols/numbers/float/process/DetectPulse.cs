using T3.Core.Utils;

namespace Lib.numbers.@float.process;

[Guid("7ff47023-622e-4834-8de5-2438d56c09bd")]
internal sealed class DetectPulse : Instance<DetectPulse>
{
    [Output(Guid = "b3254af8-03c6-41af-a775-5724aaaa91ef")]
    public readonly Slot<bool> HasChanged = new();
        
    [Output(Guid = "637DDDDA-A2D9-4E13-9EE3-9CB3790A103A")]
    public readonly Slot<float> DebugValue = new();
        
    public DetectPulse()
    {
        HasChanged.UpdateAction += Update;
        DebugValue.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var newValue = Value.GetValue(context);
        var threshold = Threshold.GetValue(context);
        var minTimeBetweenHits = MinTimeBetweenHits.GetValue(context);

        var hasTimeDecreased = context.LocalFxTime < _lastHitTime;
        if (hasTimeDecreased)
            _lastHitTime = 0;
            
        var deltaToDamped = _dampedValue - newValue;
            
        var dampFactor = Damping.GetValue(context).Clamp(0,1);
        _dampedValue = MathUtils.Lerp(newValue, _dampedValue, dampFactor);

        DebugValue.Value = deltaToDamped;
            
        var exceedsThreshold = deltaToDamped > threshold;
        var wasTriggered = MathUtils.WasTriggered(exceedsThreshold, ref _wasHit);

        var isHit = false;
        var timeSinceLastHit = context.LocalFxTime - _lastHitTime;
            
        Log.Debug($"{deltaToDamped}  triggered: {wasTriggered}  delta: {deltaToDamped}", this);
        if (wasTriggered && timeSinceLastHit >= minTimeBetweenHits)
        {
                
            if (timeSinceLastHit >= minTimeBetweenHits)
            {
                _lastHitTime = context.LocalFxTime;
                isHit = true;
            }
        }
            
        HasChanged.Value = isHit;
    }

    private double _lastHitTime = Double.NegativeInfinity;
    private bool _wasHit;
    private float _dampedValue;

    [Input(Guid = "c0b97bc3-1f93-4678-b93f-d364374fd40a")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "b9a322a3-b85d-4e51-ad1c-17d61a6f5f5e")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "4424863B-B25C-449A-8AC9-16796617AF7B")]
    public readonly InputSlot<float> Damping = new();

    [Input(Guid = "2cf8d9e3-a984-4760-88cd-5dc2c8b66d7b")]
    public readonly InputSlot<float> MinTimeBetweenHits = new();
}