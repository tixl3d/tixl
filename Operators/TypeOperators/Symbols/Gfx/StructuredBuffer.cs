namespace Types.Gfx;

[Guid("a8a0e6c4-1f49-4ed8-8d0b-e7aa6cdf8a87")]
public sealed class StructuredBuffer : Instance<StructuredBuffer>
{
    [Output(Guid = "C10E66C8-C887-4A82-B557-642990581767")]
    public readonly Slot<Buffer?> Buffer = new();

    public StructuredBuffer()
    {
        Buffer.UpdateAction += UpdateBuffer;
    }

    private void UpdateBuffer(EvaluationContext context)
    {
        int stride = Stride.GetValue(context);
        int sizeInBytes = stride * Count.GetValue(context);

        if (sizeInBytes <= 0)
            return;
        
        ResourceManager.SetupStructuredBuffer(sizeInBytes, stride, ref Buffer.Value);
    }

    [Input(Guid = "28E44436-F4E2-44EC-A28D-447E7A9F6BA8")]
    public readonly InputSlot<int> Stride = new();

    [Input(Guid = "B1CEDDFD-D289-41EB-BFC5-F36B789BFD4E")]
    public readonly InputSlot<int> Count = new();
}