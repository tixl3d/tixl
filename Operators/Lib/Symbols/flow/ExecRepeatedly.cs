using T3.Core.Utils;

namespace Lib.flow;

[Guid("46420979-1e56-4de3-a6ca-0447be1b9813")]
internal sealed class ExecRepeatedly : Instance<ExecRepeatedly>
{
    [Output(Guid = "5008c453-89ae-456b-9468-917abcb0af2e")]
    public readonly Slot<Command> Output = new();

    public ExecRepeatedly()
    {
        Output.UpdateAction += Update;
    }

    private int _callsSinceLastRefresh;
        
    private void Update(EvaluationContext context)
    {
        _callsSinceLastRefresh++;
            
        var repeatCount = RepeatCount.GetValue(context).Clamp(0, 100);
        if (repeatCount <= 0)
            return;

        var skipFrames = SkipFrameCount.GetValue(context).Clamp(0,10000);
        if (_callsSinceLastRefresh <= skipFrames)
        {
            return;
        }

        _callsSinceLastRefresh = 0;
            
        var commands = Command.CollectedInputs;

        // do preparation if needed
        for (int i = 0; i < commands.Count; i++)
        {
            commands[i].Value?.PrepareAction?.Invoke(context);
        }
            
        // execute commands
        for (int repeation = 0; repeation < repeatCount; repeation++)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                commands[i].GetValue(context);
            }
        }

        // cleanup after usage
        for (int i = 0; i < commands.Count; i++)
        {
            commands[i].Value?.RestoreAction?.Invoke(context);
        }

        Command.DirtyFlag.Clear();
    }

    [Input(Guid = "d9de54b8-6d05-4cad-a1eb-bfa770a4520d")]
    public readonly MultiInputSlot<Command> Command = new();

    [Input(Guid = "FB4C2356-5FA9-4BEB-A909-805323D5F7C1")]
    public readonly InputSlot<int> RepeatCount = new();
        
    [Input(Guid = "4CA4CF19-975E-479A-BD43-C49C96CE51B6")]
    public readonly InputSlot<int> SkipFrameCount = new();

}