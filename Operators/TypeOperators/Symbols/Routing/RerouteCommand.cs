namespace Types.Routing;

/*
 * Routes commands through a stable Output.Value proxy: PrepareAction and RestoreAction inspect
 * the current source without evaluating it, while Update pulls Input between those callbacks.
 * This preserves prepare/evaluate/restore ordering even through chains of command anchors.
 * The .t3 has no direct connection, which would replace the proxy; disabled outputs suppress
 * forwarded callbacks, and the Editor prevents bypass from replacing the proxy as well.
 */
[Guid("f05c00a8-8d85-4cc9-9d85-09cfca9af1a4")]
public sealed class RerouteCommand : Instance<RerouteCommand>, IRerouteNode
{
    [Output(Guid = "af6a1761-07e3-40bb-9c3b-f760793050b5")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "3b74e35a-1b39-4a86-a00a-d266c2f3bcce")]
    public readonly InputSlot<Command> Input = new();

    public RerouteCommand()
    {
        Output.Value = new Command
                           {
                               PrepareAction = ForwardPrepare,
                               RestoreAction = ForwardRestore,
                           };
        Output.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        Input.GetValue(context);
    }

    private void ForwardPrepare(EvaluationContext context)
    {
        if (Output.IsDisabled)
            return;

        GetSourceCommand()?.PrepareAction?.Invoke(context);
    }

    private void ForwardRestore(EvaluationContext context)
    {
        if (Output.IsDisabled)
            return;

        GetSourceCommand()?.RestoreAction?.Invoke(context);
    }

    private Command? GetSourceCommand()
    {
        // Consumers prepare before pulling, so inspecting the source must not evaluate it.
        if (Input.FirstConnection is Slot<Command> source)
            return source.Value;

        return Input.Input.IsDefault ? Input.TypedDefaultValue.Value : Input.TypedInputValue.Value;
    }
}
