using T3.Core.Utils;

namespace Lib.numbers.anim._obsolete;

[Guid("23794a1f-372d-484b-ac31-9470d0e77819")]
public sealed class _Jitter2d : Instance<_Jitter2d>
{
    [Output(Guid = "4f1fa28e-f010-48d5-bef1-51bceac17649", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector2> NewPosition = new();

    public _Jitter2d()
    {
        NewPosition.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var startPosition = Position.GetValue(context);
        var limitRange = MaxRange.GetValue(context);
        var seed = Seed.GetValue(context);
        var jumpDistance = JumpDistance.GetValue(context);
        _rate = Rate.GetValue(context);
             
        var reset = Reset.GetValue(context);
        var jump = Jump.GetValue(context);

        if (!_initialized || reset || float.IsNaN(_offset.X) || float.IsNaN(_offset.Y) || seed != _seed)
        {
            _random = new Random(seed);
            _seed = seed;
            _offset = Vector2.Zero;
            _initialized = true;
            jump = true;
        }

        _beatTime = context.Playback.FxTimeInBars;
            
        if(UseRate) {
            var activationIndex = (int)(_beatTime * _rate);
            if (activationIndex != _lastActivationIndex)
            {
                _lastActivationIndex = activationIndex;
                jump = true;
            }
        }

        if (jump)
        {
            _jumpStartOffset = _offset;
            _jumpTargetOffset = _offset + new Vector2(
                                                      (float)((_random.NextDouble() - 0.5f) * jumpDistance * 2f),
                                                      (float)((_random.NextDouble() - 0.5f) * jumpDistance * 2f));

            if (limitRange > 0.001f)
            {
                var d = _jumpTargetOffset.Length();
                if (d > limitRange)
                {
                    var overshot =  Math.Min(d- limitRange, limitRange);
                    var random = _random.NextDouble() * overshot;
                    var distanceWithinLimit = limitRange - (float)random;
                    var normalized = _jumpTargetOffset / d;
                    _jumpTargetOffset =  normalized * distanceWithinLimit;
                }
            }

            _lastJumpTime = _beatTime;
        }
            
        var blending = Blending.GetValue(context);
        if (blending >= 0.001)
        {
            var t = (Fragment / blending).Clamp(0,1);
            if(SmoothBlending.GetValue(context))
                t = MathUtils.SmootherStep(0, 1, t);
                
            _offset = Vector2.Lerp(_jumpStartOffset, _jumpTargetOffset, t);
        }
        else
        {
            _offset = _jumpTargetOffset;
        }

        NewPosition.Value = _offset + startPosition;
    }

    public float Fragment =>
        UseRate
            ? (float)((_beatTime - _lastJumpTime) * _rate ).Clamp(0,1)
            : (float)(_beatTime - _lastJumpTime).Clamp(0,1);

        
    private bool UseRate => _rate > 0.0001f;

    private int _seed = 0;
    private float _rate;
    private double _beatTime;

    private Random _random = new();
    private bool _initialized = false;
    private int _lastActivationIndex = 0;
    private double _lastJumpTime;
    private Vector2 _offset;
    private Vector2 _jumpStartOffset;
    private Vector2 _jumpTargetOffset;

    [Input(Guid = "8F17B67C-2D55-4148-B880-1DD948CE9808")]
    public readonly InputSlot<Vector2> Position = new();
        
    [Input(Guid = "F101AF0C-DE31-4AFB-ACB4-8166C62C2EC8")]
    public readonly InputSlot<float> JumpDistance = new();

    [Input(Guid = "87DE02D8-A7AF-4E5C-A079-A70AF222F0BE")]
    public readonly InputSlot<float> MaxRange = new();

    [Input(Guid = "1DF95BEB-DA6D-4263-8273-7A180FD190F5")]
    public readonly InputSlot<float> Rate = new();

    [Input(Guid = "38086D8A-15E0-4F3E-B161-A46A79FC5CC3")]
    public readonly InputSlot<float> Blending = new();

    [Input(Guid = "227D36C5-E1AA-4F3F-AED1-AA92A25DBA8F")]
    public readonly InputSlot<bool> SmoothBlending = new();

    [Input(Guid = "34EA227B-13DB-42DD-ADE5-1B07D2F6BAD5")]
    public readonly InputSlot<int> Seed = new();
        
    [Input(Guid = "BF5E7465-7349-48BD-8358-CCE6E9983AA0")]
    public readonly InputSlot<bool> Reset = new();

    [Input(Guid = "CE65293A-E13E-427A-93D2-EFB0214AD274")]
    public readonly InputSlot<bool> Jump = new();
}