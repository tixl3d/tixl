namespace Lib.flow;

[Guid("7a09136e-18b2-46b7-afff-8ef999e3965d")]
internal sealed class ExecuteOnce : Instance<ExecuteOnce>
{
    [Output(Guid = "5D73EBE6-9AA0-471A-AE6B-3F5BFD5A0F9C")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "cf2cc2a9-9db1-47ca-b68f-b894140d8481")]
    public readonly Slot<bool> OutputTrigger = new();

    public ExecuteOnce()
    {
        Output.UpdateAction += Update;
        OutputTrigger.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        OutputTrigger.Value = Trigger.DirtyFlag.IsDirty;
        if (Trigger.DirtyFlag.IsDirty)
        {
            //Log.Info("ExecuteOnce triggered", this);
            Trigger.DirtyFlag.Clear();
            var commands = Command.GetCollectedTypedInputs();

            // do preparation if needed
            for (int i = 0; i < commands.Count; i++)
            {
                commands[i].Value?.PrepareAction?.Invoke(context);
            }

            // execute commands
            for (int i = 0; i < commands.Count; i++)
            {
                commands[i].GetValue(context);
            }

            // cleanup after usage
            for (int i = 0; i < commands.Count; i++)
            {
                commands[i].Value?.RestoreAction?.Invoke(context);
            }
        }
    }

    [Input(Guid = "7450033D-5797-40C9-B6C4-B6E8D27FE501")]
    public readonly MultiInputSlot<Command> Command = new();
    [Input(Guid = "2049D44D-81A4-493B-A630-A1B273A4E6F9")]
    public readonly InputSlot<bool> Trigger = new(true);
}