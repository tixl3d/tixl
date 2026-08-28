namespace Lib.point._internal;

[Guid("b1352ba6-1164-4b44-9b69-a9eb802ea77d")]
internal sealed class _ExecuteParticleUpdate : Instance<_ExecuteParticleUpdate>
{
    [Output(Guid = "8788AEB6-E339-43D9-930B-8AF3BF703B7A")]
    public readonly Slot<ParticleSystem> Output2 = new ();

        
    public _ExecuteParticleUpdate()
    {
        Output2.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var commands = Commands.CollectedInputs;
        if (IsEnabled.GetValue(context))
        {
            if (commands != null)
            {
                // execute commands
                for (int i = 0; i < commands.Count; i++)
                {
                    //Log.Debug("Execute command " + i);
                    commands[i].GetValue(context);
                }
            }
        }
        Commands.DirtyFlag.Clear();
    }

    [Input(Guid = "5D480604-DCB1-455C-B961-D72218380C99")]
    public readonly MultiInputSlot<Command> Commands = new();
        
    [Input(Guid = "fa9f7267-d138-4219-ab78-ed37546a259c")]
    public readonly InputSlot<bool> IsEnabled = new();
        
}