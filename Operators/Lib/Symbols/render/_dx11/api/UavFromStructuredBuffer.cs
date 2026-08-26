namespace Lib.render._dx11.api;

[Guid("0b5b14bf-c850-493a-afb1-72643926e214")]
internal sealed class UavFromStructuredBuffer : Instance<UavFromStructuredBuffer>
{
    [Output(Guid = "7C9A5943-3DEB-4400-BDB2-99F56DD1976C")]
    public readonly Slot<UnorderedAccessView> UnorderedAccessView = new();

    public UavFromStructuredBuffer()
    {
        UnorderedAccessView.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var buffer = Buffer.GetValue(context);
        if (buffer == null)
            return;
            
        var bufferFlags = BufferFlags.GetValue(context);
        ResourceManager.CreateStructuredBufferUav(buffer, bufferFlags, ref UnorderedAccessView.Value);
        if (UnorderedAccessView.Value == null)
            return;

        if (UnorderedAccessView.Value != null)
        {
            UnorderedAccessView.Value.DebugName = SymbolChild.ReadableName;
            // Log.Info($"{symbolChild.ReadableName} updated with ref {UnorderedAccessView.DirtyFlag.Reference}", this);
        }
    }

    [Input(Guid = "5d888f13-0ad8-4034-99ca-da36c8fb261c")]
    public readonly InputSlot<Buffer> Buffer = new();

    [Input(Guid = "13B85721-7126-47BB-AB4F-096EAE59E412")]
    public readonly InputSlot<UnorderedAccessViewBufferFlags> BufferFlags = new();
}