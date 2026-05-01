using SharpDX.Direct3D11;
using T3.Core.Resource;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.point.helper;

[Guid("a5f4552f-7e25-43a5-bb14-21ab836fa0b3")]
internal sealed class PointsToCPU : Instance<PointsToCPU>
{
    [Output(Guid = "71EF183F-E387-4382-8488-FEC2DC27B1F1")]
    public readonly Slot<StructuredList> Output = new();

    public PointsToCPU()
    {
        Output.UpdateAction += Update;
    }
        
    private void Update(EvaluationContext context)
    {
        var updateContinuously = UpdateContinuously.GetValue(context);
        var async = Async.GetValue(context);

        try
        {
            var wasTriggered = MathUtils.WasTriggered(TriggerUpdate.GetValue(context), ref _triggerUpdate);

            if (wasTriggered)
            {
                TriggerUpdate.SetTypedInputValue(false);
            }
            var pointBuffer = PointBuffer.GetValue(context);

            if (pointBuffer == null)
            {
                return;
            }

            if (async && (updateContinuously || wasTriggered))
            {
                if (pointBuffer.Buffer == null || pointBuffer.Srv == null)
                    return;

                _pendingStartIndex = StartIndex.GetValue(context).ClampMin(0);
                _pendingMaxCount = MaxCount.GetValue(context);
                _pendingTotalElements = pointBuffer.Srv.Description.Buffer.ElementCount;
                _pendingStride = pointBuffer.Buffer.Description.StructureByteStride;

                _bufferReader.InitiateRead(pointBuffer.Buffer, _pendingTotalElements, _pendingStride, OnAsyncReadComplete);
                _bufferReader.Update();
                Output.DirtyFlag.Trigger = updateContinuously ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
                return;
            }

            var d3DDevice = ResourceManager.Device;
            var immediateContext = d3DDevice.ImmediateContext;

            if (wasTriggered
                || updateContinuously
                || _bufferWithViewsCpuAccess == null
                || _bufferWithViewsCpuAccess.Buffer == null
                || _bufferWithViewsCpuAccess.Buffer.Description.SizeInBytes != pointBuffer.Buffer.Description.SizeInBytes
                || _bufferWithViewsCpuAccess.Buffer.Description.StructureByteStride != pointBuffer.Buffer.Description.StructureByteStride
               )
            {
                try
                {
                    if (_bufferWithViewsCpuAccess != null)
                        Utilities.Dispose(ref _bufferWithViewsCpuAccess.Buffer);

                    _bufferWithViewsCpuAccess ??= new BufferWithViews();

                    if (_bufferWithViewsCpuAccess.Buffer == null ||
                        _bufferWithViewsCpuAccess.Buffer.Description.SizeInBytes != pointBuffer.Buffer.Description.SizeInBytes)
                    {
                        _bufferWithViewsCpuAccess.Buffer?.Dispose();
                        var bufferDesc = new BufferDescription
                                             {
                                                 Usage = ResourceUsage.Default,
                                                 BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
                                                 SizeInBytes = pointBuffer.Buffer.Description.SizeInBytes,
                                                 OptionFlags = ResourceOptionFlags.BufferStructured,
                                                 StructureByteStride = pointBuffer.Buffer.Description.StructureByteStride,
                                                 CpuAccessFlags = CpuAccessFlags.Read
                                             };
                        _bufferWithViewsCpuAccess.Buffer = new Buffer(ResourceManager.Device, bufferDesc);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Failed to setup structured buffer " + e.Message, this);
                    return;
                }

                ResourceManager.CreateStructuredBufferSrv(_bufferWithViewsCpuAccess.Buffer, ref _bufferWithViewsCpuAccess.Srv);

                // Keep a copy of the texture which can be accessed by CPU
                immediateContext.CopyResource(pointBuffer.Buffer, _bufferWithViewsCpuAccess.Buffer);
            }

            // Gets a pointer to the image data, and denies the GPU access to that subresource.            
            immediateContext.MapSubresource(_bufferWithViewsCpuAccess.Buffer, 0, MapMode.Read, MapFlags.None, out var sourceStream);

            using (sourceStream)
            {
                var startIndex = StartIndex.GetValue(context).ClampMin(0);
                var requestedMaxCount = MaxCount.GetValue(context);

                var elementCount = _bufferWithViewsCpuAccess.Buffer.Description.SizeInBytes /
                                   _bufferWithViewsCpuAccess.Buffer.Description.StructureByteStride;

                if (startIndex >= elementCount)
                {
                    Output.Value = new StructuredList<Point>(0);
                }
                else
                {
                    // MaxCount <= 0 means "read all remaining points".
                    var maxCount = requestedMaxCount > 0
                                       ? requestedMaxCount
                                       : int.MaxValue;
                    var outputCount = Math.Min(elementCount - startIndex, maxCount);

                    sourceStream.Position = (long)startIndex * _bufferWithViewsCpuAccess.Buffer.Description.StructureByteStride;

                    var points = outputCount > 0
                                     ? sourceStream.ReadRange<Point>(outputCount)
                                     : Array.Empty<Point>();

                    //Log.Debug($"Read {points.Length} elements", this);
                    Output.Value = new StructuredList<Point>(points);
                }
            }

            immediateContext.UnmapSubresource(_bufferWithViewsCpuAccess.Buffer, 0);
                
            Output.DirtyFlag.Trigger = updateContinuously ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
        }
        catch (Exception e)
        {
            Log.Error("Failed to fetch GPU resource " + e.Message);
        }
    }


    private void OnAsyncReadComplete(StructuredBufferReadAccess.ReadRequestItem item, IntPtr dataPointer, SharpDX.DataStream stream)
    {
        using (stream)
        {
            if (_pendingStartIndex >= _pendingTotalElements)
            {
                Output.Value = new StructuredList<Point>(0);
                return;
            }
            var maxCount = _pendingMaxCount > 0 ? _pendingMaxCount : int.MaxValue;
            var outputCount = Math.Min(_pendingTotalElements - _pendingStartIndex, maxCount);
            stream.Position = (long)_pendingStartIndex * _pendingStride;
            var points = outputCount > 0 ? stream.ReadRange<Point>(outputCount) : Array.Empty<Point>();
            Output.Value = new StructuredList<Point>(points);
        }
    }

    private bool _triggerUpdate;
    private BufferWithViews _bufferWithViewsCpuAccess = new();
    private readonly StructuredBufferReadAccess _bufferReader = new();
    private int _pendingStartIndex;
    private int _pendingMaxCount;
    private int _pendingTotalElements;
    private int _pendingStride;

        [Input(Guid = "F267534C-59AE-4758-B04A-13B6337BC0EB")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> PointBuffer = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "EFF239DA-39E9-41D3-968B-C74723EC2545")]
        public readonly InputSlot<bool> TriggerUpdate = new InputSlot<bool>();

        [Input(Guid = "77EE7CA9-A2DB-4DE9-BB9C-21EC4F1BBEAF")]
        public readonly InputSlot<bool> UpdateContinuously = new InputSlot<bool>();

        [Input(Guid = "41CC1BBC-2726-4980-93D8-010EA01E3F48")]
        public readonly InputSlot<int> StartIndex = new InputSlot<int>();

        [Input(Guid = "63014058-5418-4711-AE57-A9BA8841CB67")]
        public readonly InputSlot<int> MaxCount = new InputSlot<int>();

        [Input(Guid = "B2A36AB5-5B81-4B72-9F40-9E66D3DCF94B")]
        public readonly InputSlot<bool> Async = new InputSlot<bool>(false);
}