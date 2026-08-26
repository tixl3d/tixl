using SharpDX.Direct3D11;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.render.@_;

[Guid("11ebbb25-984a-4772-b720-b8c7e5214a83")]
internal sealed class _ReadIntFromGpuBuffer : Instance<_ReadIntFromGpuBuffer>,IStatusProvider
{
    [Output(Guid = "63DB213C-98DA-4CC7-BA56-F534A7D9CD76")]
    public readonly Slot<Command> Execute = new();
        
    [Output(Guid = "6EBCEA92-1AC5-4214-B61E-ED27B8AAA742", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Output = new();

    public _ReadIntFromGpuBuffer()
    {
        Execute.UpdateAction = Update;
        Output.UpdateAction = UpdateResult;
    }

    private void UpdateResult(EvaluationContext context)
    {
        Output.Value = _lastResult;
    }

    private void Update(EvaluationContext context)
    {
        var inputBufferWithViews = InputBuffer.GetValue(context);
        if (inputBufferWithViews == null)
        {
            _statusMessage = "No input buffer";
            return;
        }
            
        InitOrUpdateBuffers(inputBufferWithViews.Buffer);
            
        var currentBuffer = _buffersWithCpuAccess[_currentBufferIndex];
            
        // Copy the GPU buffer to the staging buffer
        // the result will show up eventually
        var immediateContext = ResourceManager.Device.ImmediateContext;
        immediateContext.CopyResource(inputBufferWithViews.Buffer, currentBuffer);

        var previousBuffer = _buffersWithCpuAccess[(_currentBufferIndex-1).Mod(BufferCount)];
            
        _currentBufferIndex = (_currentBufferIndex + 1) % BufferCount;


        var index = Index.GetValue(context);
            
        try
        {
            _statusMessage = null;
            var maxIndex = previousBuffer.Description.SizeInBytes / sizeof(int);

            if (previousBuffer.Description.SizeInBytes == 0)
            {
                _statusMessage = $"Input buffer has zero size?";
                return;
            }
                
            if (index < 0 || index >= maxIndex)
            {
                index = index.Mod(maxIndex);
                _statusMessage = $"Index exceeds buffer size ({maxIndex}). Cycled to {index}";
            }
                
            immediateContext.MapSubresource(previousBuffer,
                                            MapMode.Read,
                                            MapFlags.None,
                                            out var int32Stream);
                
            int32Stream.Seek(index * sizeof(int), SeekOrigin.Begin);
            _lastResult = int32Stream.Read<int>();
            // Log.Debug("Read int from buffer: " + _lastResult, this);
                
            immediateContext.UnmapSubresource(previousBuffer, 0);
        }
        catch(Exception e)
        {
            _statusMessage = " Failed to read back data from GPU buffer " + e.Message;
            Log.Error(_statusMessage, this);
        }
    }
        
    private void InitOrUpdateBuffers(Buffer buffer)
    {
        if (buffer == null || buffer.IsDisposed)
            return;
            
        var doesFormatMatch = _buffersWithCpuAccess != null 
                              && _buffersWithCpuAccess[0].Description.SizeInBytes == buffer.Description.SizeInBytes
                              && _buffersWithCpuAccess[0].Description.StructureByteStride == buffer.Description.StructureByteStride;
            
        if (doesFormatMatch)
            return;

        if (_buffersWithCpuAccess == null)
        {
            _buffersWithCpuAccess = new Buffer[BufferCount];
        }
        else
        {
            DisposeBuffers();
        }
            
            
        var desc = new BufferDescription
                       {
                           SizeInBytes = buffer.Description.SizeInBytes,
                           Usage = ResourceUsage.Staging,
                           BindFlags = BindFlags.None,
                           CpuAccessFlags = CpuAccessFlags.Read,
                           OptionFlags = ResourceOptionFlags.None,
                           StructureByteStride = buffer.Description.StructureByteStride
                       };

        for (var i = 0; i < BufferCount; ++i)
        {
            _buffersWithCpuAccess[i] = new Buffer(ResourceManager.Device, desc);
        }
    }
        
    // Dispose of the buffers when the instance is destroyed
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeBuffers();
        }
            
        base.Dispose(disposing);
    }

    private void DisposeBuffers()
    {
        if(_buffersWithCpuAccess == null)
            return;
            
        for (var i = 0; i < BufferCount; i++)
        {
            Utilities.Dispose(ref _buffersWithCpuAccess[i]);
        }
    }
        
    string IStatusProvider.GetStatusMessage() => _statusMessage;
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() => string.IsNullOrEmpty(_statusMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
        
    private static string _statusMessage = "";

    private int _lastResult;
    private const int BufferCount = 1;
    private Buffer[] _buffersWithCpuAccess;
    private int _currentBufferIndex;
        
    // [Input(Guid = "746896e0-57e3-4aaf-b162-a94d25ef3156")]
    // public readonly InputSlot<Command> UpdateCommand = new();

    [Input(Guid = "1319EBA8-8F22-47D7-B8ED-2871B2F95E9B")]
    public readonly InputSlot<BufferWithViews> InputBuffer = new();
        
    [Input(Guid = "DE02BF72-426F-4548-89C7-D8D6A10AEA85")]
    public readonly InputSlot<int> Index = new();


}