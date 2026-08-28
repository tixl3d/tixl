#nullable enable
using SharpDX;

namespace Lib.numbers.@int.process;

[Guid("3dd5629e-8b23-4381-acf2-db642fc4b030")]
internal sealed class IntListToBuffer :Instance<IntListToBuffer>
{
    [Output(Guid = "47EE96FE-BAEC-4F2D-BEDC-0AC04F7720D9")]
    public readonly Slot<BufferWithViews?> Result = new();

    public IntListToBuffer()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var intList = IntList.GetValue(context);
        if (intList == null || intList.Count < 1)
        {
            Result.Value = null;
            return;
        }

        var arraySize = intList.Count;
        if (_intList.TypedElements == null || _intList.NumElements != arraySize)
        {
            _intList = new StructuredList<int>(arraySize); 
        }
        
        for (var index = 0; index < intList.Count; index++)
        {
            _intList[index] = intList[index];
        }
        
        // Upload
        var totalSizeInBytes = arraySize * 4;
            
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
        
        _bufferWithViews.Buffer = _buffer;
        ResourceManager.CreateStructuredBufferSrv(_buffer, ref _bufferWithViews.Srv);
        ResourceManager.CreateStructuredBufferUav(_buffer, UnorderedAccessViewBufferFlags.None, ref _bufferWithViews.Uav);

        Result.Value = _bufferWithViews;
    }
    
    private Buffer? _buffer;
    private readonly BufferWithViews _bufferWithViews = new();
    
    private StructuredList<int> _intList = new();
    
    
    [Input(Guid = "FDC560D8-0C47-4E31-AB93-A086468E8F96")]
    public readonly InputSlot<List<int>?> IntList = new();

}