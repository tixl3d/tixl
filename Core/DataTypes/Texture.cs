#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using JeremyAnsel.Media.Dds;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.WIC;
using T3.Core.Logging;
using T3.Core.Resource;
using T3.Core.Resource.Dds;
using Device = SharpDX.Direct3D11.Device;

namespace T3.Core.DataTypes;

public sealed class Texture2D(SharpDX.Direct3D11.Texture2D texture) : Texture<SharpDX.Direct3D11.Texture2D>(texture)
{
    public override string Name { get => TextureObject.DebugName; set => TextureObject.DebugName = value; }
    public readonly Texture2DDescription Description = texture.Description;

    public static Texture2D CreateFromBitmap(Device device, BitmapSource bitmapSource)
    {
        // Allocate DataStream to receive the WIC image pixels
        var stride = bitmapSource.Size.Width * 4;
        using var buffer = new DataStream(bitmapSource.Size.Height * stride, true, true);

        // Copy the content of the WIC to the buffer
        bitmapSource.CopyPixels(stride, buffer);
        int mipLevels = (int)Math.Log(bitmapSource.Size.Width, 2.0) + 1;
        var texDesc = new Texture2DDescription
                          {
                              Width = bitmapSource.Size.Width,
                              Height = bitmapSource.Size.Height,
                              ArraySize = 1,
                              BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                              Usage = ResourceUsage.Default,
                              CpuAccessFlags = CpuAccessFlags.None,
                              Format = Format.R8G8B8A8_UNorm,
                              MipLevels = mipLevels,
                              OptionFlags = ResourceOptionFlags.GenerateMipMaps,
                              SampleDescription = new SampleDescription(1, 0),
                          };
        
        var dataRectangles = new DataRectangle[mipLevels];
        for (var i = 0; i < mipLevels; i++)
        {
            dataRectangles[i] = new DataRectangle(buffer.DataPointer, stride);
            stride /= 2;
        }

        var dxTexture = new SharpDX.Direct3D11.Texture2D(device, texDesc, dataRectangles);
        return new Texture2D(dxTexture);
    }

    public static Texture2D CreateTexture2D(Texture2DDescription description, DataRectangle[]? dataRectangles = null)
    {
        var dxTexture = new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, description, dataRectangles);
        return new Texture2D(dxTexture);
    }

    internal static bool TryLoadFromStream(FileStream stream, [NotNullWhen(true)] out Texture2D? texture,
                                           [NotNullWhen(false)] out string? failureReason)
    {
        var extension = Path.GetExtension(stream.Name);
        if (string.Equals(extension, ".dds", StringComparison.OrdinalIgnoreCase))
        {
            var ddsFile = DdsFile.FromStream(stream);
            try
            {
                DdsDirectX.CreateTexture(ddsFile, ResourceManager.Device, ResourceManager.Device.ImmediateContext, out var dxTextureResource, out var srv);
                srv?.Dispose();
                var dxTex = (SharpDX.Direct3D11.Texture2D)dxTextureResource;
                texture = new Texture2D(dxTex);
            }
            catch (Exception e)
            {
                failureReason = $"Failed to create texture from DDS: {e}";
                texture = null;
                return false;
            }
        }
        else
        {
            if (!TryCreate(stream, out texture, out failureReason))
            {
                failureReason = "Failed to create texture";
                return false;
            }
        }
        
        failureReason = null;
        return true;
    }

    private static bool TryCreate(Stream stream, [NotNullWhen(true)] out Texture2D? texture, [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            using var factory = new ImagingFactory();
            using var bitmapDecoder = new BitmapDecoder(factory, stream, DecodeOptions.CacheOnDemand);
            using var formatConverter = new FormatConverter(factory);
            using var bitmapFrameDecode = bitmapDecoder.GetFrame(0);
            formatConverter.Initialize(bitmapFrameDecode, PixelFormat.Format32bppRGBA, BitmapDitherType.None, null, 0.0, BitmapPaletteType.Custom);

            
            texture = CreateFromBitmap(ResourceManager.Device, formatConverter);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            failureReason = e.Message;
            Log.Info($"Info: couldn't access file: {e.Message}.");
            texture = null;
            return false;
        }
    }
}
public sealed class Texture3D(SharpDX.Direct3D11.Texture3D texture) : Texture<SharpDX.Direct3D11.Texture3D>(texture)
{
    public override string Name { get => TextureObject.DebugName; set => TextureObject.DebugName = value; }
    public readonly Texture3DDescription Description = texture.Description;

    public static Texture3D CreateTexture3D(Texture3DDescription description)
    {
        var dxTexture = new SharpDX.Direct3D11.Texture3D(ResourceManager.Device, description);
        return new Texture3D(dxTexture);
    }
}

public abstract class Texture<T>(T texture) : AbstractTexture(texture)
    where T : SharpDX.Direct3D11.Resource
{
    public static implicit operator T(Texture<T> texture) => texture.TextureObject;
    public static implicit operator SharpDX.Direct3D11.Resource?(Texture<T>? texture) => texture?.TextureObject;
    protected readonly T TextureObject = texture;
    public bool IsDisposed => TextureObject.IsDisposed;
}

public abstract class AbstractTexture(IDisposable disposable) : IDisposable
{
    private IDisposable? _disposable = disposable;
    public abstract string Name { get; set; }

    public static implicit operator SharpDX.Direct3D11.Resource?(AbstractTexture texture)
        => texture._disposable as SharpDX.Direct3D11.Resource;
    
    // The original implementation. Not sure, if the above is valid.
    // public static implicit operator SharpDX.Direct3D11.Resource(AbstractTexture texture) 
    //     => (SharpDX.Direct3D11.Resource)texture._disposable;

    public void Dispose()
    {
        _disposable?.Dispose();
        _disposable = null;
        GC.SuppressFinalize(this);
    }
    
    ~AbstractTexture()
    {
        Dispose();
    }
}

public static class TextureViews
{
    

    public static void CreateShaderResourceView<T>(this T resource, [NotNullWhen(true)] ref ShaderResourceView? shaderResourceView, string? name)
    where T : AbstractTexture
    {
        CreateTextureView(resource, ref shaderResourceView, 
                                    constructor: (device, texture) => new ShaderResourceView(device, texture), name: name);
    }

    public static void CreateRenderTargetView<T>(this T resource, ref RenderTargetView? renderTargetView, string? name)
        where T : AbstractTexture
    {
        CreateTextureView(resource, ref renderTargetView, 
                             constructor: (device, texture) => new RenderTargetView(device, texture), name: name);
    }

    public static void CreateUnorderedAccessView<T>(this T resource, ref UnorderedAccessView? unorderedAccessView, string? name)
        where T : AbstractTexture
    {
        CreateTextureView(resource, ref unorderedAccessView, 
                             constructor: (device, texture) => new UnorderedAccessView(device, texture), name: name);
    }
    
    private static void CreateTextureView<T>(AbstractTexture resource, ref T? view, Func<Device, AbstractTexture, T> constructor, string? name) where T : ResourceView
    {
        view?.Dispose();
        view = constructor(ResourceManager.Device, resource);
        view.DebugName = name;
    }
}