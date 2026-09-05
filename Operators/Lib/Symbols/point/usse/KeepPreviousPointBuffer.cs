using SharpDX.Direct3D;
using SharpDX.Direct3D11;

namespace Lib.point.usse;

[Guid("c7752832-7155-4082-acd5-276c1abe7eb4")]
internal sealed class KeepPreviousPointBuffer : Instance<KeepPreviousPointBuffer>
{
    [Output(Guid = "8c301f66-a743-4c1e-9f2b-fa9cb5904c98")]
    public readonly Slot<BufferWithViews> BufferA = new();

    [Output(Guid = "74a47202-3c66-476d-a850-fbb2b37dffe5")]
    public readonly Slot<BufferWithViews> BufferB = new();

    public KeepPreviousPointBuffer()
    {
        BufferA.UpdateAction += UpdateTexture;
        BufferB.UpdateAction += UpdateTexture;
    }

    private void UpdateTexture(EvaluationContext context)
    {
        var keep = Keep.GetValue(context);
        if (!InputBuffer.HasInputConnections || !keep)
            return;

        var src = InputBuffer.GetValue(context);
        if (src?.Buffer == null)
            return;

        var description = src.Buffer.Description;

        // Must be structured
        if ((description.OptionFlags & ResourceOptionFlags.BufferStructured) == 0 || description.StructureByteStride == 0)
        {
            Log.Warning("Input is not a structured buffer.");
            return;
        }

        var changed =
            _prevDesc.SizeInBytes == 0 ||
            _a == null || _b == null ||
            _a.Buffer == null || _b.Buffer == null ||
            _a.Buffer.IsDisposed || _b.Buffer.IsDisposed ||
            description.SizeInBytes != _prevDesc.SizeInBytes ||
            description.StructureByteStride != _prevDesc.StructureByteStride;

        try
        {
            if (changed)
            {
                DisposePrev();

                var newDesc = new BufferDescription
                               {
                                   SizeInBytes = description.SizeInBytes,
                                   BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                                   CpuAccessFlags = CpuAccessFlags.None,
                                   OptionFlags = ResourceOptionFlags.BufferStructured,
                                   Usage = ResourceUsage.Default,
                                   StructureByteStride = description.StructureByteStride
                               };

                _a = CreateBuffer(newDesc);
                _b = CreateBuffer(newDesc);
                _prevDesc = newDesc;
            }

            var dst = _toggle ? _a.Buffer : _b.Buffer;
            ResourceManager.Device.ImmediateContext.CopyResource(src.Buffer, dst);
        }
        catch (Exception e)
        {
            Log.Error($"Structured buffer keep failed: {e.Message}", this);
            return;
        }

        // Expose SRVs/UAVs
        BufferA.Value = _toggle ? _a : _b;
        BufferB.Value = _toggle ? _b : _a;

        BufferA.DirtyFlag.Clear();
        BufferB.DirtyFlag.Clear();

        _toggle = !_toggle;
    }

    private static BufferWithViews CreateBuffer(BufferDescription desc)
    {
        var dev = ResourceManager.Device;
        var buf = new Buffer(dev, desc);

        var elementCount = desc.SizeInBytes / Math.Max(1, desc.StructureByteStride);

        var srv = new ShaderResourceView(dev, buf, new ShaderResourceViewDescription
                                                       {
                                                           Format = SharpDX.DXGI.Format.Unknown,
                                                           Dimension = ShaderResourceViewDimension.Buffer,
                                                           Buffer = new ShaderResourceViewDescription.BufferResource
                                                                        {
                                                                            FirstElement = 0,
                                                                            ElementCount = elementCount
                                                                        }
                                                       });

        var uav = new UnorderedAccessView(dev, buf, new UnorderedAccessViewDescription
                                                        {
                                                            Format = SharpDX.DXGI.Format.Unknown,
                                                            Dimension = UnorderedAccessViewDimension.Buffer,
                                                            Buffer = new UnorderedAccessViewDescription.BufferResource
                                                                         {
                                                                             FirstElement = 0,
                                                                             ElementCount = elementCount,
                                                                             Flags = UnorderedAccessViewBufferFlags.None
                                                                         }
                                                        });

        return new BufferWithViews { Buffer = buf, Srv = srv, Uav = uav };
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        DisposePrev();
    }

    private void DisposePrev()
    {
        _a?.Dispose();
        _b?.Dispose();
        _a = null;
        _b = null;
        _prevDesc = default;
    }

    private bool _toggle;
    private BufferWithViews _a, _b;
    private BufferDescription _prevDesc;

    [Input(Guid = "07ca7e81-02b8-4cb4-92f4-7afaaa07b751")]
    public readonly InputSlot<BufferWithViews> InputBuffer = new();

    [Input(Guid = "513d0146-e0c0-4e27-a3dc-862c1f56e219")]
    public readonly InputSlot<bool> Keep = new();
}