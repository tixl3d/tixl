using SharpDX.Direct3D;
using SharpDX.Direct3D11;

namespace Lib.render._dx11.api;

[Guid("8c41b312-6628-411c-a61d-604413b73a72")]
internal sealed class SrvFromStructuredBuffer : Instance<SrvFromStructuredBuffer>
{
    [Output(Guid = "2A1FCDF6-9416-45B2-96CA-A9D6D2692278")]
    public readonly Slot<ShaderResourceView> ShaderResourceView = new();

    [Output(Guid = "E96EED5C-AE63-49B7-8ADD-2A818E4A2B89")]
    public readonly Slot<int> ElementCount = new();
        
    public SrvFromStructuredBuffer()
    {
        ShaderResourceView.UpdateAction += Update;
        ElementCount.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var buffer = Buffer.GetValue(context);
        if (buffer != null)
        {
            if ((buffer.Description.OptionFlags & ResourceOptionFlags.BufferStructured) == 0)
            {
                Log.Warning($"{nameof(SrvFromStructuredBuffer)} - input buffer is not structured, skipping SRV creation.", this);
                return;
            }
            ShaderResourceView.Value?.Dispose();

            var elementCount = buffer.Description.SizeInBytes / buffer.Description.StructureByteStride;
            var desc = new ShaderResourceViewDescription()
                           {
                               Dimension = ShaderResourceViewDimension.ExtendedBuffer,
                               Format = Format.Unknown,
                               BufferEx = new ShaderResourceViewDescription.ExtendedBufferResource()
                                              {
                                                  FirstElement = 0,
                                                  ElementCount = elementCount
                                              }
                           };
            ShaderResourceView.Value = new ShaderResourceView(ResourceManager.Device, buffer, desc); // todo: create via resource manager
            ElementCount.Value = elementCount;
        }
    }

    [Input(Guid = "BD65EF2C-F32A-4279-BB5C-7F6467B23283")]
    public readonly InputSlot<Buffer> Buffer = new();
}