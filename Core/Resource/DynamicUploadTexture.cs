using T3.Graphics.Compat;
using T3.Graphics;
using System;

using T3.Core.DataTypes.Vector;

namespace T3.Core.Rendering.UploadPipeline;

public sealed class DynamicUploadTexture : IDisposable
{
    private readonly T3.Graphics.Compat.Device device;
    private Texture2D texture;
    private ShaderResourceView readView;

    public Texture2D Texture => this.texture;
    public ShaderResourceView ReadView => this.readView;

    public DynamicUploadTexture(T3.Graphics.Compat.Device device, Int2 size, Format format)
    {
        if (device == null)
            throw new ArgumentNullException(nameof(device));

        this.device = device;
        this.CreateTexture(size, format);
    }

    public void Update(Int2 size, Format format)
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

    private void CreateTexture(Int2 size, Format format)
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
        var dataBox = deviceContext.MapSubresourceWithSize(this.texture, 0, 0, MapMode.WriteDiscard,
                                                        T3.Graphics.Compat.MapFlags.None, out _);

        T3.Core.Utils.Utilities.CopyImageMemory(data, dataBox.DataPointer, this.texture.Description.Height,
                                                srcStride, dataBox.RowPitch);

        // release our resources
        deviceContext.UnmapSubresource(this.texture, 0);
    }

    public void Dispose()
    {
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref texture);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref readView);
    }
}