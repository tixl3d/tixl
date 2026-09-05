using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.anim.utils;

[Guid("127e1eb9-aca9-4f8e-b0c9-b709c8a0745a")]
internal sealed class FindKeyframes : Instance<FindKeyframes>, IStatusProvider
{
    [Output(Guid = "58a74e28-3c7f-43f1-a1a7-ece11bb3a8f8")]
    public readonly Slot<float> Time = new();

    [Output(Guid = "2008F8FE-C5C3-4CB5-893E-1E7F7113C69E")]
    public readonly Slot<float> Value = new();

    [Output(Guid = "B5931CEC-7D39-4C52-A72D-D5E30E5902F3")]
    public readonly Slot<int> KeyframeCount = new();

    public FindKeyframes()
    {
        Time.UpdateAction += Update;
        Value.UpdateAction += Update;
        KeyframeCount.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (!AnimatedOp.HasInputConnections)
        {
            _lastErrorMessage = "No animated operator connected to reference";
            _lastAnimatedOp = null;
            return;
        }

        _lastErrorMessage = null;

        var needsUpdate = OpIndex.DirtyFlag.IsDirty
                          || CurveIndex.DirtyFlag.IsDirty
                          || WrapIndex.DirtyFlag.IsDirty;
            
            

        var opIndex = OpIndex.GetValue(context).Clamp(0, AnimatedOp.CollectedInputs.Count);
        _wrapIndex = WrapIndex.GetValue(context);
        var slot = AnimatedOp.CollectedInputs[opIndex];
        var requestCurveIndex = CurveIndex.GetValue(context);
        if (TryFindCurveWithIndex(slot, requestCurveIndex, needsUpdate, out var curve))
        {
            _curve = curve;
            _keyframes = _curve.GetVDefinitions().ToList();
            KeyframeCount.Value = _keyframes.Count;
        }
        else
        {
            KeyframeCount.Value = 0;
            Time.Value = 0;
            Value.Value = 0;
            return;
        }


        if (_keyframes.Count == 0)
        {
            return;
        }
            
        var indexOrTime = IndexOrTime.GetValue(context);
        switch (Mode.GetEnumValue<Modes>(context))
        {
            case Modes.Index:
            {
                var clampedIndex = _wrapIndex ? (int)indexOrTime % _keyframes.Count
                                       : Math.Abs((int)indexOrTime) % _keyframes.Count ;
                var vDef = _keyframes[clampedIndex];
                Time.Value = (float)vDef.U;
                Value.Value = (float)vDef.Value;
                break;
            }
            case Modes.Nearest:
                if (TryFindClosestKey(indexOrTime, out var closestKey))
                {
                    Time.Value = (float)closestKey.U;
                    Value.Value = (float)closestKey.Value;
                }

                break;

            case Modes.SampleAndDistance:
                if (TryFindClosestKey(indexOrTime, out var closestKey2))
                {
                    Time.Value = (float)closestKey2.U - indexOrTime;
                    Value.Value = (float)_curve.GetSampledValue(indexOrTime);
                }

                break;
        }
    }

    private bool TryFindCurveWithIndex(Slot<float> slot, int requestCurveIndex, bool forceUpdate, out Curve curve)
    {
        curve = null;
        var curveIndex = 0;

        if (slot?.UpdateAction?.Target is not Instance target)
            return false;

        if (!forceUpdate && target == _lastAnimatedOp)
        {
            curve = _curve;
            return curve != null;
        }
            
        _lastAnimatedOp = target;
        if (target.Parent == null)
            return false;
        
        var animator = target.Parent.Symbol.Animator;
        _keyframes.Clear();

        foreach (var p in target.Inputs)
        {
            if (!animator.IsAnimated(target.SymbolChildId, p.Id))
                continue;

            if (!animator.TryGetCurvesForInputSlot(p, out var curves))
                continue;
            
            //var curves = animator.TryGetCurvesForChildInput(p);
            foreach (var c in curves)
            {
                if (curveIndex == requestCurveIndex)
                {
                    curve = c;
                    return true;
                }
                    
                curveIndex++;
            }
        }

        return false;
    }

    private bool TryFindClosestKey(float indexOrTime, out VDefinition closestKey)
    {
        closestKey = null;
        var previousTime = _curve.TryGetPreviousKey(indexOrTime, out var previousKey)
                               ? previousKey.U
                               : double.NegativeInfinity;

        var nextTime = _curve.TryGetNextKey(indexOrTime, out var nextKey)
                           ? nextKey.U
                           : double.PositiveInfinity;

        if (previousKey == null && nextKey == null)
        {
            return false;
        }

        closestKey = Math.Abs(indexOrTime - previousTime) < Math.Abs(indexOrTime - nextTime)
                         ? previousKey
                         : nextKey;
        return true;
    }

    private Instance _lastAnimatedOp;
    private List<VDefinition> _keyframes = new();

    private string _lastErrorMessage;
    private Curve _curve;

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        return _lastErrorMessage;
    }

    private enum Modes
    {
        Index,
        Nearest,
        SampleAndDistance,
    }

    [Input(Guid = "3AD320B9-D1BC-4BDB-B5C8-20CAA721621B", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "8CBF43B8-899A-42D4-87C7-CABB43A5D219")]
    public readonly InputSlot<float> IndexOrTime = new();

    [Input(Guid = "1E606DB7-7FF8-4CC8-9156-A88D7C27A662")]
    public readonly InputSlot<bool> WrapIndex = new();

        

    [Input(Guid = "F5FA41FB-E11F-47FA-B9F8-18620DAA1AE6")]
    public readonly InputSlot<int> OpIndex = new();

    [Input(Guid = "1CB78283-AC4E-44ED-AD40-0A208A4DB099")]
    public readonly InputSlot<int> CurveIndex = new();

    [Input(Guid = "b8611c46-420f-45b0-93e3-be10806a6c57")]
    public readonly MultiInputSlot<float> AnimatedOp = new();

    private bool _wrapIndex;
}