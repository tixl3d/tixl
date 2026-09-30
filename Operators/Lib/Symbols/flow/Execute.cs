namespace Lib.flow;

[Guid("936e4324-bea2-463a-b196-6064a2d8a6b2")]
internal sealed class Execute : Instance<Execute>
{
    [Output(Guid = "E81C99CE-FCEE-4E7C-A1C7-0AA3B352B7E1")]
    public readonly Slot<Command> Output = new();

    public Execute()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var commands = Command.CollectedInputs;
        if (IsEnabled.GetValue(context))
        {
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

            // Cleanup runs backwards: the commands pushed their saved state in order, and PopState is a stack,
            // so restoring forwards hands each command the snapshot a later one took. That only stays invisible
            // while the pushed state groups do not overlap - a [ComputeShaderStage] saves the output merger
            // alongside the compute stage, so next to an [OutputMergerStage] the render targets come back wrong
            // and whatever draws afterwards goes nowhere.
            for (int i = commands.Count - 1; i >= 0; i--)
            {
                commands[i].Value?.RestoreAction?.Invoke(context);
            }
        }

        Command.DirtyFlag.Clear();
    }
        
    [Input(Guid = "5D73EBE6-9AA0-471A-AE6B-3F5BFD5A0F9C")]
    public readonly MultiInputSlot<Command> Command = new();

    [Input(Guid = "D68B5569-B43D-4A0D-9524-35289CE08098")]
    public readonly InputSlot<bool> IsEnabled = new();

}