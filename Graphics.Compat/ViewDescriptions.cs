using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// Fills in the view descriptions D3D11 derives from the resource when none is given, and reduces the
/// per-dimension union to the backend's flat view description.
/// </summary>
internal static class ViewDescriptions
{
    internal static ShaderResourceViewDescription DefaultShaderResourceView(Resource resource)
    {
        switch (resource)
        {
            case Texture2D texture:
            {
                var isCube = (texture.Description.OptionFlags & ResourceOptionFlags.TextureCube) != 0;
                var isArray = texture.Description.ArraySize > 1;
                return new ShaderResourceViewDescription
                           {
                               Format = texture.Description.Format,
                               Dimension = isCube
                                               ? ShaderResourceViewDimension.TextureCube
                                               : isArray
                                                   ? ShaderResourceViewDimension.Texture2DArray
                                                   : ShaderResourceViewDimension.Texture2D,
                               Texture2D = AllMips(texture.Description.MipLevels),
                               TextureCube = AllMips(texture.Description.MipLevels),
                               Texture2DArray = AllMipsAndSlices(texture.Description.MipLevels, texture.Description.ArraySize),
                               TextureCubeArray = AllMipsAndSlices(texture.Description.MipLevels, texture.Description.ArraySize),
                           };
            }

            case Texture3D texture:
                return new ShaderResourceViewDescription
                           {
                               Format = texture.Description.Format,
                               Dimension = ShaderResourceViewDimension.Texture3D,
                               Texture3D = new Texture3DResource { MostDetailedMip = 0, MipLevels = Math.Max(1, texture.Description.MipLevels) },
                           };

            case Texture1D texture:
                return new ShaderResourceViewDescription
                           {
                               Format = texture.Description.Format,
                               Dimension = texture.Description.ArraySize > 1
                                               ? ShaderResourceViewDimension.Texture1DArray
                                               : ShaderResourceViewDimension.Texture1D,
                               Texture1D = AllMips(texture.Description.MipLevels),
                               Texture1DArray = AllMipsAndSlices(texture.Description.MipLevels, texture.Description.ArraySize),
                           };

            case Buffer buffer:
                return new ShaderResourceViewDescription
                           {
                               Dimension = ShaderResourceViewDimension.ExtendedBuffer,
                               BufferEx = new ExtendedBufferResource { FirstElement = 0, ElementCount = ElementCount(buffer) },
                           };

            default:
                throw new ArgumentException($"Cannot derive a shader resource view for {resource.GetType().Name}");
        }
    }

    internal static RenderTargetViewDescription DefaultRenderTargetView(Resource resource)
    {
        return resource switch
                   {
                       Texture2D texture => new RenderTargetViewDescription
                                                {
                                                    Format = texture.Description.Format,
                                                    Dimension = texture.Description.ArraySize > 1
                                                                    ? RenderTargetViewDimension.Texture2DArray
                                                                    : RenderTargetViewDimension.Texture2D,
                                                    Texture2DArray = new TextureArrayResource { ArraySize = texture.Description.ArraySize },
                                                },
                       Texture3D texture => new RenderTargetViewDescription
                                                {
                                                    Format = texture.Description.Format,
                                                    Dimension = RenderTargetViewDimension.Texture3D,
                                                    Texture3D = new Texture3DResource { WSize = texture.Description.Depth },
                                                },
                       _ => throw new ArgumentException($"Cannot derive a render target view for {resource.GetType().Name}"),
                   };
    }

    internal static DepthStencilViewDescription DefaultDepthStencilView(Resource resource)
    {
        return resource switch
                   {
                       Texture2D texture => new DepthStencilViewDescription
                                                {
                                                    Format = texture.Description.Format,
                                                    Dimension = texture.Description.ArraySize > 1
                                                                    ? DepthStencilViewDimension.Texture2DArray
                                                                    : DepthStencilViewDimension.Texture2D,
                                                    Texture2DArray = new TextureArrayResource { ArraySize = texture.Description.ArraySize },
                                                },
                       _ => throw new ArgumentException($"Cannot derive a depth stencil view for {resource.GetType().Name}"),
                   };
    }

    internal static UnorderedAccessViewDescription DefaultUnorderedAccessView(Resource resource)
    {
        return resource switch
                   {
                       Texture2D texture => new UnorderedAccessViewDescription
                                                {
                                                    Format = texture.Description.Format,
                                                    Dimension = texture.Description.ArraySize > 1
                                                                    ? UnorderedAccessViewDimension.Texture2DArray
                                                                    : UnorderedAccessViewDimension.Texture2D,
                                                    Texture2DArray = new TextureArrayResource { ArraySize = texture.Description.ArraySize },
                                                },
                       Texture3D texture => new UnorderedAccessViewDescription
                                                {
                                                    Format = texture.Description.Format,
                                                    Dimension = UnorderedAccessViewDimension.Texture3D,
                                                    Texture3D = new Texture3DResource { WSize = texture.Description.Depth },
                                                },
                       Buffer buffer => new UnorderedAccessViewDescription
                                            {
                                                Dimension = UnorderedAccessViewDimension.Buffer,
                                                Buffer = new UnorderedAccessViewBufferResource { ElementCount = ElementCount(buffer) },
                                            },
                       _ => throw new ArgumentException($"Cannot derive an unordered access view for {resource.GetType().Name}"),
                   };
    }

