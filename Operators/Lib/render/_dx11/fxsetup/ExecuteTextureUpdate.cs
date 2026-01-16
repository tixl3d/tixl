namespace Lib.render._dx11.fxsetup;

[Guid("6c2f8241-9f4b-451e-8a1d-871631d21163")]
internal sealed class ExecuteTextureUpdate : Instance<ExecuteTextureUpdate>
{
    [Output(Guid = "C955F2A2-9823-4844-AC11-98EA07DC50AA")]
    public readonly Slot<Texture2D> Output = new();

    public ExecuteTextureUpdate()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var isEnabled = IsEnabled.GetValue(context);
        if (!isEnabled || TriggerTexture.HasInputConnections && !TriggerTexture.DirtyFlag.IsDirty)
        {
            Output.DirtyFlag.Clear();
            Texture.DirtyFlag.Clear();
            if (Texture.HasInputConnections)
            {
                Texture.FirstConnection?.DirtyFlag.Clear();
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
        TriggerTexture.DirtyFlag.Clear();
            
        var inputBuffer = Texture.GetValue(context);
        Output.Value = inputBuffer;
    }

    [Input(Guid = "088ddcee-1407-4cd8-85bc-6704b8ea73b1")]
    public readonly InputSlot<Command> UpdateCommands = new();
        
    [Input(Guid = "5599A8AC-0686-4FA8-806C-52A44F910F11")]
    public readonly InputSlot<Texture2D> Texture = new();
        
    [Input(Guid = "DC3BD757-300B-416A-92F9-B9C976EF7206")]
    public readonly InputSlot<Texture2D> TriggerTexture = new();
        
    [Input(Guid = "6B1C2262-F7B1-4ABD-B787-D03240245431")]
    public readonly InputSlot<bool> IsEnabled = new();
}