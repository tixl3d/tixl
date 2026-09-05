namespace Lib.point._experimental;

[Guid("9cdcfa49-609d-4a64-ae97-8f98567075d1")]
internal sealed class KeepBufferReference : Instance<KeepBufferReference>
{
    [Output(Guid = "437C53CC-F949-4369-A2BE-F7C66557EEA7")]
    public readonly Slot<BufferWithViews> Result = new();

    public KeepBufferReference()
    {
        Result.UpdateAction = Update;
    }
        
    private void Update(EvaluationContext context)
    {
        _bufferWithViews = Buffer.GetValue(context);
        Result.Value = _bufferWithViews;
            
        var obj = BufferReference.GetValue(context);
            
        if(obj is not List<BufferWithViews> bufferList)
        {
            Log.Warning("reference was not a BufferWithViews", this);
            return;
        }
            
        if(bufferList.Count != 1)
        {
            bufferList.Clear();
            bufferList.Add(_bufferWithViews);
        }
        else
        {
            bufferList[0] = _bufferWithViews;
        }
            
        //BufferReference.Value = _reference;
        //Log.Debug($"Set reference to {_bufferWithViews}", this);
    }
        
    [Input(Guid = "1904C4C7-3288-42FB-A5CB-B76E38780376")]
    public readonly InputSlot<BufferWithViews> Buffer = new();

    [Input(Guid = "62A6DF26-0BA4-41D5-A2E3-CAAEA5315B37")]
    public readonly InputSlot<Object> BufferReference = new();
        
        
    private BufferWithViews _bufferWithViews;
}