    internal static TextureViewDescription ToTextureView(in ShaderResourceViewDescription description)
    {
        return description.Dimension switch
                   {
                       ShaderResourceViewDimension.Texture1D => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1D),
                       ShaderResourceViewDimension.Texture1DArray => Flatten(description.Format, TextureDimension.Texture1D,
                                                                             description.Texture1DArray),
                       ShaderResourceViewDimension.Texture3D => Flatten(description.Format, TextureDimension.Texture3D, description.Texture3D),
                       ShaderResourceViewDimension.TextureCube => Flatten(description.Format, TextureDimension.TextureCube, description.TextureCube),
                       ShaderResourceViewDimension.TextureCubeArray => Flatten(description.Format, TextureDimension.TextureCube,
                                                                               description.TextureCubeArray),
                       ShaderResourceViewDimension.Texture2DArray or ShaderResourceViewDimension.Texture2DMultisampledArray
                           => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2DArray),
                       _ => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2D),
                   };
    }

    internal static TextureViewDescription ToTextureView(in RenderTargetViewDescription description)
    {
        return description.Dimension switch
                   {
                       RenderTargetViewDimension.Texture1D      => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1D),
                       RenderTargetViewDimension.Texture1DArray => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1DArray),
                       RenderTargetViewDimension.Texture3D      => Flatten(description.Format, TextureDimension.Texture3D, description.Texture3D),
                       RenderTargetViewDimension.Texture2DArray or RenderTargetViewDimension.Texture2DMultisampledArray
                           => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2DArray),
                       _ => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2D),
                   };
    }

    internal static TextureViewDescription ToTextureView(in DepthStencilViewDescription description)
    {
        return description.Dimension switch
                   {
                       DepthStencilViewDimension.Texture1D      => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1D),
                       DepthStencilViewDimension.Texture1DArray => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1DArray),
                       DepthStencilViewDimension.Texture2DArray or DepthStencilViewDimension.Texture2DMultisampledArray
                           => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2DArray),
                       _ => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2D),
                   };
    }

    internal static TextureViewDescription ToTextureView(in UnorderedAccessViewDescription description)
    {
        return description.Dimension switch
                   {
                       UnorderedAccessViewDimension.Texture1D => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1D),
                       UnorderedAccessViewDimension.Texture1DArray => Flatten(description.Format, TextureDimension.Texture1D,
                                                                              description.Texture1DArray),
                       UnorderedAccessViewDimension.Texture3D      => Flatten(description.Format, TextureDimension.Texture3D, description.Texture3D),
                       UnorderedAccessViewDimension.Texture2DArray => Flatten(description.Format, TextureDimension.Texture2D,
                                                                              description.Texture2DArray),
                       _ => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2D),
                   };
    }

    /// <summary>Views of a buffer bind a byte range; the element size comes from the buffer's stride.</summary>
    internal static (int Offset, int Size) BufferRange(Buffer buffer, int firstElement, int elementCount)
    {
        var stride = buffer.Description.StructureByteStride > 0 ? buffer.Description.StructureByteStride : 4;
        return (firstElement * stride, elementCount * stride);
    }

    private static int ElementCount(Buffer buffer)
    {
        var stride = buffer.Description.StructureByteStride > 0 ? buffer.Description.StructureByteStride : 4;
        return buffer.Description.SizeInBytes / stride;
    }

    private static TextureResource AllMips(int mipLevels) => new() { MostDetailedMip = 0, MipLevels = Math.Max(1, mipLevels) };

    private static TextureArrayResource AllMipsAndSlices(int mipLevels, int arraySize)
        => new() { MostDetailedMip = 0, MipLevels = Math.Max(1, mipLevels), FirstArraySlice = 0, ArraySize = Math.Max(1, arraySize) };

    // The render-target and depth views use MipSlice, the shader views MostDetailedMip; a view with no mip
    // count set means "the rest", which the backend reads as a count of 0.
    private static TextureViewDescription Flatten(Format format, TextureDimension dimension, in TextureResource resource)
        => new()
               {
                   Format = format,
                   Dimension = dimension,
                   FirstMip = Math.Max(resource.MostDetailedMip, resource.MipSlice),
                   MipCount = Math.Max(0, resource.MipLevels),
                   FirstArraySlice = 0,
                   ArraySize = 1,
               };

    private static TextureViewDescription Flatten(Format format, TextureDimension dimension, in TextureArrayResource resource)
        => new()
               {
                   Format = format,
                   Dimension = dimension,
                   FirstMip = Math.Max(resource.MostDetailedMip, resource.MipSlice),
                   MipCount = Math.Max(0, resource.MipLevels),
                   FirstArraySlice = resource.FirstArraySlice,
                   ArraySize = Math.Max(1, resource.ArraySize),
               };

    private static TextureViewDescription Flatten(Format format, TextureDimension dimension, in Texture3DResource resource)
        => new()
               {
                   Format = format,
                   Dimension = dimension,
                   FirstMip = Math.Max(resource.MostDetailedMip, resource.MipSlice),
                   MipCount = Math.Max(0, resource.MipLevels),

                   // A 3D view's slices are depth slices, not array layers.
                   FirstArraySlice = resource.FirstWSlice,
                   ArraySize = Math.Max(1, resource.WSize),
               };
}
