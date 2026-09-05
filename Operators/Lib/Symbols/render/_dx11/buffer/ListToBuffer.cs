using SharpDX;

namespace Lib.render._dx11.buffer;


[Guid("7e28c796-85e7-47ee-99bb-9599284dbeeb")]
internal sealed class ListToBuffer : Instance<ListToBuffer>
{
    [Output(Guid = "c52dfa83-9820-4a54-b90b-62278dc8fe3f")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Output(Guid = "e1775fdf-af5a-49b2-b6fc-20e2180b71f5")]
    public readonly Slot<int> Length = new();

    public ListToBuffer()
    {
        OutBuffer.UpdateAction += Update;
        Length.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var listsCollectedInputs = Lists.CollectedInputs.Select(c => c.GetValue(context)).Where(c => c != null).ToList();
        Lists.DirtyFlag.Clear();

        if (listsCollectedInputs.Count == 0)
        {
            OutBuffer.Value = null;
            Length.Value = 0;
            return;
        }

        var totalSizeInBytes = 0;
        foreach (var entry in listsCollectedInputs)
        {
            if (entry == null)
                continue;

            totalSizeInBytes += entry.TotalSizeInBytes;
        }

        if (totalSizeInBytes == 0)
        {
            _buffer = null;
            Length.Value = 0;
        }
        else
        {
            using (var data = new DataStream(totalSizeInBytes, true, true))
            {
                foreach (var structuredList in listsCollectedInputs)
                {
                    structuredList?.WriteToStream(data);
                }

                data.Position = 0;

                var firstInputList = listsCollectedInputs.FirstOrDefault();
                var elementSizeInBytes = firstInputList?.ElementSizeInBytes ?? 0; // todo: add check that all inputs have same type
                try
                {
                    ResourceManager.SetupStructuredBuffer(data, totalSizeInBytes, elementSizeInBytes, ref _buffer);
                }
                catch (Exception e)
                {
                    Log.Error("Failed to setup structured buffer " + e.Message, this);
                    return;
                }

                var elementCount = totalSizeInBytes / elementSizeInBytes;
                Length.Value = elementCount;
            }
            ResourceManager.CreateStructuredBufferSrv(_buffer, ref _bufferWithViews.Srv);
            ResourceManager.CreateStructuredBufferUav(_buffer, UnorderedAccessViewBufferFlags.None, ref _bufferWithViews.Uav);
        }

        _bufferWithViews.Buffer = _buffer;
        OutBuffer.Value = _bufferWithViews;
            
    }

    private Buffer _buffer;
    private readonly BufferWithViews _bufferWithViews = new();

    [Input(Guid = "08F181BB-9777-497C-871D-BCC1FF252F2F")]
    public readonly MultiInputSlot<StructuredList> Lists = new();
}