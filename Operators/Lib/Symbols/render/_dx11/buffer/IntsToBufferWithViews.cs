using SharpDX;

namespace Lib.render._dx11.buffer;


[Guid("c036b4f2-97bf-40f1-b4d5-c2067a4fa97f")]
internal sealed class IntsToBufferWithViews : Instance<IntsToBufferWithViews>
{
    [Output(Guid = "54b2df2e-3cc4-4dac-99d6-44a37842be60")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Output(Guid = "49508B09-B6A4-47D1-91E7-A68D72614A3F")]
    public readonly Slot<ShaderResourceView> Srv = new();
        
    [Output(Guid = "cd3f349f-97f8-48d0-9719-41ecd16c440b")]
    public readonly Slot<int> Length = new();

    public IntsToBufferWithViews()
    {
        OutBuffer.UpdateAction = Update;
        Srv.UpdateAction = Update;
        Length.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var connectedInputs = Lists.GetCollectedTypedInputs();
        var connectedInputsCount = connectedInputs.Count;
        if (connectedInputsCount == 0)
        {
            OutBuffer.Value = null;
            Length.Value = 0;
            return;
        }

        Lists.DirtyFlag.Clear();

        if(_intList.NumElements != connectedInputsCount)
            _intList = new StructuredList<int>(connectedInputsCount);
            
        for (var index = 0; index < connectedInputsCount; index++)
        {
            var value = connectedInputs[index].GetValue(context);
            _intList.TypedElements[index] = value;
        }
            
        var totalSizeInBytes = connectedInputsCount * 4;
            
        using var data = new DataStream(totalSizeInBytes, true, true);
        data.Position = 0;
        _intList.WriteToStream(data);
        data.Position = 0;

        try
        {
            ResourceManager.SetupStructuredBuffer(data, 
                                                  totalSizeInBytes, 
                                                  4, 
                                                  ref _buffer);
        }
        catch (Exception e)
        {
            Log.Error("Failed to setup structured buffer " + e.Message, this);
            return;
        }

        Length.Value = connectedInputsCount;
                    
        _bufferWithViews.Buffer = _buffer;
        ResourceManager.CreateStructuredBufferSrv(_buffer, ref _bufferWithViews.Srv);
        ResourceManager.CreateStructuredBufferUav(_buffer, UnorderedAccessViewBufferFlags.None, ref _bufferWithViews.Uav);

        OutBuffer.Value = _bufferWithViews;
        Srv.Value = _bufferWithViews.Srv;
            
        OutBuffer.DirtyFlag.Clear();
        Srv.DirtyFlag.Clear();
        Length.DirtyFlag.Clear();
    }
        
    private StructuredList<int> _intList = new(1);
        
    [StructLayout(LayoutKind.Explicit, Size = 1 * 4)]
    public struct Integer
    {
        [FieldOffset(0 * 4)]
        public int Value;
    }
        
    private Buffer _buffer;
    private readonly BufferWithViews _bufferWithViews = new();

    [Input(Guid = "FC2F2627-889E-469B-B57D-C9B855883759")]
    public readonly MultiInputSlot<int> Lists = new();
}