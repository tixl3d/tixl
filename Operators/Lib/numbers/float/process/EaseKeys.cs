using T3.Core.Animation;
using T3.Core.Utils;
using static T3.Core.Utils.EasingFunctions;

namespace Lib.numbers.@float.process;

[Guid("02aed181-4bf1-48f8-ac2a-93d66c170c0c")]
internal sealed class EaseKeys : Instance<EaseKeys>
{
    [Output(Guid = "3543e169-91ca-45ed-9b17-675f67a03a51", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

    public EaseKeys()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var easeMode = Interpolation.GetEnumValue<Interpolations>(context); // Easing function selector
        var easeDirection = Direction.GetEnumValue<EaseDirection>(context);

        var currentTime = context.LocalTime;

        if (Math.Abs(currentTime - _lastEvalTime) < MinTimeElapsedBeforeEvaluation)
            return;

        if (!TryFindCurveWithIndex(out var curve))
        {
            Result.Value = Value.GetValue(context);
            return;
        }

        _curve = curve;
        _keyframes = _curve.GetVDefinitions().ToList();

        _lastEvalTime = currentTime;

        float duration;

        if (
            TryFindClosestKeys(currentTime, out var nearestKeys)
            && nearestKeys.Item1.OutType != VDefinition.Interpolation.Constant
        )
        {
            var (previousKey, nextKey) = nearestKeys;
            _startTime = previousKey.U;
            _initialValue = (float)previousKey.Value;
            _targetValue = (float)nextKey.Value;
            duration = Math.Max((float)(nextKey.U - previousKey.U), 0.0001f);
        }
        else
        {
            Result.Value = Value.GetValue(context);
            return;
        }

        // Calculate progress based on elapsed time and duration
        var elapsedTime = (float)(currentTime - _startTime);
        var progress = (elapsedTime / duration).Clamp(0f, 1f);

        var easedProgress = ApplyEasing(progress, easeDirection, easeMode);

        Result.Value = MathUtils.Lerp(_initialValue, _targetValue, easedProgress);
    }

    private bool TryFindCurveWithIndex(out Curve curve)
    {
        curve = null;

        var animator = Parent.Symbol.Animator;
        _keyframes.Clear();

        if (!animator.IsAnimated(SymbolChildId, Value.Id))
            return false;

        if (!animator.TryGetCurvesForInputSlot(Value, out var curves))
            return false;

        if (curves.Length == 0)
            return false;

        curve = curves[0];
        return true;
    }

    private bool TryFindClosestKeys(double time, out Tuple<VDefinition, VDefinition> closestKeys)
    {
        closestKeys = null;
        _curve.TryGetPreviousKey(time, out var previousKey);
        _curve.TryGetNextKey(time, out var nextKey);

        if (previousKey == null || nextKey == null)
        {
            return false;
        }

        closestKeys = new(previousKey, nextKey);
        return true;
    }
    private List<VDefinition> _keyframes = [];

    private Curve _curve;

    // Ease.cs
    private double _lastEvalTime;
    private double _startTime;
    private float _initialValue;
    private float _targetValue;

    [Input(Guid = "73c822ca-560c-4e86-b4ff-0ae722d7a371")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "ca4739a7-be8b-4e65-bbcf-593772bd0c5d", MappedType = typeof(EaseDirection))]
    public readonly InputSlot<int> Direction = new();

    [Input(Guid = "375b49e7-bc67-46cf-a687-2cad66e6fd76", MappedType = typeof(Interpolations))]
    public readonly InputSlot<int> Interpolation = new();
}
