using T3.Core.Utils;

namespace Lib.numbers.@float.logic;

[Guid("146fae64-18da-4183-9794-a322f47c669e")]
internal sealed class HasValueChanged : Instance<HasValueChanged>
{
    [Output(Guid = "35ab8188-77a1-4cd9-b2ad-c503034e49f9", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> HasChanged = new();

    [Output(Guid = "ab818835-77a1-4cd9-b2ad-c503034e49f9", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Delta = new();

    [Output(Guid = "5E37DA80-5D1F-4E17-A0A1-1E386B3A2561")]
    public readonly Slot<float> DeltaOnHit = new();

    public HasValueChanged()
    {
        HasChanged.UpdateAction += Update;
        Delta.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var newValue = Value.GetValue(context);
        var threshold = Threshold.GetValue(context);
        var minTimeBetweenHits = MinTimeBetweenHits.GetValue(context);
        var preventContinuedChanges = PreventContinuedChanges.GetValue(context);

        if (Math.Abs(context.LocalFxTime - _lastEvalTime) < 0.0002f)
            return;

        _lastEvalTime = context.LocalFxTime;

        var hasChanged = false;

        var absDelta = Math.Abs(newValue - _lastValue);

        switch (Mode.GetEnumValue<Modes>(context))
        {
            case Modes.Changed:

                var increase = absDelta > threshold;
                hasChanged = increase;
                break;

            case Modes.Increased:
                hasChanged = newValue > _lastValue + threshold;
                break;

            case Modes.Decreased:
                hasChanged = newValue < _lastValue - threshold;
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        var wasTriggered = MathUtils.WasTriggered(hasChanged, ref _wasHit);

        if (hasChanged && (preventContinuedChanges || wasTriggered))
        {
            var timeSinceLastHit = Math.Abs(context.LocalFxTime - _lastHitTime);
            if (timeSinceLastHit >= minTimeBetweenHits)
            {
                _lastHitTime = context.LocalFxTime;
                _lastHitDelta = absDelta;
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

    private float _lastValue;
    private double _lastHitTime;
    private float _lastHitDelta;
    private bool _wasHit;
    private double _lastEvalTime = -1;

    [Input(Guid = "7f5fb125-8aca-4344-8b30-e7d4e7873c1c")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "1e03f11b-f6cc-497e-9528-ed9490e878b5")]
    public readonly InputSlot<float> Threshold = new();

    [Input(Guid = "031ef11b-f6cc-497e-9528-ed9490e878b5", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "B994249D-C101-41D9-8142-A4F675FCD70C")]
    public readonly InputSlot<float> MinTimeBetweenHits = new();

    [Input(Guid = "8EBF4715-0B4B-4CDD-A079-9F91C2DF0476")]
    public readonly InputSlot<bool> PreventContinuedChanges = new();

    private enum Modes
    {
        Changed,
        Increased,
        Decreased,
    }
}