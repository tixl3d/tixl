namespace Lib.render._dx11.fxsetup;

[Guid("4c93c6e6-da4d-4255-a2e1-eca388eb775f")]
internal sealed class ExecuteValueUpdate : Instance<ExecuteValueUpdate>
{
    [Output(Guid = "98A58913-8ABE-44D5-8F92-539C6C600329")]
    public readonly Slot<float> Output = new();

    public ExecuteValueUpdate()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var isEnabled = IsEnabled.GetValue(context);
        if (!isEnabled)
        {
            Output.DirtyFlag.Clear();
            InputValue.DirtyFlag.Clear();
            if (InputValue.HasInputConnections)
            {
                InputValue.FirstConnection?.DirtyFlag.Clear();
            }
            
            UpdateCommands.DirtyFlag.Clear();
            return;
        }
            
        if (UpdateCommands.HasInputConnections && UpdateCommands.DirtyFlag.IsDirty)
        {
            // This will execute the input
            UpdateCommands.GetValue(context);
        }
        UpdateCommands.DirtyFlag.Clear();

        Output.Value = InputValue.GetValue(context);
    }

    [Input(Guid = "cb6c5ef9-6262-4962-a6b9-cbd8e787a02b")]
    public readonly InputSlot<Command> UpdateCommands = new();
        
        
    [Input(Guid = "C0631CFA-C130-4821-AF83-20C2945D2FE5")]
    public readonly InputSlot<float> InputValue = new();
        
    [Input(Guid = "d1d1d97f-fdb6-4b68-8f6b-b1cacf71a7be")]
    public readonly InputSlot<bool> IsEnabled = new();
}