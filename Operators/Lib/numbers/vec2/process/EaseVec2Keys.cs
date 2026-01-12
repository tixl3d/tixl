using T3.Core.Animation;
using T3.Core.Utils;
using static T3.Core.Utils.EasingFunctions;

namespace Lib.numbers.@vec2.process;

[Guid("42b8c3bc-1425-4592-98e3-c4acacd84348")]
internal sealed class EaseVec2Keys : Instance<EaseVec2Keys>
{
    [Output(Guid = "3288ad72-ade9-4e74-8885-87c1bc3579b8", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector2> Result = new();

    private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

    public EaseVec2Keys()
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

        if (!TryFindCurves(out var curves))
        {
            Result.Value = Value.GetValue(context);
            return;
        }

        for (var i = 0; i < curves.Length; i++)
        {
            var curve = curves[i];
            _keyframes = curve.GetVDefinitions().ToList();
            _lastEvalTime = currentTime;
            float duration;

            if (
                TryFindClosestKeys(currentTime, curve, out var nearestKeys)
                && nearestKeys.Item1.OutType != VDefinition.Interpolation.Constant
            )
            {
                var (previousKey, nextKey) = nearestKeys;
                _startTime[i] = (float)previousKey.U;
                _initialValue[i] = (float)previousKey.Value;
                _targetValue[i] = (float)nextKey.Value;
                duration = Math.Max((float)(nextKey.U - previousKey.U), 0.0001f);
            }
            else
            {
                Result.Value[i] = Value.GetValue(context)[i];
                continue;
            }

            // Calculate progress based on elapsed time and duration
            var elapsedTime = (float)currentTime - _startTime[i];
            var progress = (elapsedTime / duration).Clamp(0f, 1f);

            var easedProgress = ApplyEasing(progress, easeDirection, easeMode);

            Result.Value[i] = MathUtils.Lerp(_initialValue[i], _targetValue[i], easedProgress);
        }
    }

    private bool TryFindCurves(out Curve[] curves)
    {
        curves = null;

        var animator = Parent.Symbol.Animator;
        _keyframes.Clear();

        if (!animator.IsAnimated(SymbolChildId, Value.Id))
            return false;

        if (!animator.TryGetCurvesForInputSlot(Value, out var _curves))
            return false;

        if (_curves.Length == 0)
            return false;

        curves = _curves;
        return true;
    }

    private static bool TryFindClosestKeys(double time, Curve curve, out Tuple<VDefinition, VDefinition> closestKeys)
    {
        closestKeys = null;
        curve.TryGetPreviousKey(time, out var previousKey);
        curve.TryGetNextKey(time, out var nextKey);

        if (previousKey == null || nextKey == null)
        {
            return false;
        }

        closestKeys = new(previousKey, nextKey);
        return true;
    }
    private List<VDefinition> _keyframes = [];

    // Ease.cs
    private double _lastEvalTime;
    private Vector2 _startTime = Vector2.Zero;
    private Vector2 _initialValue = Vector2.Zero;
    private Vector2 _targetValue = Vector2.Zero;

    [Input(Guid = "68f2144e-e749-4193-86d5-f923f2f155d0")]
    public readonly InputSlot<Vector2> Value = new();

    [Input(Guid = "944dfd8a-0e4b-4387-b420-d1c6a69fdeef", MappedType = typeof(EaseDirection))]
    public readonly InputSlot<int> Direction = new();

    [Input(Guid = "fba596d6-bb06-43ad-921e-ea55da365fc2", MappedType = typeof(Interpolations))]
    public readonly InputSlot<int> Interpolation = new();
}
