#if PLATFORM_WINDOWS
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;

namespace T3.Core.Rendering.UploadPipeline;

public sealed class DynamicUploadTexture : IDisposable
{
    private readonly SharpDX.Direct3D11.Device device;
    private Texture2D texture;
    private ShaderResourceView readView;

    public Texture2D Texture => this.texture;
    public ShaderResourceView ReadView => this.readView;

    public DynamicUploadTexture(SharpDX.Direct3D11.Device device, Size2 size, Format format)
    {
        if (device == null)
            throw new ArgumentNullException(nameof(device));

        this.device = device;
        this.CreateTexture(size, format);
    }

    public void Update(Size2 size, Format format)
    {
        if (texture != null)
        {
            if (texture.Description.Width != size.Width 
                || texture.Description.Height != size.Height
                || texture.Description.Format != format)
            {
                Dispose();
            }
        }

        if (texture == null)
        {
            this.CreateTexture(size, format);
        }
    }

    private void CreateTexture(Size2 size, Format format)
    {
        var imageDesc = new Texture2DDescription
                            {
                                BindFlags = BindFlags.ShaderResource,
                                Format = format,
                                Width = size.Width,
                                Height = size.Height,
                                MipLevels = 1,
                                SampleDescription = new SampleDescription(1, 0),
                                Usage = ResourceUsage.Dynamic,
                                OptionFlags = ResourceOptionFlags.None,
                                CpuAccessFlags = CpuAccessFlags.Write,
                                ArraySize = 1
                            };

        this.texture = new Texture2D(this.device, imageDesc);
        this.readView = new ShaderResourceView(this.device, this.texture);
    }

    public void WriteData(DeviceContext deviceContext, IntPtr data, int size, int srcStride)
    {
        var dataBox = deviceContext.MapSubresource(this.texture, 0, 0, MapMode.WriteDiscard,
                                                   SharpDX.Direct3D11.MapFlags.None, out int _);

        T3.Core.Utils.Utilities.CopyImageMemory(data, dataBox.DataPointer, this.texture.Description.Height,
                                                srcStride, dataBox.RowPitch);

        // release our resources
        deviceContext.UnmapSubresource(this.texture, 0);
    }

    public void Dispose()
    {
        Utilities.Dispose(ref texture);
        Utilities.Dispose(ref readView);
    }
}
#endif // PLATFORM_WINDOWS
