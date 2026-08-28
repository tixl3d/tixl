namespace Lib.render._dx11.fxsetup;

[Guid("58351c8f-4a73-448e-b7bb-69412e71bd76")]
internal sealed class ExecuteBufferUpdate : Instance<ExecuteBufferUpdate>
{
    [Output(Guid = "9A66687E-A834-452C-A652-BA1FC70C2C7B")]
    public readonly Slot<BufferWithViews> Output2 = new();

        
    public ExecuteBufferUpdate()
    {
        Output2.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (!IsEnabled.GetValue(context))
        {
            UpdateCommand.DirtyFlag.Clear();
            BufferWithViews.DirtyFlag.Clear();
            return;
        }

        // This will execute the input
        UpdateCommand.GetValue(context);
            
        Output2.Value = BufferWithViews.GetValue(context);
    }

    [Input(Guid = "51110D89-083E-42B8-B566-87B144DFBED9")]
    public readonly InputSlot<Command> UpdateCommand = new();
        
    [Input(Guid = "72CFE742-88FB-41CD-B6CF-D96730B24B23")]
    public readonly InputSlot<BufferWithViews> BufferWithViews = new();
        
    [Input(Guid = "6887F319-CF3F-4E87-9A8C-A7C912DBF5AD")]
    public readonly InputSlot<bool> IsEnabled = new();
        
}