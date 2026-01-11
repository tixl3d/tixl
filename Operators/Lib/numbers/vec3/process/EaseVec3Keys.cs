using T3.Core.Animation;
using T3.Core.Utils;
using static T3.Core.Utils.EasingFunctions;

namespace Lib.numbers.@vec3.process;

[Guid("0ecb95ca-7db2-4a11-a6e5-214ae91ec268")]
internal sealed class EaseVec3Keys : Instance<EaseVec3Keys>
{
    [Output(Guid = "b022aed3-1667-474f-b24e-107b3257e13c", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Result = new();

    private const float MinTimeElapsedBeforeEvaluation = 1 / 1000f;

    public EaseVec3Keys()
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
    private Vector3 _startTime = Vector3.Zero;
    private Vector3 _initialValue = Vector3.Zero;
    private Vector3 _targetValue = Vector3.Zero;

    [Input(Guid = "48d7bfd8-54c3-4c1b-bee4-92aaa735cc85")]
    public readonly InputSlot<Vector3> Value = new();

    [Input(Guid = "fa9c6388-a7eb-4424-956b-dc1b7f2486bc", MappedType = typeof(EaseDirection))]
    public readonly InputSlot<int> Direction = new();

    [Input(Guid = "4b8c9c9b-37fa-4c50-ac65-4c5a20e56d2f", MappedType = typeof(Interpolations))]
    public readonly InputSlot<int> Interpolation = new();
}
