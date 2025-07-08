using System;
using T3.Core.Logging;
using T3.Core.Operator.Interfaces;

namespace T3.Core.Operator.Slots;

public sealed class TransformCallbackSlot<T> : Slot<T>
{
    public ITransformable TransformableOp { get; set; }

    private new void Update(EvaluationContext context)
    {
        // FIXME: Casting is ugly. TransformCall should us ITransformable instead 
        TransformableOp.TransformCallback?.Invoke(TransformableOp as Instance, context);
        if (_baseUpdateAction == null)
        {
            Log.Warning("Failed to call base transform gizmo update for " + Parent.SymbolChildId, this.Parent.SymbolChildId);
            return;
        }
        _baseUpdateAction(context);
    }

    private Action<EvaluationContext> _baseUpdateAction;

    public override Action<EvaluationContext> UpdateAction
    {
        set
        {
            _baseUpdateAction = value;
            base.UpdateAction = Update;
        }
    }
        
        
    protected override void SetDisabled(bool shouldBeDisabled)
    {
        if (shouldBeDisabled == _isDisabled)
            return;

        if (shouldBeDisabled)
        {
            if (_keepOriginalUpdateAction != null)
            {
                Log.Warning("Is already bypassed or disabled");
                return;
            }
                
            _keepOriginalUpdateAction = _baseUpdateAction;
            base.UpdateAction = EmptyAction;
            DirtyFlag.Invalidate();
        }
        else
        {
            RestoreUpdateAction();
        }

        _isDisabled = shouldBeDisabled;
    }

    internal override bool TrySetBypassToInput(Slot<T> targetSlot)
    {
        if (_keepOriginalUpdateAction != null)
        {
            //Log.Warning("Already disabled or bypassed");
            return false;
        }
            
        _keepOriginalUpdateAction = _baseUpdateAction;
        base.UpdateAction = ByPassUpdate;
        DirtyFlag.Invalidate();
        _targetInputForBypass = targetSlot;
        return true;
    }
}