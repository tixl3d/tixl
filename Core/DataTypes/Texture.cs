#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.Versioning;
using JeremyAnsel.Media.Dds;
using StbImageSharp;
using T3.Graphics.Compat;
using T3.Graphics;
using SharpDX.WIC;
using T3.Core.Logging;
using T3.Core.Resource;
using T3.Core.Resource.Dds;
using Device = T3.Graphics.Compat.Device;

namespace T3.Core.DataTypes;

public sealed class Texture2D(T3.Graphics.Compat.Texture2D texture) : Texture<T3.Graphics.Compat.Texture2D>(texture)
{
    public override string Name { get => TextureObject.DebugName; set => TextureObject.DebugName = value; }
    public readonly Texture2DDescription Description = texture.Description;

    [SupportedOSPlatform("windows")]
    public static Texture2D CreateFromBitmap(Device device, BitmapSource bitmapSource)
    {
        var stride = bitmapSource.Size.Width * 4;
        // WIC writes into its own stream type, so this one stays SharpDX.
        using var buffer = new SharpDX.DataStream(bitmapSource.Size.Height * stride, true, true);
        bitmapSource.CopyPixels(stride, buffer);

        return CreateFromRgba(device, bitmapSource.Size.Width, bitmapSource.Size.Height, buffer.DataPointer);
    }

    /// <summary>
    /// Creates a texture from tightly packed RGBA8 pixels. The caller owns the memory; it is only read while
    /// this runs.
    /// </summary>
    public static unsafe Texture2D CreateFromRgba(Device device, int width, int height, ReadOnlySpan<byte> rgba)
    {
        fixed (byte* pixels = rgba)
        {
            return CreateFromRgba(device, width, height, (IntPtr)pixels);
        }
    }

    private static Texture2D CreateFromRgba(Device device, int width, int height, IntPtr rgba)
    {
        var stride = width * 4;
        var mipLevels = (int)Math.Log(width, 2.0) + 1;
        var texDesc = new Texture2DDescription
                          {
                              Width = width,
                              Height = height,
                              ArraySize = 1,
                              BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                              Usage = ResourceUsage.Default,
                              CpuAccessFlags = CpuAccessFlags.None,
                              Format = Format.R8G8B8A8_UNorm,
                              MipLevels = mipLevels,
                              OptionFlags = ResourceOptionFlags.GenerateMipMaps,
                              SampleDescription = new SampleDescription(1, 0),
                          };

        // Only level 0 has pixels; the coarser levels are filtered down from it on the GPU right away, so
        // anything that samples the image small (a thumbnail, a card, a minifying shader) sees the picture.
        var dataRectangles = new DataRectangle[mipLevels];
        for (var i = 0; i < mipLevels; i++)
        {
            dataRectangles[i] = new DataRectangle(rgba, stride);
            stride /= 2;
        }

        var dxTexture = new T3.Graphics.Compat.Texture2D(device, texDesc, dataRectangles);
        if (mipLevels > 1)
        {
            using var srv = new ShaderResourceView(device, dxTexture);
            device.ImmediateContext.GenerateMips(srv);
        }

        return new Texture2D(dxTexture);
    }

    public static Texture2D CreateTexture2D(Texture2DDescription description, DataRectangle[]? dataRectangles = null)
    {
        var dxTexture = new T3.Graphics.Compat.Texture2D(ResourceManager.Device, description, dataRectangles);
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
                var dxTex = (T3.Graphics.Compat.Texture2D)dxTextureResource;
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
            var image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (image.Width <= 0 || image.Height <= 0)
            {
                texture = null;
                failureReason = "Image has no pixels";
                return false;
            }

            texture = CreateFromRgba(ResourceManager.Device, image.Width, image.Height, image.Data);
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
public sealed class Texture3D(T3.Graphics.Compat.Texture3D texture) : Texture<T3.Graphics.Compat.Texture3D>(texture)
{
    public override string Name { get => TextureObject.DebugName; set => TextureObject.DebugName = value; }
    public readonly Texture3DDescription Description = texture.Description;

    public static Texture3D CreateTexture3D(Texture3DDescription description)
    {
        var dxTexture = new T3.Graphics.Compat.Texture3D(ResourceManager.Device, description);
        return new Texture3D(dxTexture);
    }
}

public abstract class Texture<T>(T texture) : AbstractTexture(texture)
    where T : T3.Graphics.Compat.Resource
{
    public static implicit operator T(Texture<T> texture) => texture.TextureObject;
    public static implicit operator T3.Graphics.Compat.Resource?(Texture<T>? texture) => texture?.TextureObject;
    protected readonly T TextureObject = texture;
    public bool IsDisposed => TextureObject.IsDisposed;
}

public abstract class AbstractTexture(IDisposable disposable) : IDisposable
{
    private IDisposable? _disposable = disposable;
    public abstract string Name { get; set; }

    public static implicit operator T3.Graphics.Compat.Resource?(AbstractTexture texture)
        => texture._disposable as T3.Graphics.Compat.Resource;
    
    // The original implementation. Not sure, if the above is valid.
    // public static implicit operator T3.Graphics.Compat.Resource(AbstractTexture texture) 
    //     => (T3.Graphics.Compat.Resource)texture._disposable;

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