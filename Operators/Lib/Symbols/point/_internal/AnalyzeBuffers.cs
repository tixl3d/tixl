using T3.Core.Utils;

namespace Lib.point._internal;

[Guid("7ad3a38a-9f04-43ba-a16f-6982b87dd2d4")]
internal sealed class AnalyzeBuffers : Instance<AnalyzeBuffers>
{
    [Output(Guid = "4906B2CE-7AAF-4025-B48E-49E6D660C13B")]
    public readonly Slot<int> BufferCount = new();

    [Output(Guid = "5638E071-A0A7-4EE2-AA04-9B651821BEBB")]
    public readonly Slot<BufferWithViews> SelectedBuffer = new();

    [Output(Guid = "D7BBD6D5-57EB-4C3A-8C84-E497E490AF83")]
    public readonly Slot<int> StartPositionForSelected = new();

    [Output(Guid = "0702a722-0b93-4840-9abd-f8ee348c3647")]
    public readonly Slot<int> TotalSize = new();
        
    [Output(Guid = "79FE54BE-6841-4F4D-8216-0FA26FF21F21")]
    public readonly Slot<int> Stride = new();

    public AnalyzeBuffers()
    {
        BufferCount.UpdateAction += Update;
        StartPositionForSelected.UpdateAction += Update;
        TotalSize.UpdateAction += Update;
        SelectedBuffer.UpdateAction += Update;
        Stride.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var connections = Input.GetCollectedTypedInputs();
        var selectedIndex = Index.GetValue(context).Clamp(0, connections.Count-1);

        if (connections.Count == 0)
        {
            TotalSize.Value = 0;
            StartPositionForSelected.Value = 0;
            BufferCount.Value = 0;
            return;
        }

        var totalSize = 0;
        var startPosition = 0;
        BufferWithViews selectedBuffer = null;
        var hadErrors = false;
        for (var connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
        {
            var input = connections[connectionIndex];
                
            var bufferWithViews = input.GetValue(context);
            if (bufferWithViews !=null && bufferWithViews.Srv != null && !bufferWithViews.Srv.IsDisposed)
            {
                var length = bufferWithViews.Srv.Description.Buffer.ElementCount;

                if (connectionIndex == selectedIndex)
                {
                    startPosition = totalSize;
                    selectedBuffer = input.Value;
                }
                totalSize += length;
            }
            else
            {
                hadErrors = true;
                if (_complainedOnce)
                    continue;
                    
                Log.Warning($"Undefined BufferWithViews at index {connectionIndex}", this);
                _complainedOnce = true;
            }
        }

        if (!hadErrors)
        {
            _complainedOnce = false;
        }

        SelectedBuffer.Value = selectedBuffer;
        StartPositionForSelected.Value = startPosition;
        BufferCount.Value = connections.Count; 
        TotalSize.Value = totalSize;
        if (selectedBuffer?.Buffer != null)
        {
            Stride.Value = selectedBuffer.Buffer.Description.StructureByteStride;
        }
    }

    private bool _complainedOnce;
        
    [Input(Guid = "c8a5769e-2536-4caa-8380-22fbeed1ef12")]
    public readonly MultiInputSlot<BufferWithViews> Input = new();

    [Input(Guid = "bf9c64ac-39b5-41c0-a896-84809b12fff6")]
    public readonly InputSlot<int> Index = new();

}