#nullable enable
using SharpDX;

namespace Lib.flow.skillQuest;

[Guid("53a8a318-81dc-4c76-96a1-f86d0e2fb1d7")]
public sealed class _ReadBackImageDifference : Instance<_ReadBackImageDifference>
{
    [Output(Guid = "AC586AD2-075C-4365-95C3-9E8C97CB3AF4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Result = new();

    public _ReadBackImageDifference()
    {
        Result.UpdateAction = Update;
    }
    
    
    private void Update(EvaluationContext context)
    {
        var bufferWithViews = Differences.GetValue(context);

        if (bufferWithViews == null)
        {
            Log.Warning("Missing input", this);
            Result.Value = -1;
            return;
        }

        _bufferReader.InitiateRead(bufferWithViews.Buffer,
                                   bufferWithViews.Srv.Description.Buffer.ElementCount,
                                   bufferWithViews.Buffer.Description.StructureByteStride,
                                   OnPointsReadComplete);
        _bufferReader.Update();

        if (_imageDifference.Length > 0)
        {
            Result.Value = _imageDifference[0].Difference;
        }
    }

    
    private void OnPointsReadComplete(StructuredBufferReadAccess.ReadRequestItem readItem, IntPtr dataPointer, DataStream? dataStream)
    {
        if (dataStream == null)
            return;

        var count = readItem.ElementCount;
        if (_imageDifference.Length != count)
            _imageDifference = new ImageDifference[count];

        using (dataStream)
        {
            dataStream.ReadRange(_imageDifference, 0, count);
        }
    }
    
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _bufferReader.Dispose();
    }
    

    [StructLayout(LayoutKind.Explicit, Size = Stride)]
    private struct ImageDifference
    {
        [FieldOffset(0)]
        public int Difference;

        [Newtonsoft.Json.JsonIgnore]
        public const int Stride = 1 * 4;
    }

    private readonly StructuredBufferReadAccess _bufferReader = new();
    private ImageDifference[] _imageDifference = [];

    //private readonly BufferWithViews _visualizeBuffer = new(); // Initialize here

    [Input(Guid = "18306295-6ea8-48f9-87bb-fc36fa1ab10c")]
    public readonly InputSlot<BufferWithViews> Differences = new();
}