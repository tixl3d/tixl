namespace Lib.flow;

[Guid("e8d2b4ac-0ba2-400f-9c82-e5dd15a23d32")]
internal sealed class Once : Instance<Once>
{
    [Output(Guid = "68389552-6d8a-433b-a75f-18e76435519b")]
    public readonly Slot<bool> OutputTrigger = new();

    public Once()
    {
        OutputTrigger.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Math.Abs(context.LocalFxTime - _lastUpdateTime) < 0.0001f)
        {
            return;
        }

        _lastUpdateTime = context.LocalFxTime;
        
        var dirtyFlagIsDirty = Trigger.DirtyFlag.IsDirty;
        OutputTrigger.Value = dirtyFlagIsDirty;
        
        if (dirtyFlagIsDirty)
        {
            OutputTrigger.DirtyFlag.Trigger = DirtyFlagTrigger.Always;
        }
        else
        {
            OutputTrigger.DirtyFlag.Trigger = DirtyFlagTrigger.None;
        }
        Trigger.DirtyFlag.Clear();
    }

    private double _lastUpdateTime = -1;

    [Input(Guid = "1da5310b-ecad-4f5b-871f-b0321a521ef6")]
    public readonly InputSlot<bool> Trigger = new(true);
}