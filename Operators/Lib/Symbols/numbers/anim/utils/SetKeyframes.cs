using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.anim.utils;

[Guid("97ea4288-2deb-44ea-8714-8374c71fda88")]
internal sealed class SetKeyframes : Instance<SetKeyframes>
                                   , IStatusProvider
{
    [Output(Guid = "02bdfa79-7076-4095-85d9-740401db0f9d")]
    public readonly Slot<float> CurrentValue = new();

    public SetKeyframes()
    {
        CurrentValue.UpdateAction += Update;
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
        var value = Value.GetValue(context);

        var needsUpdate = OpIndex.DirtyFlag.IsDirty
                          || CurveIndex.DirtyFlag.IsDirty;

        var triggeredSet = MathUtils.WasTriggered(TriggerSet.GetValue(context), ref _triggeredSet);
        var triggeredClear = MathUtils.WasTriggered(TriggerClear.GetValue(context), ref _triggeredClear);
        
        if(TriggerSet.Value && !TriggerSet.HasInputConnections)
            TriggerSet.SetTypedInputValue(false);
            
        if(TriggerClear.Value && !TriggerClear.HasInputConnections)
            TriggerClear.SetTypedInputValue(false);

        var opIndex = OpIndex.GetValue(context).Clamp(0, AnimatedOp.CollectedInputs.Count);
        var slot = AnimatedOp.CollectedInputs[opIndex];
        var requestCurveIndex = CurveIndex.GetValue(context);

        if (TryFindCurveWithIndex(slot, requestCurveIndex, needsUpdate, out var curve))
        {
            _curve = curve;

            if (triggeredSet)
            {
                _curve.AddOrUpdateV(context.LocalFxTime, new VDefinition() { Value = value });
                CurrentValue.Value = value;
            }

            if (triggeredClear)
            {
                var defs = _curve.GetVDefinitions();
                
                for (var index = defs.Count - 1; index >= 0; index--)
                {
                    var vDef = defs[index];
                    _curve.RemoveKeyframeAt(vDef.U);
                }
            }
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

        if (target.Parent == null)
        {
            _lastAnimatedOp = null;
            return false;
        }

        _lastAnimatedOp = target;
        var animator = target.Parent.Symbol.Animator;

        foreach (var p in target.Inputs)
        {
            if (!animator.IsAnimated(target.SymbolChildId, p.Id))
                continue;

            if (!animator.TryGetCurvesForInputSlot(p, out var curves))
                continue;
            
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

    private bool _triggeredSet;
    private bool _triggeredClear;
    private Instance _lastAnimatedOp;

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

    [Input(Guid = "FC7C69F7-8F6B-4BE9-96B7-BF3145F4DEF4")]
    public readonly InputSlot<bool> TriggerSet = new();

    [Input(Guid = "E1C338E4-DB8D-4D12-B073-34DB00170565")]
    public readonly InputSlot<bool> TriggerClear = new();

    [Input(Guid = "36B4906D-1CAB-4589-B368-D63C5A9A3BA6")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "198c6328-1b1c-4ba3-81dd-3b109ac86353")]
    public readonly InputSlot<int> OpIndex = new();

    [Input(Guid = "6c918e42-d313-4921-8b8f-6485f3f5cb6c")]
    public readonly InputSlot<int> CurveIndex = new();

    [Input(Guid = "92ecce97-9412-44f6-808e-9036f3f99ce4")]
    public readonly MultiInputSlot<float> AnimatedOp = new();
}