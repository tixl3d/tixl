namespace Lib.flow.context;

[Guid("010aca02-263a-471c-b407-025b023f7f60")]
internal sealed class ExecuteRawBufferUpdate : Instance<ExecuteRawBufferUpdate>
{
    [Output(Guid = "DA3CD196-9454-438B-91B0-95486347902C")]
    public readonly Slot<Buffer> Output = new();

    public ExecuteRawBufferUpdate()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // This will execute the input
        UpdateCommands.GetValue(context);
        Output.Value = Buffer.GetValue(context);
    }

    [Input(Guid = "62AC9F97-1FCA-49F3-8FAC-F0A580C367BA")]
    public readonly InputSlot<Command> UpdateCommands = new();
        
    [Input(Guid = "8D7ADBC1-0D55-47E0-93C5-42926AF00771")]
    public readonly InputSlot<Buffer> Buffer = new();
}