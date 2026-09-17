namespace Types.Routing;

/// <summary>
/// Routes commands through a stable Output.Value proxy: PrepareAction and RestoreAction inspect
/// the current source without evaluating it, while Update pulls Input between those callbacks.
/// This preserves prepare/evaluate/restore ordering even through chains of command anchors.
/// The .t3 has no direct connection, which would replace the proxy; disabled outputs suppress
/// forwarded callbacks, and the Editor prevents bypass from replacing the proxy as well.
/// </summary>
[Guid("f05c00a8-8d85-4cc9-9d85-09cfca9af1a4")]
public sealed class RerouteCommand : Instance<RerouteCommand>, IRerouteNode
{
    /// <summary>Stable command proxy that forwards source callbacks without replacing its identity.</summary>
    [Output(Guid = "af6a1761-07e3-40bb-9c3b-f760793050b5")]
    public readonly Slot<Command> Output = new();

    /// <summary>Upstream command evaluated between the forwarded prepare and restore callbacks.</summary>
    [Input(Guid = "3b74e35a-1b39-4a86-a00a-d266c2f3bcce")]
    public readonly InputSlot<Command> Input = new();

    /// <summary>Installs the stable callback proxy and the input-pulling evaluator.</summary>
    public RerouteCommand()
    {
        Output.Value = new Command
                           {
                               PrepareAction = ForwardPrepare,
                               RestoreAction = ForwardRestore,
                           };
        Output.UpdateAction = Update;
    }

    /// <summary>Evaluates the current input between the consumer prepare and restore phases.</summary>
    /// <param name="context">Evaluation state passed unchanged to the upstream command input.</param>
    private void Update(EvaluationContext context)
    {
        Input.GetValue(context);
    }

    /// <summary>Forwards prepare to the current source without evaluating it, unless the output is disabled.</summary>
    /// <param name="context">Evaluation state passed unchanged to the current source's prepare callback.</param>
    private void ForwardPrepare(EvaluationContext context)
    {
        if (Output.IsDisabled)
            return;

        GetSourceCommand()?.PrepareAction?.Invoke(context);
    }

    /// <summary>Forwards restore to the current source without evaluating it, unless the output is disabled.</summary>
    /// <param name="context">Evaluation state passed unchanged to the current source's restore callback.</param>
    private void ForwardRestore(EvaluationContext context)
    {
        if (Output.IsDisabled)
            return;

        GetSourceCommand()?.RestoreAction?.Invoke(context);
    }

    /// <summary>Reads the connected command or local input value without triggering evaluation.</summary>
    /// <returns>Current connected command, or the local input value/default when unconnected; may be null.</returns>
    private Command? GetSourceCommand()
    {
        // Consumers prepare before pulling, so inspecting the source must not evaluate it.
        if (Input.FirstConnection is Slot<Command> source)
            return source.Value;

        return Input.Input.IsDefault ? Input.TypedDefaultValue.Value : Input.TypedInputValue.Value;
    }
}
