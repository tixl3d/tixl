using SharpDX.Direct3D11;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.io.dmx.obsolete;

[Guid("0e881c4d-c4ed-42c1-a2c9-d076b0489395")]
public sealed class PointsToRGBList : Instance<PointsToRGBList>
{
    [Output(Guid = "74a3f459-f0d6-411c-9dc8-d3748f019fad")]
    public readonly Slot<List<float>> Result = new(new List<float>(20));

    public PointsToRGBList()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var updateContinuously = true;
            
        try
        {

            var pointBuffer = Points.GetValue(context);

            if (pointBuffer == null)
            {
                return;
            }


            var d3DDevice = ResourceManager.Device;
            var immediateContext = d3DDevice.ImmediateContext;

            if (updateContinuously
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
            var sourceDataBox =
                immediateContext.MapSubresource(_bufferWithViewsCpuAccess.Buffer, 0, MapMode.Read, MapFlags.None, out var sourceStream);

            using (sourceStream)
            {
                var elementCount = _bufferWithViewsCpuAccess.Buffer.Description.SizeInBytes /
                                   _bufferWithViewsCpuAccess.Buffer.Description.StructureByteStride;
                    
                var points = sourceStream.ReadRange<Point>(elementCount);
                    
                //Log.Debug($"Read {points.Length} elements", this);
                //Output.Value = new StructuredList<Point>(points);
                var items = new List<float>();

                for (var connectionIndex = 0; connectionIndex < points.Length; connectionIndex++)
                {
                    var point = points[connectionIndex];
                    var factorR = (point.Color.X - 0f) / (1f - 0f);
                    var vR = factorR * (255.0f - 0.0f) + 0.0f;
                    var factorG = (point.Color.Y - 0.0f) / (1.0f - 0.0f);
                    var vG = factorG * (255.0f - 0.0f) + 0.0f;
                    var factorB = (point.Color.Z - 0.0f) / (1.0f - 0.0f);
                    var vB = factorB * (255.0f - 0.0f) + 0.0f;
                    items.Add((float)Math.Round(vR));
                    items.Add((float)Math.Round(vG));
                    items.Add((float)Math.Round(vB));
                }

                Result.Value = items;
            }

            immediateContext.UnmapSubresource(_bufferWithViewsCpuAccess.Buffer, 0);
                
            Result.DirtyFlag.Trigger = updateContinuously ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
        }
        catch (Exception e)
        {
            Log.Error("Failed to fetch GPU resource " + e.Message);
        }
    }



    private BufferWithViews _bufferWithViewsCpuAccess = new();

    [Input(Guid = "b98449e7-b7ef-4b15-883c-28bf7662c987")]
    public readonly InputSlot<BufferWithViews> Points = new InputSlot<BufferWithViews>();
}