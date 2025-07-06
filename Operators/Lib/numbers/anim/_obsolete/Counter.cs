using T3.Core.Utils;

namespace Lib.numbers.anim._obsolete;

[Guid("11882635-4757-4cac-a024-70bb4e8b504c")]
public sealed class Counter : Instance<Counter>
{
    [Output(Guid = "c53e3a03-3a6d-4547-abbf-7901b5045539", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    [Output(Guid = "BAE829AD-8454-4625-BDE4-A7AB62F579A4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasStep = new();

    public Counter()
    {
        Result.UpdateAction += Update;
        WasStep.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var startPosition = StartValue.GetValue(context);
        var modulo = Modulo.GetValue(context);
        var increment = Increment.GetValue(context);
        _rate = Rate.GetValue(context);
        _phase = Phase.GetValue(context);
        _blending = Blending.GetValue(context);
        var reset = TriggerReset.GetValue(context);
        var jump = TriggerIncrement.GetValue(context);
        var smoothBlending = SmoothBlending.GetValue(context);

        var f = (SpeedFactors)AllowSpeedFactor.GetValue(context);
        switch (f)
        {
            case SpeedFactors.None:
                _speedFactor = 1;
                break;
            case SpeedFactors.FactorA:
                {
                    if (!context.FloatVariables.TryGetValue(SpeedFactorA, out _speedFactor))
                        _speedFactor = 1;

                    break;
                }
            case SpeedFactors.FactorB:
                if (!context.FloatVariables.TryGetValue(SpeedFactorB, out _speedFactor))
                    _speedFactor = 1;

                break;

            default:
                Log.Debug($"Incorrect speed factor mode {f} in Counter", this);
                _speedFactor = 1;
                break;
        }

        if (!_initialized || reset || float.IsNaN(_count))
        {
            _count = 0;
            _initialized = true;
            jump = true;
        }

        _beatTime = context.Playback.FxTimeInBars * _speedFactor;

        if (UseRate)
        {
            var activationIndex = (int)(_beatTime * _rate + _phase);
            if (activationIndex != _lastActivationIndex)
            {
                //Log.Debug($"ai {activationIndex}  != {_lastActivationIndex}  rate={_rate} t = {_beatTime} ", this);
                _lastActivationIndex = activationIndex;
                jump = true;
            }
        }

        // Store previous count before updating
        var previousCount = _count;

        if (jump)
        {
            if (modulo > 0.001f)
            {
                _jumpStartOffset = _jumpTargetOffset;
                _jumpTargetOffset += 1;
            }
            else
            {
                _jumpStartOffset = _count;
                _jumpTargetOffset = _count + increment;
            }
            _lastJumpTime = _beatTime;
        }

        if (_blending >= 0.001)
        {
            var t = (Fragment / _blending).Clamp(0, 1);
            if (smoothBlending)
                t = MathUtils.SmootherStep(0, 1, t);

            _count = MathUtils.Lerp(_jumpStartOffset, _jumpTargetOffset, t);
        }
        else
        {
            _count = _jumpTargetOffset;
        }

        if (modulo > 0.001f)
        {
            Result.Value = (_count * increment % modulo) + startPosition;
        }
        else
        {
            Result.Value = _count + startPosition;
        }

        // WasStep.Value = jump;
        // Improved WasStep logic similar to AnimValue's WasHit
        if (Math.Abs(context.LocalFxTime - _lastUpdateTime) > double.Epsilon)
        {
            _lastUpdateTime = context.LocalFxTime;
            WasStep.Value = (int)previousCount != (int)_count;
        }

        Result.DirtyFlag.Clear();
        WasStep.DirtyFlag.Clear();
    }

    private enum SpeedFactors
    {
        None,
        FactorA,
        FactorB,
    }

    private const string SpeedFactorA = "SpeedFactorA";
    private const string SpeedFactorB = "SpeedFactorB";


    public float Fragment =>
        UseRate
            ? (float)((_beatTime - _lastJumpTime) * _rate).Clamp(0, 1)
            : (float)(_beatTime - _lastJumpTime).Clamp(0, 1);

    private bool UseRate => _rate > -1 && !TriggerIncrement.HasInputConnections;

    private float _speedFactor = 1;
    private float _rate;
    private float _phase;

    private double _beatTime;
    private float _blending;

    private bool _initialized = false;
    private int _lastActivationIndex = 0;
    private double _lastJumpTime;
    private float _count;
    private float _jumpStartOffset;
    private float _jumpTargetOffset;

    private double _lastUpdateTime = double.NegativeInfinity;

    [Input(Guid = "eefdb8ca-68e7-4e39-b302-22eb8930fb8c")]
    public readonly InputSlot<bool> TriggerIncrement = new();

    [Input(Guid = "7BFBAE6B-FA0B-4E5A-8040-E0BE3600AFEB")]
    public readonly InputSlot<bool> TriggerReset = new();

    [Input(Guid = "754CEBE3-AB6C-4877-9C32-C67FBAE9E4C2")]
    public readonly InputSlot<float> StartValue = new();

    [Input(Guid = "BCA3F7B2-A093-4CB3-89A5-0E2681760607")]
    public readonly InputSlot<float> Increment = new();

    [Input(Guid = "73B493CB-91D1-4D4F-B9A8-005017ECAC8F")]
    public readonly InputSlot<float> Modulo = new();

    [Input(Guid = "286CBBFB-796D-499F-93D3-D467512110BE")]
    public readonly InputSlot<float> Rate = new();

    [Input(Guid = "701E7534-FAB2-4204-A68F-66D467E39F66")]
    public readonly InputSlot<float> Phase = new();

    [Input(Guid = "B04D475B-A898-421B-BF26-AE5CF982A351")]
    public readonly InputSlot<float> Blending = new();

    [Input(Guid = "E0C386B9-A987-4D11-9171-2971FA759827")]
    public readonly InputSlot<bool> SmoothBlending = new();

    [Input(Guid = "C386B9E0-A987-4D11-9171-2971FA759827", MappedType = typeof(SpeedFactors))]
    public readonly InputSlot<int> AllowSpeedFactor = new();

}